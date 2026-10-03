namespace Deskverse.App.Services.Imaging;

using Deskverse.Core;
using Deskverse.Core.Abstractions;
using Deskverse.Core.Entities;
using Deskverse.Storage;
using Microsoft.Extensions.Logging;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.Storage.Streams;

/// <summary>
/// PNG thumbnail generation via Windows imaging APIs. Decoding is bounded by a
/// semaphore so a large grid cannot spawn unbounded concurrent decoders, and all
/// work happens off the UI thread.
/// </summary>
public sealed class WindowsThumbnailService : IThumbnailService
{
    private const int MaxEdge = 480;

    private readonly IAppEnvironment _environment;
    private readonly CacheManager _cacheManager;
    private readonly ILogger<WindowsThumbnailService> _logger;
    private readonly SemaphoreSlim _pipeline = new(2, 2);

    public WindowsThumbnailService(
        IAppEnvironment environment,
        CacheManager cacheManager,
        ILogger<WindowsThumbnailService> logger)
    {
        _environment = environment;
        _cacheManager = cacheManager;
        _logger = logger;
    }

    public async Task<string?> GetOrCreateThumbnailAsync(Wallpaper wallpaper, CancellationToken cancellationToken = default)
    {
        if (!wallpaper.IsCached || string.IsNullOrEmpty(wallpaper.CacheRelativePath))
        {
            return null;
        }

        try
        {
            var thumbnailPath = Path.Combine(_environment.ThumbnailsDirectory, $"{wallpaper.Id:N}.png");
            if (File.Exists(thumbnailPath))
            {
                return thumbnailPath;
            }

            var sourcePath = _cacheManager.ResolveCachePath(wallpaper.CacheRelativePath);
            if (sourcePath is null || !File.Exists(sourcePath))
            {
                return null;
            }

            await _pipeline.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (File.Exists(thumbnailPath))
                {
                    return thumbnailPath;
                }

                await GenerateAsync(sourcePath, thumbnailPath, wallpaper.Kind == WallpaperKind.Video, cancellationToken)
                    .ConfigureAwait(false);
                return thumbnailPath;
            }
            finally
            {
                _pipeline.Release();
            }
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Thumbnail generation failed for {WallpaperId}", wallpaper.Id);
            return null;
        }
    }

    public async Task InvalidateAsync(Guid wallpaperId, CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(_environment.ThumbnailsDirectory, $"{wallpaperId:N}.png");
        await Task.Run(() =>
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (IOException)
            {
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    private static async Task GenerateAsync(string sourcePath, string targetPath, bool isVideo, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);

        SoftwareBitmap frame;
        if (isVideo)
        {
            var file = await StorageFile.GetFileFromPathAsync(sourcePath).AsTask(ct).ConfigureAwait(false);
            using var poster = await file
                .GetThumbnailAsync(ThumbnailMode.VideosView, (uint)MaxEdge)
                .AsTask(ct)
                .ConfigureAwait(false);
            if (poster is null)
            {
                throw new InvalidOperationException("The video has no decodable poster frame.");
            }

            frame = await DecodeScaledAsync(poster, MaxEdge).ConfigureAwait(false);
        }
        else
        {
            using var source = await OpenReadAsync(sourcePath, ct).ConfigureAwait(false);
            frame = await DecodeScaledAsync(source, MaxEdge).ConfigureAwait(false);
        }

        await EncodePngAsync(frame, targetPath, ct).ConfigureAwait(false);
    }

    private static async Task<SoftwareBitmap> DecodeScaledAsync(IRandomAccessStream stream, int maxEdge)
    {
        var decoder = await BitmapDecoder.CreateAsync(stream).AsTask().ConfigureAwait(false);
        var (targetWidth, targetHeight) = ScaleDimensions((int)decoder.PixelWidth, (int)decoder.PixelHeight, maxEdge);
        var transform = new BitmapTransform { ScaledWidth = (uint)targetWidth, ScaledHeight = (uint)targetHeight };
        return await decoder
            .GetSoftwareBitmapAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied,
                transform,
                ExifOrientationMode.RespectExifOrientation,
                ColorManagementMode.DoNotColorManage)
            .AsTask()
            .ConfigureAwait(false);
    }

    private static async Task EncodePngAsync(SoftwareBitmap bitmap, string targetPath, CancellationToken ct)
    {
        var folder = await StorageFolder.GetFolderFromPathAsync(Path.GetDirectoryName(targetPath)!)
            .AsTask(ct)
            .ConfigureAwait(false);
        var file = await folder
            .CreateFileAsync(Path.GetFileName(targetPath), CreationCollisionOption.ReplaceExisting)
            .AsTask(ct)
            .ConfigureAwait(false);
        using var output = await file.OpenAsync(FileAccessMode.ReadWrite).AsTask(ct).ConfigureAwait(false);
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, output).AsTask(ct).ConfigureAwait(false);
        encoder.SetSoftwareBitmap(bitmap);
        await encoder.FlushAsync().AsTask(ct).ConfigureAwait(false);
    }

    private static async Task<IRandomAccessStreamWithContentType> OpenReadAsync(string path, CancellationToken ct)
    {
        var file = await StorageFile.GetFileFromPathAsync(path).AsTask(ct).ConfigureAwait(false);
        return await file.OpenReadAsync().AsTask(ct).ConfigureAwait(false);
    }

    private static (int Width, int Height) ScaleDimensions(int width, int height, int maxEdge)
    {
        if (width <= 0 || height <= 0)
        {
            return (1, 1);
        }

        var longest = Math.Max(width, height);
        if (longest <= maxEdge)
        {
            return (width, height);
        }

        var scale = (double)maxEdge / longest;
        return (Math.Max(1, (int)Math.Round(width * scale)), Math.Max(1, (int)Math.Round(height * scale)));
    }
}
