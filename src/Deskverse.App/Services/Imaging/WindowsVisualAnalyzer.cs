namespace Deskverse.App.Services.Imaging;

using Deskverse.Core.Abstractions;
using Microsoft.Extensions.Logging;
using Windows.Graphics.Imaging;
using Windows.Media.Editing;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.Storage.Streams;

/// <summary>
/// Visual feature extraction (dominant color, brightness, edge density) using
/// Windows imaging. Analysis samples a downscaled frame, but reported dimensions
/// always reflect the original media so library metadata stays accurate.
/// </summary>
public sealed class WindowsVisualAnalyzer : IVisualAnalyzer
{
    private const int AnalysisMaxEdge = 256;

    private readonly ILogger<WindowsVisualAnalyzer> _logger;

    public WindowsVisualAnalyzer(ILogger<WindowsVisualAnalyzer> logger)
    {
        _logger = logger;
    }

    public async Task<VisualFeatures?> AnalyzeAsync(string absolutePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(absolutePath) || !File.Exists(absolutePath))
        {
            return null;
        }

        try
        {
            var isVideo = IsVideoPath(absolutePath);
            using var frame = isVideo
                ? await DecodeVideoFrameAsync(absolutePath, cancellationToken).ConfigureAwait(false)
                : null;

            SoftwareBitmap bitmap;
            int originalWidth;
            int originalHeight;

            if (isVideo)
            {
                if (frame is null)
                {
                    return null;
                }

                bitmap = await DecodeScaledAsync(frame).ConfigureAwait(false);
                (originalWidth, originalHeight) = await GetVideoDimensionsAsync(absolutePath, cancellationToken)
                    .ConfigureAwait(false)
                    ?? ((int)bitmap.PixelWidth, (int)bitmap.PixelHeight);
            }
            else
            {
                var file = await StorageFile.GetFileFromPathAsync(absolutePath)
                    .AsTask(cancellationToken)
                    .ConfigureAwait(false);
                using var stream = await file.OpenReadAsync().AsTask(cancellationToken).ConfigureAwait(false);
                var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(cancellationToken).ConfigureAwait(false);
                originalWidth = (int)decoder.PixelWidth;
                originalHeight = (int)decoder.PixelHeight;
                bitmap = await DecodeScaledAsync(stream).ConfigureAwait(false);
            }

            using (bitmap)
            {
                return AnalyzePixels(bitmap, originalWidth, originalHeight);
            }
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Visual analysis failed for {Path}", absolutePath);
            return null;
        }
    }

    private static VisualFeatures? AnalyzePixels(SoftwareBitmap bitmap, int originalWidth, int originalHeight)
    {
        var width = (int)bitmap.PixelWidth;
        var height = (int)bitmap.PixelHeight;
        if (width < 2 || height < 2)
        {
            return null;
        }

        var buffer = new Buffer((uint)(4L * width * height));
        bitmap.CopyToBuffer(buffer);
        var reader = DataReader.FromBuffer(buffer);
        var pixels = new byte[buffer.Length];
        reader.ReadBytes(pixels);

        // BGRA8 byte order in the buffer.
        var histogram = new int[4096];
        var sumR = new long[4096];
        var sumG = new long[4096];
        var sumB = new long[4096];
        var luminance = new double[width * height];

        long opacityCount = 0;
        for (var i = 0; i < width * height; i++)
        {
            var b = pixels[i * 4];
            var g = pixels[i * 4 + 1];
            var r = pixels[i * 4 + 2];
            var a = pixels[i * 4 + 3];

            luminance[i] = 0.2126 * r + 0.7152 * g + 0.0722 * b;

            if (a < 16)
            {
                continue;
            }

            opacityCount++;
            var bucket = ((r >> 4) << 8) | ((g >> 4) << 4) | (b >> 4);
            histogram[bucket]++;
            sumR[bucket] += r;
            sumG[bucket] += g;
            sumB[bucket] += b;
        }

        if (opacityCount == 0)
        {
            return null;
        }

        var bestBucket = 0;
        for (var i = 1; i < histogram.Length; i++)
        {
            if (histogram[i] > histogram[bestBucket])
            {
                bestBucket = i;
            }
        }

        var bestCount = Math.Max(1, histogram[bestBucket]);
        var dominantR = (int)Math.Clamp(sumR[bestBucket] / bestCount, 0, 255);
        var dominantG = (int)Math.Clamp(sumG[bestBucket] / bestCount, 0, 255);
        var dominantB = (int)Math.Clamp(sumB[bestBucket] / bestCount, 0, 255);
        var dominantHex = $"{dominantR:X2}{dominantG:X2}{dominantB:X2}";

        double brightnessSum = 0;
        for (var i = 0; i < luminance.Length; i++)
        {
            brightnessSum += luminance[i];
        }

        var brightness = Math.Clamp(brightnessSum / luminance.Length / 255, 0, 1);

        // Mean absolute gradient in both axes, normalized to [0,1].
        double gradientSum = 0;
        long gradientCount = 0;
        for (var y = 0; y < height; y++)
        {
            var row = y * width;
            for (var x = 0; x < width - 1; x++)
            {
                gradientSum += Math.Abs(luminance[row + x + 1] - luminance[row + x]);
                gradientCount++;
            }
        }

        for (var y = 0; y < height - 1; y++)
        {
            var row = y * width;
            for (var x = 0; x < width; x++)
            {
                gradientSum += Math.Abs(luminance[row + width + x] - luminance[row + x]);
                gradientCount++;
            }
        }

        var density = gradientCount == 0
            ? 0
            : Math.Clamp(gradientSum / gradientCount / 255, 0, 1);

        return new VisualFeatures(dominantHex, brightness, density, originalWidth, originalHeight);
    }

    private static async Task<SoftwareBitmap> DecodeScaledAsync(IRandomAccessStream stream)
    {
        var decoder = await BitmapDecoder.CreateAsync(stream).AsTask().ConfigureAwait(false);
        var scale = Math.Min(1, (double)AnalysisMaxEdge / Math.Max(decoder.PixelWidth, decoder.PixelHeight));
        var targetWidth = Math.Max(1, (int)Math.Round(decoder.PixelWidth * scale));
        var targetHeight = Math.Max(1, (int)Math.Round(decoder.PixelHeight * scale));
        var transform = new BitmapTransform { ScaledWidth = (uint)targetWidth, ScaledHeight = (uint)targetHeight };
        return await decoder
            .GetSoftwareBitmapAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Ignore,
                transform,
                ExifOrientationMode.RespectExifOrientation,
                ColorManagementMode.DoNotColorManage)
            .AsTask()
            .ConfigureAwait(false);
    }

    private static async Task<IRandomAccessStream?> DecodeVideoFrameAsync(string path, CancellationToken ct)
    {
        var file = await StorageFile.GetFileFromPathAsync(path).AsTask(ct).ConfigureAwait(false);
        using var clip = await MediaClip.CreateFromFileAsync(file).AsTask(ct).ConfigureAwait(false);
        return await clip.GetThumbnailAsync(TimeSpan.FromSeconds(1)).AsTask(ct).ConfigureAwait(false);
    }

    private static async Task<(int Width, int Height)?> GetVideoDimensionsAsync(string path, CancellationToken ct)
    {
        var file = await StorageFile.GetFileFromPathAsync(path).AsTask(ct).ConfigureAwait(false);
        var properties = await file.Properties.GetVideoPropertiesAsync().AsTask(ct).ConfigureAwait(false);
        return properties is { Width: > 0, Height: > 0 }
            ? ((int)properties.Width, (int)properties.Height)
            : null;
    }

    private static bool IsVideoPath(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".mp4" or ".webm" or ".mkv" or ".mov" or ".avi";
}
