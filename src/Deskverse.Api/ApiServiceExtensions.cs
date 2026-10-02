namespace Deskverse.Api;

using System.Runtime.Versioning;
using Microsoft.Extensions.DependencyInjection;

[SupportedOSPlatform("windows")]
public static class ApiServiceExtensions
{
    /// <summary>
    /// Registers the local API's own services: the DPAPI-backed token store and
    /// the loopback host. The host forwards to application-layer singletons, so
    /// callers must also register <c>AddDeskverseApplication</c> and friends.
    /// </summary>
    public static IServiceCollection AddDeskverseApi(this IServiceCollection services)
    {
        services.AddSingleton<ApiTokenStore>();
        services.AddSingleton<LocalApiHost>();
        return services;
    }
}
