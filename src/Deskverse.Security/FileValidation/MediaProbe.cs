namespace Deskverse.Security.FileValidation;

using Deskverse.Core;

/// <summary>
/// Parses real pixel dimensions out of image headers so corrupt or malformed
/// media can be rejected before it ever reaches the cache.
/// </summary>
public static class MediaProbe
{
    /// <summary>Reads dimensions for supported static formats. Returns false when the header is malformed.</summary>
    public static bool TryReadImageDimensions(Stream stream, WallpaperFormat format, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (!stream.CanRead)
        {
            return false;
        }

        var origin = stream.Position;
        try
        {
            stream.Position = 0;
            return format switch
            {
                WallpaperFormat.Png => TryReadPng(stream, ref width, ref height),
                WallpaperFormat.Bmp => TryReadBmp(stream, ref width, ref height),
                WallpaperFormat.Gif => TryReadGif(stream, ref width, ref height),
                WallpaperFormat.Jpeg => TryReadJpeg(stream, ref width, ref height),
                WallpaperFormat.WebP => TryReadWebP(stream, ref width, ref height),
                _ => false,
            };
        }
        finally
        {
            stream.Position = origin;
        }
    }

    private static bool TryReadPng(Stream stream, ref int width, ref int height)
    {
        var header = new byte[24];
        if (ReadAll(stream, header) < 24 || !header.AsSpan(12, 4).SequenceEqual("IHDR"u8))
        {
            return false;
        }

        width = ReadInt32BigEndian(header, 16);
        height = ReadInt32BigEndian(header, 20);
        return width > 0 && height > 0;
    }

    private static bool TryReadBmp(Stream stream, ref int width, ref int height)
    {
        var header = new byte[26];
        if (ReadAll(stream, header) < 26)
        {
            return false;
        }

        width = ReadInt32LittleEndian(header, 18);
        height = Math.Abs(ReadInt32LittleEndian(header, 22));
        return width > 0 && height > 0;
    }

    private static bool TryReadGif(Stream stream, ref int width, ref int height)
    {
        var header = new byte[10];
        if (ReadAll(stream, header) < 10)
        {
            return false;
        }

        width = ReadUInt16LittleEndian(header, 6);
        height = ReadUInt16LittleEndian(header, 8);
        return width > 0 && height > 0;
    }

    private static bool TryReadJpeg(Stream stream, ref int width, ref int height)
    {
        // Walk JPEG markers until a start-of-frame segment is found.
        if (stream.ReadByte() != 0xFF || stream.ReadByte() != 0xD8)
        {
            return false;
        }

        while (true)
        {
            int marker = stream.ReadByte();
            if (marker != 0xFF)
            {
                return false;
            }

            // Skip fill bytes.
            while (marker == 0xFF)
            {
                marker = stream.ReadByte();
            }

            if (marker is 0xD8 or 0x01 or (>= 0xD0 and <= 0xD7))
            {
                continue;
            }

            if (marker == 0xD9)
            {
                return false;
            }

            var lengthBytes = new byte[2];
            if (ReadAll(stream, lengthBytes) < 2)
            {
                return false;
            }

            var segmentLength = ReadUInt16BigEndian(lengthBytes, 0);
            if (segmentLength < 2)
            {
                return false;
            }

            if (IsStartOfFrame(marker))
            {
                var frame = new byte[Math.Min((int)segmentLength, 8)];
                if (ReadAll(stream, frame) < frame.Length || frame.Length < 5)
                {
                    return false;
                }

                height = ReadUInt16BigEndian(frame, 1);
                width = ReadUInt16BigEndian(frame, 3);
                return width > 0 && height > 0;
            }

            stream.Seek(segmentLength - 2, SeekOrigin.Current);
        }
    }

    private static bool TryReadWebP(Stream stream, ref int width, ref int height)
    {
        var riff = new byte[30];
        if (ReadAll(stream, riff) < 12)
        {
            return false;
        }

        var chunk = riff.AsSpan(12, 4);
        if (chunk.SequenceEqual("VP8X"u8))
        {
            if (ReadAll(stream, riff, 12, 30) < 30)
            {
                return false;
            }

            width = ReadUInt24LittleEndian(riff, 24) + 1;
            height = ReadUInt24LittleEndian(riff, 27) + 1;
            return width > 0 && height > 0;
        }

        if (chunk.SequenceEqual("VP8L"u8))
        {
            if (ReadAll(stream, riff, 12, 25) < 25)
            {
                return false;
            }

            // 32-bit LE bitstream: 14 bits width-1, 14 bits height-1 after a signature byte.
            var bits = ReadUInt32LittleEndian(riff, 21);
            width = (int)((bits & 0x3FFF) + 1);
            height = (int)(((bits >> 14) & 0x3FFF) + 1);
            return width > 0 && height > 0;
        }

        return false;
    }

    private static bool IsStartOfFrame(int marker) =>
        marker is (>= 0xC0 and <= 0xCF) and not 0xC4 and not 0xC8 and not 0xCC;

    private static int ReadAll(Stream stream, byte[] buffer, int offset = 0, int limit = -1)
    {
        var end = limit < 0 ? buffer.Length : Math.Min(limit, buffer.Length);
        var read = offset;
        while (read < end)
        {
            var chunk = stream.Read(buffer, read, end - read);
            if (chunk == 0)
            {
                break;
            }

            read += chunk;
        }

        return read;
    }

    private static int ReadInt32BigEndian(byte[] b, int i) =>
        (b[i] << 24) | (b[i + 1] << 16) | (b[i + 2] << 8) | b[i + 3];

    private static int ReadInt32LittleEndian(byte[] b, int i) =>
        b[i] | (b[i + 1] << 8) | (b[i + 2] << 16) | (b[i + 3] << 24);

    private static ushort ReadUInt16LittleEndian(byte[] b, int i) => (ushort)(b[i] | (b[i + 1] << 8));

    private static ushort ReadUInt16BigEndian(byte[] b, int i) => (ushort)((b[i] << 8) | b[i + 1]);

    private static int ReadUInt24LittleEndian(byte[] b, int i) => b[i] | (b[i + 1] << 8) | (b[i + 2] << 16);

    private static uint ReadUInt32LittleEndian(byte[] b, int i) =>
        (uint)(b[i] | (b[i + 1] << 8) | (b[i + 2] << 16) | (b[i + 3] << 24));
}
