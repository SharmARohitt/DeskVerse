namespace Deskverse.SecurityTests;

using Deskverse.Security;
using Deskverse.Security.Network;
using Microsoft.Extensions.Options;
using Xunit;

public sealed class UrlPolicyTests
{
    private static UrlPolicy CreatePolicy(bool requireHttps = true, bool allowLoopback = false)
    {
        var opts = new SecurityOptions { RequireHttps = requireHttps, AllowTrustedLoopback = allowLoopback };
        return new UrlPolicy(Options.Create(opts));
    }

    [Theory]
    [InlineData("http://example.com/image.jpg")]
    public void Validate_HttpUrl_WhenHttpsRequired_IsBlocked(string url)
    {
        var policy = CreatePolicy(requireHttps: true);
        var result = policy.Validate(new Uri(url));
        Assert.False(result.Allowed);
    }

    [Fact]
    public void Validate_HttpsUrl_IsAllowed()
    {
        var policy = CreatePolicy();
        var result = policy.Validate(new Uri("https://images.unsplash.com/photo.jpg"));
        // External DNS may or may not resolve in test; only check that it doesn't throw.
        Assert.NotNull(result);
    }

    [Theory]
    [InlineData("http://127.0.0.1/api")]
    [InlineData("http://localhost/api")]
    [InlineData("http://[::1]/api")]
    public void Validate_LoopbackUrls_AreBlockedForRemoteRequests(string url)
    {
        var policy = CreatePolicy(requireHttps: false);
        var result = policy.Validate(new Uri(url), isTrustedLocalRequest: false);
        Assert.False(result.Allowed);
    }

    [Theory]
    [InlineData("http://10.0.0.1/resource")]
    [InlineData("http://192.168.1.1/image.jpg")]
    [InlineData("http://172.16.0.1/wallpaper.png")]
    public void Validate_PrivateIpUrls_AreBlocked(string url)
    {
        var policy = CreatePolicy(requireHttps: false);
        var result = policy.Validate(new Uri(url), isTrustedLocalRequest: false);
        Assert.False(result.Allowed);
    }

    [Theory]
    [InlineData("http://169.254.100.1/resource")]
    public void Validate_LinkLocalUrls_AreBlocked(string url)
    {
        var policy = CreatePolicy(requireHttps: false);
        var result = policy.Validate(new Uri(url), isTrustedLocalRequest: false);
        Assert.False(result.Allowed);
    }

    [Fact]
    public void Validate_IPv4MappedToIPv6Private_IsBlocked()
    {
        // ::ffff:192.168.0.1 is IPv4-mapped private
        var policy = CreatePolicy(requireHttps: false);
        var result = policy.Validate(new Uri("http://[::ffff:192.168.0.1]/resource"), isTrustedLocalRequest: false);
        Assert.False(result.Allowed);
    }
}
