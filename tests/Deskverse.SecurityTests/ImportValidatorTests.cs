namespace Deskverse.SecurityTests;

using System.Security.Cryptography;
using Deskverse.Core;
using Deskverse.Security;
using Deskverse.Security.FileValidation;
using Microsoft.Extensions.Options;
using Xunit;

/// <summary>
/// Import validation: bytes are authoritative over extensions, executables and
/// oversized payloads are refused, and every rejection carries a reason.
/// </summary>
public sealed class ImportValidatorTests : IDisposable
{
    private readonly string _root;

    public ImportValidatorTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"dv-import-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort cleanup of the temp sandbox.
        }
    }

    [Fact]
    public async Task ValidPng_IsAcceptedWithDimensionsAndHash()
    {
        var path = Write("real.png", Png(1920, 1080));
        var result = await CreateValidator().ValidateFileAsync(path);

        Assert.True(result.Accepted, result.Detail);
        Assert.Equal(WallpaperKind.Static, result.Kind);
        Assert.Equal(WallpaperFormat.Png, result.Format);
        Assert.Equal(1920, result.Width);
        Assert.Equal(1080, result.Height);
        Assert.Equal(new FileInfo(path).Length, result.FileSizeBytes);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))), result.FileHash);
    }

    [Fact]
    public async Task SameContentTwice_ProducesTheSameHash()
    {
        var bytes = Png(64, 64);
        var first = await CreateValidator().ValidateFileAsync(Write("a.png", bytes));
        var second = await CreateValidator().ValidateFileAsync(Write("b.png", bytes));

        Assert.True(first.Accepted);
        Assert.True(second.Accepted);
        Assert.Equal(first.FileHash, second.FileHash);
    }

    [Fact]
    public async Task PngPayloadNamedAsJpeg_IsRejectedAsMislabeled()
    {
        var result = await CreateValidator().ValidateFileAsync(Write("lies.jpg", Png(320, 240)));

        Assert.False(result.Accepted);
        Assert.Equal(ImportRejectReason.SignatureMismatch, result.Reason);
    }

    [Fact]
    public async Task ExecutableRenamedAsPng_IsRejected()
    {
        var exe = new byte[128];
        exe[0] = (byte)'M';
        exe[1] = (byte)'Z';

        var result = await CreateValidator().ValidateFileAsync(Write("payload.png", exe));

        Assert.False(result.Accepted);
        Assert.True(
            result.Reason is ImportRejectReason.UnsupportedExtension or ImportRejectReason.SignatureMismatch,
            $"Expected a rejection reason, got {result.Reason}: {result.Detail}");
    }

    [Theory]
    [InlineData("wallpaper.png.exe")]
    [InlineData("wallpaper.PNG.EXE")]
    [InlineData("wallpaper.png.js")]
    [InlineData("wallpaper.ps1")]
    public async Task ExecutableOrDoubleExtension_IsNeverAccepted(string fileName)
    {
        var result = await CreateValidator().ValidateFileAsync(Write(fileName, Png(8, 8)));

        Assert.False(result.Accepted);
        Assert.Equal(ImportRejectReason.UnsupportedExtension, result.Reason);
    }

    [Fact]
    public async Task OversizedImage_IsRejectedAgainstTheConfiguredLimit()
    {
        var validator = CreateValidator(new SecurityOptions { MaxImageBytes = 32 });
        var result = await validator.ValidateFileAsync(Write("big.png", Png(8, 8)));

        Assert.False(result.Accepted);
        Assert.Equal(ImportRejectReason.FileTooLarge, result.Reason);
        Assert.Contains("bytes", result.Detail);
    }

    [Fact]
    public async Task EmptyFile_IsRejected()
    {
        var result = await CreateValidator().ValidateFileAsync(Write("empty.png", []));

        Assert.False(result.Accepted);
        Assert.Equal(ImportRejectReason.EmptyFile, result.Reason);
    }

    [Fact]
    public async Task MissingFile_IsRejected()
    {
        var result = await CreateValidator().ValidateFileAsync(Path.Combine(_root, "nope.png"));

        Assert.False(result.Accepted);
        Assert.Equal(ImportRejectReason.UnreadableFile, result.Reason);
    }

    [Fact]
    public async Task UnsupportedExtension_IsRejected()
    {
        var result = await CreateValidator().ValidateFileAsync(Write("notes.txt", "hello"u8.ToArray()));

        Assert.False(result.Accepted);
        Assert.Equal(ImportRejectReason.UnsupportedExtension, result.Reason);
    }

    [Fact]
    public async Task PathWithNullByte_IsRejected()
    {
        var result = await CreateValidator().ValidateFileAsync(Path.Combine(_root, "evil\0.png"));

        Assert.False(result.Accepted);
        Assert.Equal(ImportRejectReason.PathTraversal, result.Reason);
    }

    [Fact]
    public async Task TruncatedPngHeader_IsRejectedAsInvalidMedia()
    {
        // Correct signature and IHDR marker, but the size fields are zero.
        var bytes = Png(0, 0);
        var result = await CreateValidator().ValidateFileAsync(Write("zero.png", bytes));

        Assert.False(result.Accepted);
        Assert.Equal(ImportRejectReason.InvalidMedia, result.Reason);
    }

    [Fact]
    public async Task WebMContainerNamedMkv_IsAcceptedAsTheSameFamily()
    {
        var bytes = new byte[40];
        ReadOnlySpan<byte> ebml = [0x1A, 0x45, 0xDF, 0xA3];
        ebml.CopyTo(bytes.AsSpan(0, 4));
        "webm"u8.CopyTo(bytes.AsSpan(10, 4));

        var result = await CreateValidator().ValidateFileAsync(Write("clip.mkv", bytes));

        Assert.True(result.Accepted, result.Detail);
        Assert.Equal(WallpaperKind.Video, result.Kind);
    }

    private string Write(string name, byte[] bytes)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static ImportValidator CreateValidator(SecurityOptions? options = null) =>
        new(Options.Create(options ?? new SecurityOptions()));

    private static byte[] Png(int width, int height)
    {
        var bytes = new byte[40];
        ReadOnlySpan<byte> signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        signature.CopyTo(bytes);
        BitConverter.GetBytes(13).Reverse().ToArray().CopyTo(bytes.AsSpan(8, 4));
        "IHDR"u8.CopyTo(bytes.AsSpan(12));
        BitConverter.GetBytes(width).Reverse().ToArray().CopyTo(bytes.AsSpan(16, 4));
        BitConverter.GetBytes(height).Reverse().ToArray().CopyTo(bytes.AsSpan(20, 4));
        return bytes;
    }
}
