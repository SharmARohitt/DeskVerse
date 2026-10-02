namespace Deskverse.SecurityTests;

using Deskverse.Core;
using Deskverse.Security.FileValidation;
using Xunit;

public sealed class FileSignatureTests
{
    [Fact]
    public void DetectFormat_JpegBytes_ReturnsJpeg()
    {
        // JPEG starts with FF D8 FF
        byte[] jpegMagic = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46];
        var format = FileSignatureValidator.DetectFormat(jpegMagic);
        Assert.Equal(WallpaperFormat.Jpeg, format);
    }

    [Fact]
    public void DetectFormat_PngBytes_ReturnsPng()
    {
        // PNG: 89 50 4E 47 0D 0A 1A 0A
        byte[] pngMagic = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00];
        var format = FileSignatureValidator.DetectFormat(pngMagic);
        Assert.Equal(WallpaperFormat.Png, format);
    }

    [Fact]
    public void DetectFormat_GifBytes_ReturnsGif()
    {
        // GIF: 47 49 46 38
        byte[] gifMagic = [(byte)'G', (byte)'I', (byte)'F', (byte)'8', (byte)'9', (byte)'a'];
        var format = FileSignatureValidator.DetectFormat(gifMagic);
        Assert.Equal(WallpaperFormat.Gif, format);
    }

    [Fact]
    public void DetectFormat_BmpBytes_ReturnsBmp()
    {
        byte[] bmpMagic = [(byte)'B', (byte)'M', 0x36, 0x04, 0x00, 0x00];
        var format = FileSignatureValidator.DetectFormat(bmpMagic);
        Assert.Equal(WallpaperFormat.Bmp, format);
    }

    [Fact]
    public void DetectFormat_UnknownBytes_ReturnsUnknown()
    {
        byte[] garbage = [0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07];
        var format = FileSignatureValidator.DetectFormat(garbage);
        Assert.Equal(WallpaperFormat.Unknown, format);
    }

    [Fact]
    public void DetectFormat_ExeBytes_ReturnsUnknown()
    {
        // PE / MZ header: 4D 5A
        byte[] exeMagic = [0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00];
        var format = FileSignatureValidator.DetectFormat(exeMagic);
        Assert.Equal(WallpaperFormat.Unknown, format);
    }

    [Fact]
    public void DetectFormat_EmptyBytes_ReturnsUnknown()
    {
        var format = FileSignatureValidator.DetectFormat(ReadOnlySpan<byte>.Empty);
        Assert.Equal(WallpaperFormat.Unknown, format);
    }

    [Fact]
    public void DetectFormat_WebpBytes_ReturnsWebp()
    {
        // RIFF????WEBP
        byte[] webpMagic = new byte[32];
        System.Text.Encoding.ASCII.GetBytes("RIFF").CopyTo(webpMagic, 0);
        webpMagic[4] = 0x20; webpMagic[5] = 0x00; webpMagic[6] = 0x00; webpMagic[7] = 0x00;
        System.Text.Encoding.ASCII.GetBytes("WEBP").CopyTo(webpMagic, 8);
        var format = FileSignatureValidator.DetectFormat(webpMagic);
        Assert.Equal(WallpaperFormat.WebP, format);
    }

    [Fact]
    public async Task DetectFormatAsync_StreamAtNonZeroPosition_ReadsFromCurrentAndRestores()
    {
        byte[] pngData = [0x00, 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00];
        using var stream = new MemoryStream(pngData);
        stream.Position = 1; // skip the leading 0x00

        var format = await FileSignatureValidator.DetectFormatAsync(stream);
        Assert.Equal(WallpaperFormat.Png, format);
        // Position should be restored to where we were before the call.
        Assert.Equal(1, stream.Position);
    }

    [Fact]
    public void MediaFileRules_BlockedExtensions_ContainCommonExecutables()
    {
        Assert.True(MediaFileRules.IsBlockedExtension(".exe"));
        Assert.True(MediaFileRules.IsBlockedExtension(".EXE")); // case insensitive
        Assert.True(MediaFileRules.IsBlockedExtension(".dll"));
        Assert.True(MediaFileRules.IsBlockedExtension(".bat"));
        Assert.True(MediaFileRules.IsBlockedExtension(".ps1"));
        Assert.True(MediaFileRules.IsBlockedExtension(".js"));
    }

    [Fact]
    public void MediaFileRules_ValidImageExtensions_NotBlocked()
    {
        Assert.False(MediaFileRules.IsBlockedExtension(".jpg"));
        Assert.False(MediaFileRules.IsBlockedExtension(".png"));
        Assert.False(MediaFileRules.IsBlockedExtension(".gif"));
        Assert.False(MediaFileRules.IsBlockedExtension(".webp"));
    }
}
