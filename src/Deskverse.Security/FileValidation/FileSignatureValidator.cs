namespace Deskverse.Security.FileValidation;

using Deskverse.Core;

/// <summary>
/// Sniffs the true file type from leading bytes. Extensions are advisory only;
/// the signature decides what a file actually is.
/// </summary>
public static class FileSignatureValidator
{
    private const int ProbeLength = 32;

    /// <summary>Detects the real format from the file's leading bytes.</summary>
    public static WallpaperFormat DetectFormat(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (!stream.CanRead)
        {
            return WallpaperFormat.Unknown;
        }

        var origin = stream.Position;
        try
        {
            var buffer = new byte[ProbeLength];
            var read = ReadAtMost(stream, buffer);
            return DetectFormat(buffer.AsSpan(0, read));
        }
        finally
        {
            stream.Position = origin;
        }
    }

    public static async Task<WallpaperFormat> DetectFormatAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (!stream.CanRead)
        {
            return WallpaperFormat.Unknown;
        }

        var origin = stream.Position;
        try
        {
            var buffer = new byte[ProbeLength];
            var read = 0;
            while (read < buffer.Length)
            {
                var chunk = await stream.ReadAsync(buffer.AsMemory(read), cancellationToken).ConfigureAwait(false);
                if (chunk == 0)
                {
                    break;
                }

                read += chunk;
            }

            return DetectFormat(buffer.AsSpan(0, read));
        }
        finally
        {
            stream.Position = origin;
        }
    }

    public static WallpaperFormat DetectFormat(ReadOnlySpan<byte> header)
    {
        if (header.Length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
        {
            return WallpaperFormat.Jpeg;
        }

        if (header.Length >= 8
            && header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47
            && header[4] == 0x0D && header[5] == 0x0A && header[6] == 0x1A && header[7] == 0x0A)
        {
            return WallpaperFormat.Png;
        }

        if (header.Length >= 2 && header[0] == (byte)'B' && header[1] == (byte)'M')
        {
            return WallpaperFormat.Bmp;
        }

        if (header.Length >= 6
            && header[0] == (byte)'G' && header[1] == (byte)'I' && header[2] == (byte)'F')
        {
            return WallpaperFormat.Gif;
        }

        if (header.Length >= 12 && IsRiff(header))
        {
            if (BytesEqual(header.Slice(8, 4), "WEBP"u8))
            {
                return WallpaperFormat.WebP;
            }

            if (BytesEqual(header.Slice(8, 4), "AVI "u8))
            {
                return WallpaperFormat.Avi;
            }
        }

        // EBML header shared by WebM and Matroska.
        if (header.Length >= 4 && header[0] == 0x1A && header[1] == 0x45 && header[2] == 0xDF && header[3] == 0xA3)
        {
            // DocType appears a bit later; treat both as Matroska family. WebM is a
            // Matroska subset, so callers should accept either playback-wise.
            return IsWebMDocType(header) ? WallpaperFormat.WebM : WallpaperFormat.Matroska;
        }

        // ISO base media file format (MP4 family) starts with size then "ftyp".
        if (header.Length >= 12 && BytesEqual(header.Slice(4, 4), "ftyp"u8))
        {
            var brand = header.Slice(8, 4);
            if (BytesEqual(brand, "qt  "u8))
            {
                return WallpaperFormat.QuickTimeMov;
            }

            return WallpaperFormat.Mp4;
        }

        return WallpaperFormat.Unknown;
    }

    private static bool IsWebMDocType(ReadOnlySpan<byte> header)
    {
        // DocType string lives inside the EBML header, commonly within the first
        // 32 bytes; look for "webm" to distinguish from generic Matroska.
        return header.IndexOf("webm"u8) >= 0;
    }

    private static bool IsRiff(ReadOnlySpan<byte> header) =>
        BytesEqual(header.Slice(0, 4), "RIFF"u8);

    private static bool BytesEqual(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right) => left.SequenceEqual(right);

    private static int ReadAtMost(Stream stream, byte[] buffer)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var chunk = stream.Read(buffer, read, buffer.Length - read);
            if (chunk == 0)
            {
                break;
            }

            read += chunk;
        }

        return read;
    }

    /// <summary>Checks that a detected signature is consistent with the claimed extension.</summary>
    public static bool ExtensionMatches(WallpaperFormat detected, string extension)
    {
        if (!MediaFileRules.TryGetFormat(extension, out _, out _))
        {
            return false;
        }

        return detected switch
        {
            WallpaperFormat.Unknown => false,
            WallpaperFormat.Matroska or WallpaperFormat.WebM =>
                MediaFileRules.TryGetFormat(extension, out var extFormat, out _)
                && extFormat is WallpaperFormat.WebM or WallpaperFormat.Matroska,
            _ => MediaFileRules.TryGetFormat(extension, out var extFormat, out _)
                && (extFormat == detected || (detected == WallpaperFormat.Mp4 && extFormat == WallpaperFormat.Mp4)),
        };
    }
}
