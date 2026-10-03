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

        // Downloads are bounded by SecureDownloadService's own cancellation token,
        // so the transport-level timeout is disabled here.
        services.AddHttpClient<SecureDownloadService>(client => client.Timeout = Timeout.InfiniteTimeSpan)
            .ConfigurePrimaryHttpMessageHandler(static () =>
                HardenedHttpClient.CreateHandler(TimeSpan.FromSeconds(15)));

        services.AddSingleton<UrlPolicy>();
        services.AddSingleton<ImportValidator>();

        services.AddSingleton<PreferencesCachePolicy>();
        // The cache policy is read through the abstraction everywhere else, so the
        // preferences-backed implementation has to be the registered one.
        services.AddSingleton<Deskverse.Storage.ICachePolicy>(sp => sp.GetRequiredService<PreferencesCachePolicy>());
        services.AddSingleton<Storage.CacheManager>();

        // RecommendationService takes the scoring engine, so it must be resolvable
        // from the same container; the engine is stateless and shares one instance.
        services.AddOptions<Deskverse.Intelligence.RecommendationOptions>();
        services.AddSingleton<Deskverse.Intelligence.RecommendationEngine>();

        services.AddSingleton<StorageService>();
        services.AddSingleton<WallpaperManager>();
        services.AddSingleton<DiscoveryService>();
        services.AddSingleton<RecommendationService>();
        services.AddSingleton<PreferencesService>();
        services.AddSingleton<CollectionsService>();
        services.AddSingleton<SystemHealthService>();
        services.AddSingleton<LocalApiState>();
        services.AddSingleton<RotationScheduler>();
        return services;
    }
}
