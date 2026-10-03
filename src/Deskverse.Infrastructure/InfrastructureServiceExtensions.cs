namespace Deskverse.Infrastructure;

using Deskverse.Core.Abstractions;
using Deskverse.Infrastructure.Interop;
using Deskverse.Infrastructure.Persistence;
using Deskverse.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Runtime.Versioning;

public static class InfrastructureServiceExtensions
{
    /// <summary>
    /// Registers persistence, repositories, Win32 system services, and the database
    /// initializer. Call <see cref="DatabaseInitializer.InitializeAsync"/> during startup.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static IServiceCollection AddDeskverseInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<IAppEnvironment, WindowsAppEnvironment>();
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<WindowsAppEnvironment>(sp => (WindowsAppEnvironment)sp.GetRequiredService<IAppEnvironment>());

        services.AddDbContextFactory<DeskverseDbContext>((sp, options) =>
        {
            var environment = sp.GetRequiredService<IAppEnvironment>();
            options.UseSqlite($"Data Source={environment.DatabasePath}");
        });

        services.AddSingleton<DatabaseInitializer>();

        services.AddSingleton<IWallpaperRepository, WallpaperRepository>();
        services.AddSingleton<IPreferencesStore, PreferencesStore>();
        services.AddSingleton<IUsageRepository, UsageRepository>();
        services.AddSingleton<IProviderConfigStore, ProviderConfigStore>();
        services.AddSingleton<ICollectionRepository, CollectionRepository>();

        services.AddSingleton<Win32DisplayService>();
        services.AddSingleton<IDisplayService>(sp => sp.GetRequiredService<Win32DisplayService>());
        services.AddSingleton<ISystemWallpaperApi, SystemParametersWallpaperApi>();

        return services;
    }
}
