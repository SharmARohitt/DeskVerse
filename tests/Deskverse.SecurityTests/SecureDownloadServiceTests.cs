namespace Deskverse.SecurityTests;

using System.Security.Cryptography;
using Deskverse.Core;
using Deskverse.Security;
using Deskverse.Security.FileValidation;
using Deskverse.Security.Network;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

/// <summary>
/// Download hardening: SSRF-blocked targets, refused redirects, size caps, and the
/// guarantee that an interrupted or rejected transfer never leaves a staged file behind.
/// </summary>
public sealed class SecureDownloadServiceTests : IDisposable
{
    private static readonly byte[] PngBytes = Png(8, 8);

    private readonly string _tempRoot;

    public SecureDownloadServiceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"dv-dl-{Guid.NewGuid():N}");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup of the temp sandbox.
        }
    }

    [Fact]
    public async Task ValidMedia_IsStagedWithHashAndSize()
    {
        var service = CreateService(_ => Response(PngBytes, "image/png"));
        var result = await service.DownloadAsync(new Uri("https://93.184.216.34/wall.png"), _tempRoot, ".png");

        Assert.True(result.Success, result.Error);
        var file = result.Value!;
        Assert.True(File.Exists(file.TempPath));
        Assert.Equal(PngBytes.LongLength, file.SizeBytes);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(PngBytes)), file.FileHash);
    }

    [Theory]
    [InlineData("https://127.0.0.1/wall.png")]
    [InlineData("https://10.0.0.5/wall.png")]
    [InlineData("https://192.168.0.7/wall.png")]
    [InlineData("https://172.16.3.9/wall.png")]
    [InlineData("https://169.254.169.254/latest/meta-data")]
    [InlineData("https://localhost/wall.png")]
    [InlineData("http://93.184.216.34/wall.png")]
    public async Task LoopbackPrivateInsecureOrMetadataTargets_AreBlockedBeforeTheRequest(string url)
    {
        var requested = false;
        var service = CreateService(_ => { requested = true; return Response(PngBytes, "image/png"); });

        var result = await service.DownloadAsync(new Uri(url), _tempRoot, ".png");

        Assert.False(result.Success);
        Assert.Contains("Blocked URL", result.Error);
        Assert.False(requested);
        Assert.True(!Directory.Exists(_tempRoot) || Directory.GetFiles(_tempRoot).Length == 0);
    }

    [Fact]
    public async Task RedirectResponse_IsRefusedAndLeavesNoStagedFile()
    {
        var response = new HttpResponseMessage(System.Net.HttpStatusCode.Found);
        response.Headers.Location = new Uri("https://169.254.169.254/latest/meta-data");

        var service = CreateService(_ => response);
        var result = await service.DownloadAsync(new Uri("https://93.184.216.34/wall.png"), _tempRoot, ".png");

        Assert.False(result.Success);
        Assert.Contains("Redirects are not followed", result.Error);
        Assert.Empty(Directory.GetFiles(_tempRoot));
    }

    [Fact]
    public async Task DeclaredSizeAboveTheLimit_IsRefusedWithoutDownloading()
    {
        var service = CreateService(
            _ => Response(new byte[4096], "image/png"),
            new SecurityOptions { MaxDownloadBytes = 1024 });

        var result = await service.DownloadAsync(new Uri("https://93.184.216.34/wall.png"), _tempRoot, ".png");

        Assert.False(result.Success);
        Assert.Contains("exceeds the download limit", result.Error);
    }

    [Fact]
    public async Task UndeclaredSizeAboveTheLimit_IsAbortedAndCleanedUp()
    {
        var content = new StreamContent(new NonSeekableStream(new byte[4096]));
        content.Headers.ContentLength = null;
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");

        var service = CreateService(
            _ => new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = content },
            new SecurityOptions { MaxDownloadBytes = 1024 });

        var result = await service.DownloadAsync(new Uri("https://93.184.216.34/wall.png"), _tempRoot, ".png");

        Assert.False(result.Success);
        Assert.Contains("exceeded the limit", result.Error);
        Assert.Empty(Directory.GetFiles(_tempRoot));
    }

    [Fact]
    public async Task EmptyBody_IsRefused()
    {
        var service = CreateService(_ => Response([], "image/png"));
        var result = await service.DownloadAsync(new Uri("https://93.184.216.34/wall.png"), _tempRoot, ".png");

        Assert.False(result.Success);
        Assert.Contains("empty body", result.Error);
        Assert.Empty(Directory.GetFiles(_tempRoot));
    }

    [Fact]
    public async Task DisguisedExecutablePayload_IsRefusedAfterSignatureCheck()
    {
        var exe = new byte[64];
        exe[0] = (byte)'M';
        exe[1] = (byte)'Z';

        var service = CreateService(_ => Response(exe, "application/octet-stream"));
        var result = await service.DownloadAsync(new Uri("https://93.184.216.34/wall.png"), _tempRoot, ".png");

        Assert.False(result.Success);
        Assert.Contains("not a supported media format", result.Error);
        Assert.Empty(Directory.GetFiles(_tempRoot));
    }

    [Theory]
    [InlineData("application/x-msdownload")]
    [InlineData("text/html")]
    [InlineData("application/json")]
    public async Task NonMediaContentType_IsRefused(string contentType)
    {
        var service = CreateService(_ => Response(PngBytes, contentType));
        var result = await service.DownloadAsync(new Uri("https://93.184.216.34/wall.png"), _tempRoot, ".png");

        Assert.False(result.Success);
        Assert.Contains("not accepted media", result.Error);
    }

    [Fact]
    public async Task TransportNeverFollowsRedirects()
    {
        using var handler = HardenedHttpClient.CreateHandler(TimeSpan.FromSeconds(5));
        Assert.False(handler.AllowAutoRedirect);
    }

    private SecureDownloadService CreateService(
        Func<System.Net.Http.HttpRequestMessage, HttpResponseMessage> respond,
        SecurityOptions? options = null)
    {
        var http = new HttpClient(new StubHandler(respond));
        return new SecureDownloadService(
            http,
            new UrlPolicy(Options.Create(options ?? new SecurityOptions())),
            Options.Create(options ?? new SecurityOptions()),
            NullLogger<SecureDownloadService>.Instance);
    }

    private static HttpResponseMessage Response(byte[] body, string contentType)
    {
        var content = new ByteArrayContent(body);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        return new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = content };
    }

    private static byte[] Png(int width, int height)
    {
        var bytes = new byte[33];
        ReadOnlySpan<byte> signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        signature.CopyTo(bytes);
        BitConverter.GetBytes(13).Reverse().ToArray().CopyTo(bytes.AsSpan(8, 4));
        "IHDR"u8.CopyTo(bytes.AsSpan(12));
        BitConverter.GetBytes(width).Reverse().ToArray().CopyTo(bytes.AsSpan(16, 4));
        BitConverter.GetBytes(height).Reverse().ToArray().CopyTo(bytes.AsSpan(20, 4));
        return bytes;
    }

    private sealed class StubHandler(Func<System.Net.Http.HttpRequestMessage, HttpResponseMessage> respond) : System.Net.Http.HttpMessageHandler
    {
        protected override Task<System.Net.Http.HttpResponseMessage> SendAsync(
            System.Net.Http.HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }

    private sealed class NonSeekableStream(byte[] data) : Stream
    {
        private readonly MemoryStream _inner = new(data);

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
