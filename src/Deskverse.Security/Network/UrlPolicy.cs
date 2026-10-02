namespace Deskverse.Security.Network;

using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Options;

public sealed record UrlPolicyResult
{
    public bool Allowed { get; init; }

    public string? Reason { get; init; }

    public static UrlPolicyResult Ok() => new() { Allowed = true };

    public static UrlPolicyResult Deny(string reason) => new() { Allowed = false, Reason = reason };
}

/// <summary>
/// URL admission policy for remote provider traffic: HTTPS-only, validated
/// hosts, and SSRF protection that blocks loopback, private, and link-local
/// destinations for untrusted remote requests.
/// </summary>
public sealed class UrlPolicy
{
    private readonly SecurityOptions _options;

    public UrlPolicy(IOptions<SecurityOptions> options)
    {
        _options = options.Value;
    }

    /// <summary>
    /// Validates a remote provider URL. Trusted loopback (the local DeskVerse API)
    /// is allowed only when explicitly opted in; provider content never is.
    /// </summary>
    public UrlPolicyResult Validate(Uri url, bool isTrustedLocalRequest = false)
    {
        ArgumentNullException.ThrowIfNull(url);

        if (_options.RequireHttps && url.Scheme != "https")
        {
            return UrlPolicyResult.Deny($"Only HTTPS remote URLs are accepted, got '{url.Scheme}'.");
        }

        if (url.Scheme is not ("https" or "http"))
        {
            return UrlPolicyResult.Deny($"Unsupported URL scheme '{url.Scheme}'.");
        }

        if (!url.IsAbsoluteUri || string.IsNullOrEmpty(url.Host))
        {
            return UrlPolicyResult.Deny("URL must be absolute with a host.");
        }

        if (url.Port is < 0)
        {
            return UrlPolicyResult.Deny("URL contains an invalid port.");
        }

        var hostResult = ValidateHost(url.Host, isTrustedLocalRequest);
        return hostResult;
    }

    private UrlPolicyResult ValidateHost(string host, bool isTrustedLocalRequest)
    {
        if (IPAddress.TryParse(host, out var literal))
        {
            return ValidateAddress(literal, isTrustedLocalRequest);
        }

        // Uri.Host normalizes some names; reject obvious local names outright.
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".local", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase))
        {
            if (isTrustedLocalRequest && _options.AllowTrustedLoopback)
            {
                return UrlPolicyResult.Ok();
            }

            return UrlPolicyResult.Deny("Local hostnames are blocked for remote provider requests.");
        }

        // DNS resolution: every returned address must pass the private/loopback filter.
        try
        {
            var entries = Dns.GetHostEntry(host);
            foreach (var address in entries.AddressList)
            {
                var result = ValidateAddress(address, isTrustedLocalRequest);
                if (!result.Allowed)
                {
                    return result;
                }
            }

            if (entries.AddressList.Length == 0)
            {
                return UrlPolicyResult.Deny("Host did not resolve to any address.");
            }

            return UrlPolicyResult.Ok();
        }
        catch (SocketException ex)
        {
            return UrlPolicyResult.Deny($"Host could not be resolved: {ex.Message}");
        }
    }

    private static UrlPolicyResult ValidateAddress(IPAddress address, bool isTrustedLocalRequest)
    {
        if (IPAddress.IsLoopback(address))
        {
            if (isTrustedLocalRequest)
            {
                return UrlPolicyResult.Ok();
            }

            return UrlPolicyResult.Deny("Loopback addresses are blocked for remote provider requests.");
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            var isPrivate = bytes[0] == 10
                || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                || (bytes[0] == 192 && bytes[1] == 168)
                || (bytes[0] == 169 && bytes[1] == 254)
                || bytes[0] == 0
                || bytes[0] >= 224;
            if (isPrivate)
            {
                return UrlPolicyResult.Deny(
                    "Private, link-local, unspecified, or multicast addresses are blocked for remote requests.");
            }

            return UrlPolicyResult.Ok();
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var bytes = address.GetAddressBytes();
            var isLinkLocal = bytes[0] == 0xFE && (bytes[1] & 0xC0) == 0x80;
            var isUniqueLocal = (bytes[0] & 0xFE) == 0xFC;
            var isMulticast = bytes[0] == 0xFF;
            if (isLinkLocal || isUniqueLocal || isMulticast || address.Equals(IPAddress.IPv6Any))
            {
                return UrlPolicyResult.Deny(
                    "Private, link-local, unspecified, or multicast addresses are blocked for remote requests.");
            }

            return UrlPolicyResult.Ok();
        }

        return UrlPolicyResult.Deny("Unsupported address family.");
    }
}
