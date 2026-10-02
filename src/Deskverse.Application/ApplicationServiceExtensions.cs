namespace Deskverse.Application;

using Deskverse.Core.Abstractions;
using Deskverse.Security;
using Deskverse.Security.FileValidation;
using Deskverse.Security.Network;
using Microsoft.Extensions.DependencyInjection;

public static class ApplicationServiceExtensions
{
    /// <summary>
    /// Registers application services on top of the infrastructure, provider,
    /// and storage registrations. Visual analysis and thumbnails are registered
    /// by the composition root because they are platform-specific.
    /// </summary>
    public static IServiceCollection AddDeskverseApplication(this IServiceCollection services)
    {
        services.AddOptions<SecurityOptions>();

        services.AddHttpClient<SecureDownloadService>();
        services.AddSingleton<UrlPolicy>();
        services.AddSingleton<ImportValidator>();

        services.AddSingleton<PreferencesCachePolicy>();
        services.AddSingleton<Storage.CacheManager>();
        services.AddSingleton<StorageService>();
        services.AddSingleton<WallpaperManager>();
        services.AddSingleton<DiscoveryService>();
        services.AddSingleton<RecommendationService>();
        services.AddSingleton<PreferencesService>();
        services.AddSingleton<CollectionsService>();
        services.AddSingleton<SystemHealthService>();
        services.AddSingleton<LocalApiState>();
        return services;
    }
}
