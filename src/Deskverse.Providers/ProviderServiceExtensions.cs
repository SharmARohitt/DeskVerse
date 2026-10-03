namespace Deskverse.Providers;

using Deskverse.Core.Abstractions;
using Deskverse.Security.Network;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

public static class ProviderServiceExtensions
{
    /// <summary>
    /// Registers the always-on local provider, the development mock provider,
    /// the Wallhaven provider (enabled when configured), and the aggregator.
    /// The Wallhaven API key is read from the environment variable
    /// DESKVERSE_WALLHAVEN_APIKEY or the "Wallhaven:ApiKey" configuration key.
    /// </summary>
    public static IServiceCollection AddDeskverseProviders(this IServiceCollection services)
    {
        services.AddSingleton<LocalWallpaperProvider>();
        services.AddSingleton<MockWallpaperProvider>();
        services.AddSingleton<IWallpaperProvider>(sp => sp.GetRequiredService<LocalWallpaperProvider>());
        services.AddSingleton<IWallpaperProvider>(sp => sp.GetRequiredService<MockWallpaperProvider>());

        // Wallhaven — register unconditionally; provider degrades gracefully if offline.
        services.AddOptions<WallhavenOptions>()
            .Configure<IConfiguration>((opts, cfg) =>
            {
                cfg.GetSection(WallhavenOptions.SectionKey).Bind(opts);
                // Allow the API key to be supplied via a dedicated environment variable
                // without exposing it anywhere in configuration files.
                var envKey = System.Environment.GetEnvironmentVariable("DESKVERSE_WALLHAVEN_APIKEY");
                if (!string.IsNullOrWhiteSpace(envKey))
                {
                    opts.ApiKey = envKey.Trim();
                }
            });

        services.AddHttpClient<WallhavenProvider>()
            .ConfigurePrimaryHttpMessageHandler(static () =>
                HardenedHttpClient.CreateHandler(TimeSpan.FromSeconds(10)));
        services.AddSingleton<IWallpaperProvider>(sp => sp.GetRequiredService<WallhavenProvider>());

        services.AddSingleton<ProviderAggregator>();
        return services;
    }
}
