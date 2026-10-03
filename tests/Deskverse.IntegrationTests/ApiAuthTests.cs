namespace Deskverse.IntegrationTests;

using System.Net;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using Deskverse.Api;
using Deskverse.Core.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

/// <summary>
/// The local API must stay loopback-only and token-gated, and rotating the token
/// must invalidate the previous secret. DPAPI-backed storage requires Windows.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ApiAuthTests : IDisposable
{
    private readonly string _root;

    public ApiAuthTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"dv-api-{Guid.NewGuid():N}");
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
    public async Task MissingToken_IsRejectedWith401()
    {
        var (status, passed, body) = await RunAsync("/api/v1/health", authorization: null);

        Assert.Equal(StatusCodes.Status401Unauthorized, status);
        Assert.False(passed);
        Assert.Contains("bearer token", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WrongToken_IsRejectedWith401AndNeverEchoesTheSecret()
    {
        var tokens = CreateTokenStore();
        var (status, passed, body) = await RunAsync("/api/v1/health", $"Bearer {new string('a', 64)}", tokens);

        Assert.Equal(StatusCodes.Status401Unauthorized, status);
        Assert.False(passed);
        Assert.DoesNotContain(tokens.TokenHex, body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ValidToken_IsAccepted()
    {
        var tokens = CreateTokenStore();
        var (status, passed, _) = await RunAsync("/api/v1/health", $"Bearer {tokens.TokenHex}", tokens);

        Assert.True(passed);
        Assert.Equal(StatusCodes.Status200OK, status);
    }

    [Theory]
    [InlineData("not-hex-at-all")]
    [InlineData("")]
    [InlineData("00112233")]
    public async Task MalformedOrShortToken_IsRejected(string presented)
    {
        var tokens = CreateTokenStore();
        var (status, passed, _) = await RunAsync("/api/v1/health", $"Bearer {presented}", tokens);

        Assert.Equal(StatusCodes.Status401Unauthorized, status);
        Assert.False(passed);
    }

    [Theory]
    [InlineData(4, 4, 4, 4)]
    [InlineData(192, 168, 1, 55)]
    [InlineData(169, 254, 169, 254)]
    public async Task NonLoopbackClient_IsForbiddenEvenWithAValidToken(byte a, byte b, byte c, byte d)
    {
        var tokens = CreateTokenStore();
        var (status, passed, _) = await RunAsync(
            "/api/v1/health",
            $"Bearer {tokens.TokenHex}",
            tokens,
            remote: new IPAddress([a, b, c, d]));

        Assert.Equal(StatusCodes.Status403Forbidden, status);
        Assert.False(passed);
    }

    [Fact]
    public async Task UnknownRemoteAddress_IsForbidden()
    {
        var tokens = CreateTokenStore();
        var (status, passed, _) = await RunAsync(
            "/api/v1/health",
            $"Bearer {tokens.TokenHex}",
            tokens,
            omitRemoteAddress: true);

        Assert.Equal(StatusCodes.Status403Forbidden, status);
        Assert.False(passed);
    }

    [Fact]
    public async Task Ipv6LoopbackClient_IsAccepted()
    {
        var tokens = CreateTokenStore();
        var (_, passed, _) = await RunAsync("/api/v1/health", $"Bearer {tokens.TokenHex}", tokens, remote: IPAddress.IPv6Loopback);

        Assert.True(passed);
    }

    [Fact]
    public async Task PathsOutsideApi_AreNotServedByTheTokenGate()
    {
        var tokens = CreateTokenStore();
        var (_, passed, _) = await RunAsync("/index.html", authorization: null, tokens);

        // Nothing is mapped outside /api, but the gate must not claim those paths.
        Assert.True(passed);
    }

    [Fact]
    public void GeneratedToken_HasExpectedLengthAndPersistsEncrypted()
    {
        var store = CreateTokenStore();

        Assert.Equal(64, store.TokenHex.Length);
        Assert.True(Convert.FromHexString(store.TokenHex).Length == 32);

        var blob = File.ReadAllBytes(Path.Combine(_root, "api-token.bin"));
        Assert.False(blob.AsSpan().IndexOf(Convert.FromHexString(store.TokenHex)) >= 0);
    }

    [Fact]
    public void Rotate_InvalidatesTheOldTokenAndAcceptsTheNewOne()
    {
        var store = CreateTokenStore();
        var original = store.TokenHex;
        Assert.True(store.IsValid(original));

        store.Rotate();
        var rotated = store.TokenHex;

        Assert.NotEqual(original, rotated);
        Assert.True(store.IsValid(rotated), "The newly generated token must be accepted.");
        Assert.False(store.IsValid(original), "The previous token must stop working immediately.");
    }

    [Fact]
    public void Rotate_PersistsTheReplacementSoRestartKeepsIt()
    {
        var store = CreateTokenStore();
        store.Rotate();
        var rotated = store.TokenHex;

        var reopened = CreateTokenStore();
        Assert.Equal(rotated, reopened.TokenHex);
        Assert.True(reopened.IsValid(rotated));
    }

    private ApiTokenStore CreateTokenStore() =>
        new(new TestEnvironment(_root), NullLogger<ApiTokenStore>.Instance);

    private static IServiceProvider BuildServices() =>
        new ServiceCollection()
            .AddOptions()
            .Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(_ => { })
            .BuildServiceProvider();

    private static async Task<(int StatusCode, bool NextCalled, string Body)> RunAsync(
        string path,
        string? authorization,
        ApiTokenStore? tokens = null,
        IPAddress? remote = null,
        bool omitRemoteAddress = false)
    {
        tokens ??= new ApiTokenStore(new TestEnvironment(Path.Combine(Path.GetTempPath(), $"dv-api-{Guid.NewGuid():N}")), NullLogger<ApiTokenStore>.Instance);

        var nextCalled = false;
        var middleware = new ApiAuthenticationMiddleware(
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            tokens);

        var context = new DefaultHttpContext { RequestServices = BuildServices() };
        if (!omitRemoteAddress)
        {
            context.Connection.RemoteIpAddress = remote ?? IPAddress.Loopback;
        }

        context.Request.Path = path;
        if (authorization is not null)
        {
            context.Request.Headers.Authorization = authorization;
        }

        using var body = new MemoryStream();
        context.Response.Body = body;

        await middleware.InvokeAsync(context);

        body.Position = 0;
        var text = new StreamReader(body).ReadToEnd();
        return (context.Response.StatusCode, nextCalled, text);
    }

    private sealed record TestEnvironment(string Root) : IAppEnvironment
    {
        public string DataRoot => Root;
        public string DefaultCacheDirectory => Path.Combine(Root, "cache");
        public string DatabasePath => Path.Combine(Root, "deskverse.db");
        public string LogsDirectory => Path.Combine(Root, "logs");
        public string ThumbnailsDirectory => Path.Combine(Root, "thumbs");
        public string TempDirectory => Path.Combine(Root, "tmp");
    }
}
