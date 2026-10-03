namespace Deskverse.Security.Network;

using System.Net.Http;

/// <summary>
/// The HTTP transport every outbound DeskVerse request must use.
/// Redirects are never followed automatically: <see cref="UrlPolicy"/> vets only the
/// address the caller supplied, so an auto-followed redirect could reach a loopback
/// or link-local target, and custom headers such as a provider API key are carried
/// across redirects to an unrelated host.
/// </summary>
public static class HardenedHttpClient
{
    public static SocketsHttpHandler CreateHandler(TimeSpan connectTimeout) => new()
    {
        AllowAutoRedirect = false,
        ConnectTimeout = connectTimeout,
        PooledConnectionLifetime = TimeSpan.FromMinutes(10),
    };
}
