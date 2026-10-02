namespace Deskverse.Providers;

using Deskverse.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;

public static class ProviderServiceExtensions
{
    /// <summary>
    /// Registers the always-on local provider, the development mock provider, and
    /// the aggregator. Remote providers are added as additional IWallpaperProvider
    /// registrations and picked up by the aggregator automatically.
    /// </summary>
    public static IServiceCollection AddDeskverseProviders(this IServiceCollection services)
    {
        services.AddSingleton<LocalWallpaperProvider>();
        services.AddSingleton<MockWallpaperProvider>();
        services.AddSingleton<IWallpaperProvider>(sp => sp.GetRequiredService<LocalWallpaperProvider>());
        services.AddSingleton<IWallpaperProvider>(sp => sp.GetRequiredService<MockWallpaperProvider>());
        services.AddSingleton<ProviderAggregator>();
        return services;
    }
}
