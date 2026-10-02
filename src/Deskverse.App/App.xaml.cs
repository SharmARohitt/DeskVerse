namespace Deskverse.App;

using Deskverse.Api;
using Deskverse.App.Services;
using Deskverse.App.Services.Imaging;
using Deskverse.App.ViewModels;
using Deskverse.Application;
using Deskverse.Core.Abstractions;
using Deskverse.Infrastructure;
using Deskverse.Infrastructure.Persistence;
using Deskverse.WallpaperEngine;
using Deskverse.WallpaperEngine.Playback;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Serilog;
using Serilog.Events;

/// <summary>
/// Composition root. Wires every Deskverse module into one container, starts the
/// local API, the resource governor, and the main window. All module registrations
/// happen here; platform-specific visual analysis and thumbnails are provided by
/// this project because they depend on Windows imaging.
/// </summary>
public partial class App : Application
{
    private ServiceProvider _services = default!;
    private DispatcherQueue? _dispatcher;
    private MainWindow? _mainWindow;
    private bool _shutdownStarted;

    public static IServiceProvider Services { get; private set; } = default!;

    public static MainWindow? MainWindowInstance { get; private set; }

    public App()
    {
        InitializeComponent();

        UnhandledException += (_, e) =>
        {
            Log.Error(e.Exception, "Unhandled XAML exception: {Message}", e.Message);
            e.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Log.Fatal(e.ExceptionObject as Exception, "Unhandled CLR exception");

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Error(e.Exception, "Unobserved task exception");
            e.SetObserved();
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        _services = BuildServices();
        Services = _services;

        _mainWindow = new MainWindow();
        MainWindowInstance = _mainWindow;
        _mainWindow.Closed += OnMainWindowClosed;
        _mainWindow.Activate();

        var picker = _services.GetRequiredService<PickerService>();
        picker.SetOwner(WinRT.Interop.WindowNative.GetWindowHandle(_mainWindow));

        _ = Task.Run(RunStartupAsync);
    }

    private static ServiceProvider BuildServices()
    {
        var services = new ServiceCollection();

        services.AddSerilog((sp, configuration) =>
        {
            // The logger factory is created lazily, so directories are guaranteed
            // to exist before the first log write.
            sp.GetRequiredService<WindowsAppEnvironment>().EnsureDirectories();
            var environment = sp.GetRequiredService<IAppEnvironment>();
            configuration
                .MinimumLevel.Debug()
                .Enrich.FromLogContext()
                .WriteTo.File(
                    Path.Combine(environment.LogsDirectory, "deskverse-.log"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 14,
                    shared: true,
                    outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
                .WriteTo.Debug(restrictedToMinimumLevel: LogEventLevel.Debug);
        });

        // Empty IConfiguration so provider options binding doesn't throw on startup.
        services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(
            _ => new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());

        services.AddDeskverseInfrastructure();
        services.AddDeskverseProviders();
        services.AddDeskverseWallpaperEngine();
        services.AddDeskverseApplication();
        services.AddDeskverseApi();

        services.AddSingleton<IVisualAnalyzer, WindowsVisualAnalyzer>();
        services.AddSingleton<IThumbnailService, WindowsThumbnailService>();

        services.AddSingleton<NotificationService>();
        services.AddSingleton<PickerService>();
        services.AddSingleton<WallpaperActions>();
        services.AddSingleton<DisplayMonitorService>();

        services.AddSingleton<MainViewModel>();
        services.AddSingleton<HomeViewModel>();
        services.AddSingleton<DiscoverViewModel>();
        services.AddSingleton<LibraryViewModel>();
        services.AddSingleton<CollectionsViewModel>();
        services.AddSingleton<StudioViewModel>();
        services.AddSingleton<StorageViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<RotationViewModel>();

        return services.BuildServiceProvider();
    }

    private async Task RunStartupAsync()
    {
        try
        {
            _services.GetRequiredService<WindowsAppEnvironment>().EnsureDirectories();

            var initializer = _services.GetRequiredService<DatabaseInitializer>();
            await initializer.InitializeAsync().ConfigureAwait(false);
            Log.Information("Database ready at {Path}", _services.GetRequiredService<IAppEnvironment>().DatabasePath);

            var preferencesService = _services.GetRequiredService<PreferencesService>();
            var preferences = await preferencesService.LoadAsync().ConfigureAwait(false);

            if (!preferences.FirstRunComplete)
            {
                var defaultCache = _services.GetRequiredService<IAppEnvironment>().DefaultCacheDirectory;
                FirstRunSetupResult? setup = null;
                await DispatchAsync(async () =>
                {
                    setup = await _mainWindow!.ShowFirstRunSetupAsync(defaultCache, preferences.StorageLimitBytes)
                        .ConfigureAwait(true);
                }).ConfigureAwait(false);

                if (setup is null)
                {
                    // The user dismissed setup without choosing; safe defaults apply.
                    await preferencesService
                        .CompleteFirstRunAsync(defaultCache, preferences.StorageLimitBytes)
                        .ConfigureAwait(false);
                }
                else
                {
                    await preferencesService
                        .CompleteFirstRunAsync(setup.CacheDirectory, setup.StorageLimitBytes)
                        .ConfigureAwait(false);
                }
            }

            // VideoWallpaperHost captures the DispatcherQueue, so its first resolution
            // must happen on the UI thread.
            await DispatchAsync(() =>
            {
                _ = _services.GetRequiredService<IVideoWallpaperHost>();
                return Task.CompletedTask;
            }).ConfigureAwait(false);

            _services.GetRequiredService<ResourceGovernor>().Start();

            // Start display topology monitoring (raises DisplayTopologyChangedMessage on changes).
            _ = _services.GetRequiredService<DisplayMonitorService>();

            // Start the wallpaper rotation scheduler if enabled in preferences.
            var rotationScheduler = _services.GetRequiredService<RotationScheduler>();
            await rotationScheduler.StartAsync().ConfigureAwait(false);

            var apiHost = _services.GetRequiredService<LocalApiHost>();
            await apiHost.StartAsync().ConfigureAwait(false);
            var apiState = _services.GetRequiredService<LocalApiState>();
            Log.Information("Local API listening on 127.0.0.1:{Port}", apiState.Port);

            await DispatchAsync(() =>
            {
                _mainWindow!.OnStartupComplete();
                return Task.CompletedTask;
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "DeskVerse failed to start");
            await DispatchAsync(() =>
            {
                _mainWindow?.ShowFatalError("DeskVerse could not finish starting. Details are in %LOCALAPPDATA%\\DeskVerse\\logs.");
                return Task.CompletedTask;
            }).ConfigureAwait(false);
        }
    }

    private async Task DispatchAsync(Func<Task> action)
    {
        var dispatcher = _dispatcher ?? throw new InvalidOperationException("No UI dispatcher was captured.");
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!dispatcher.TryEnqueue(async () =>
        {
            try
            {
                await action().ConfigureAwait(true);
                completion.TrySetResult(true);
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        }))
        {
            throw new InvalidOperationException("The UI dispatcher is no longer available.");
        }

        await completion.Task.ConfigureAwait(false);
    }

    private void OnMainWindowClosed(object? sender, WindowEventArgs args)
    {
        if (_shutdownStarted)
        {
            return;
        }

        _shutdownStarted = true;
        _ = ShutdownAsync();
    }

    private async Task ShutdownAsync()
    {
        try
        {
            var apiHost = _services.GetRequiredService<LocalApiHost>();
            await apiHost.StopAsync().ConfigureAwait(false);

            var rotationScheduler = _services.GetRequiredService<RotationScheduler>();
            await rotationScheduler.StopAsync().ConfigureAwait(false);

            var governor = _services.GetRequiredService<ResourceGovernor>();
            await governor.StopAsync().ConfigureAwait(false);
            governor.Dispose();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Shutdown cleanup failed");
        }
        finally
        {
            await _services.DisposeAsync().ConfigureAwait(false);
            Log.CloseAndFlush();
        }
    }
}
