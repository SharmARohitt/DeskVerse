namespace Deskverse.WallpaperEngine;

using Deskverse.Core.Abstractions;
using Deskverse.WallpaperEngine.Interop;
using Deskverse.WallpaperEngine.Playback;
using Microsoft.Extensions.DependencyInjection;

public static class WallpaperEngineServiceExtensions
{
    /// <summary>
    /// Registers the wallpaper engines. <see cref="VideoWallpaperHost"/> must be
    /// resolved (first use) on the UI thread because it captures the DispatcherQueue.
    /// </summary>
    public static IServiceCollection AddDeskverseWallpaperEngine(this IServiceCollection services)
    {
        services.AddSingleton<DesktopWallpaperComApi>();
        services.AddSingleton<ISystemWallpaperApi>(sp => sp.GetRequiredService<DesktopWallpaperComApi>());

        services.AddSingleton<StaticWallpaperEngine>();
        services.AddSingleton<VideoWallpaperEngine>();
        services.AddSingleton<IVideoWallpaperHost, VideoWallpaperHost>();
        services.AddSingleton<IWallpaperEngine, CompositeWallpaperEngine>();
        services.AddSingleton<ResourceGovernor>();
        return services;
    }
}
