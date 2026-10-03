namespace Deskverse.Security.FileValidation;

using System.Security.Cryptography;
using Deskverse.Core;
using Microsoft.Extensions.Options;

public sealed record ImportValidationResult
{
    public bool Accepted { get; init; }

    public required string SourcePath { get; init; }

    public WallpaperKind Kind { get; init; }

    public WallpaperFormat Format { get; init; }

    public int Width { get; init; }

    public int Height { get; init; }

    public long FileSizeBytes { get; init; }

    public string FileHash { get; init; } = string.Empty;

    public ImportRejectReason Reason { get; init; } = ImportRejectReason.None;

    public string? Detail { get; init; }

    public static ImportValidationResult Reject(string path, ImportRejectReason reason, string detail) => new()
    {
        Accepted = false,
        SourcePath = path,
        Reason = reason,
        Detail = detail,
    };
}

/// <summary>
/// Full validation pipeline for candidate wallpaper files: extension policy,
/// size limits, magic-byte signature, header sanity, and content hash.
/// </summary>
public sealed class ImportValidator
{
    private readonly SecurityOptions _options;

    public ImportValidator(IOptions<SecurityOptions> options)
    {
        _options = options.Value;
    }

    public async Task<ImportValidationResult> ValidateFileAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(path);

        // Reject null bytes, which can truncate paths in some runtimes.
        if (path.Contains('\0'))
        {
            return ImportValidationResult.Reject(path, ImportRejectReason.PathTraversal, "Path contains a null byte.");
        }

        FileInfo info;
        try
        {
            info = new FileInfo(path);
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
        {
            return ImportValidationResult.Reject(path, ImportRejectReason.PathTraversal, ex.Message);
        }

        if (!info.Exists)
        {
            return ImportValidationResult.Reject(path, ImportRejectReason.UnreadableFile, "File does not exist.");
        }

        // Reject reparse points (symlinks/junctions) pointing outside the allowed tree.
        if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            return ImportValidationResult.Reject(path, ImportRejectReason.PathTraversal,
                "Reparse points (symlinks/junctions) are not accepted.");
        }

        var fileName = info.Name;
        // Reject double extensions that could disguise dangerous files (image.png.exe).
        var dotCount = fileName.Count(c => c == '.');
        if (dotCount > 1)
        {
            var allExtensions = fileName.Split('.').Skip(1).Select(e => "." + e).ToArray();
            foreach (var ext in allExtensions.Take(allExtensions.Length - 1))
            {
                if (MediaFileRules.IsBlockedExtension(ext))
                {
                    return ImportValidationResult.Reject(path, ImportRejectReason.UnsupportedExtension,
                        $"Double extension '{string.Join("", allExtensions)}' contains a blocked segment '{ext}'.");
                }
            }
        }

        var extension = info.Extension;
        if (MediaFileRules.IsBlockedExtension(extension))
        {
            return ImportValidationResult.Reject(path, ImportRejectReason.UnsupportedExtension,
                $"Executable or script files are never accepted: {extension}");
        }

        if (!MediaFileRules.TryGetFormat(extension, out var declaredFormat, out var kind))
        {
            return ImportValidationResult.Reject(path, ImportRejectReason.UnsupportedExtension,
                $"Unsupported wallpaper extension: {extension}");
        }

        var limit = kind == WallpaperKind.Video ? _options.MaxVideoBytes : _options.MaxImageBytes;
        if (info.Length == 0)
        {
            return ImportValidationResult.Reject(path, ImportRejectReason.EmptyFile, "File is empty.");
        }

        if (info.Length > limit)
        {
            return ImportValidationResult.Reject(path, ImportRejectReason.FileTooLarge,
                $"File is {info.Length:N0} bytes; the limit for {kind.ToString().ToLowerInvariant()} media is {limit:N0} bytes.");
        }

        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 81920, useAsync: true);

            var detected = await FileSignatureValidator.DetectFormatAsync(stream, cancellationToken).ConfigureAwait(false);
            if (detected == WallpaperFormat.Unknown)
            {
                return ImportValidationResult.Reject(path, ImportRejectReason.SignatureMismatch,
                    "File signature could not be recognized as supported media.");
            }

            // The signature is authoritative; a mismatch between it and the extension
            // means the file is mislabeled or disguised, so it is rejected.
            if (!SignaturesCompatible(detected, declaredFormat))
            {
                return ImportValidationResult.Reject(path, ImportRejectReason.SignatureMismatch,
                    $"Extension {extension} claims {declaredFormat} but the file signature is {detected}.");
            }

            int width = 0, height = 0;
            if (kind == WallpaperKind.Static)
            {
                if (!MediaProbe.TryReadImageDimensions(stream, detected, out width, out height))
                {
                    return ImportValidationResult.Reject(path, ImportRejectReason.InvalidMedia,
                        "Image header is malformed; dimensions could not be read.");
                }
            }

            string hash;
            try
            {
                stream.Position = 0;
                hash = await ComputeHashAsync(stream, cancellationToken).ConfigureAwait(false);
            }
            catch (IOException ex)
            {
                return ImportValidationResult.Reject(path, ImportRejectReason.UnreadableFile, ex.Message);
            }

            return new ImportValidationResult
            {
                Accepted = true,
                SourcePath = path,
                Kind = kind,
                Format = detected,
                Width = width,
                Height = height,
                FileSizeBytes = info.Length,
                FileHash = hash,
            };
        }
        catch (IOException ex)
        {
            return ImportValidationResult.Reject(path, ImportRejectReason.UnreadableFile, ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            return ImportValidationResult.Reject(path, ImportRejectReason.UnreadableFile, ex.Message);
        }
    }

    /// <summary>WebM and Matroska share a container family, as do MP4 variants; anything else must match exactly.</summary>
    private static bool SignaturesCompatible(WallpaperFormat detected, WallpaperFormat declared) => (detected, declared) switch
    {
        (WallpaperFormat.WebM, WallpaperFormat.Matroska) => true,
        (WallpaperFormat.Matroska, WallpaperFormat.WebM) => true,
        (WallpaperFormat.Mp4, WallpaperFormat.QuickTimeMov) => true,
        (WallpaperFormat.QuickTimeMov, WallpaperFormat.Mp4) => true,
        _ => detected == declared,
    };

    internal static async Task<string> ComputeHashAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash);
    }
}
