namespace Deskverse.Api;

using System.Net;
using System.Runtime.Versioning;
using Deskverse.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

/// <summary>
/// Runs the local ASP.NET Core API on Kestrel, bound to 127.0.0.1 on an
/// OS-assigned port. The host forwards to the application's singletons so the
/// API and the UI always act on the same live state. Never binds a network
/// interface and never accepts requests without the bearer token.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class LocalApiHost : IAsyncDisposable
{
    private readonly IServiceProvider _appServices;
    private readonly ILogger<LocalApiHost> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private WebApplication? _app;

    public LocalApiHost(IServiceProvider appServices, ILogger<LocalApiHost> logger)
    {
        _appServices = appServices;
        _logger = logger;
    }

    public bool IsRunning => _app is not null;

    /// <summary>The bound loopback port, or 0 while stopped.</summary>
    public int Port { get; private set; }

    /// <summary>Starts Kestrel on an OS-assigned loopback port and reports it through <see cref="LocalApiState"/>.</summary>
    public async Task<int> StartAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_app is not null)
            {
                return Port;
            }

            var builder = WebApplication.CreateSlimBuilder();

            // Route all API logging through the app's logger factory (Serilog).
            builder.Logging.ClearProviders();
            builder.Services.Replace(ServiceDescriptor.Singleton(_appServices.GetRequiredService<ILoggerFactory>()));

            builder.WebHost.ConfigureKestrel(kestrel =>
            {
                kestrel.Listen(IPAddress.Loopback, 0); // 0 = port assigned by the OS
                kestrel.AddServerHeader = false;
                kestrel.Limits.MaxConcurrentConnections = 16;
                kestrel.Limits.MaxRequestBodySize = 128 * 1024; // JSON bodies only, and tiny ones
            });

            builder.Services.ConfigureHttpJsonOptions(json =>
            {
                json.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
            });

            Forward<WallpaperManager>(builder.Services);
            Forward<DiscoveryService>(builder.Services);
            Forward<RecommendationService>(builder.Services);
            Forward<StorageService>(builder.Services);
            Forward<PreferencesService>(builder.Services);
            Forward<CollectionsService>(builder.Services);
            Forward<SystemHealthService>(builder.Services);
            Forward<LocalApiState>(builder.Services);
            Forward<ApiTokenStore>(builder.Services);
            Forward<RotationScheduler>(builder.Services);

            var app = builder.Build();

            app.Use(async (context, next) =>
            {
                try
                {
                    await next(context).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(
                        ex, "Unhandled API error for {Method} {Path}.",
                        context.Request.Method, context.Request.Path);
                    context.Response.Clear();
                    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                    await context.Response.WriteAsJsonAsync(new ErrorDto("The request could not be completed."))
                        .ConfigureAwait(false);
                }
            });
            app.UseMiddleware<ApiAuthenticationMiddleware>();

            ApiEndpoints.Map(app);

            await app.StartAsync(cancellationToken).ConfigureAwait(false);
            _app = app;
            Port = ResolvePort(app);

            _appServices.GetRequiredService<LocalApiState>().SetListening(Port);
            _logger.LogInformation(
                "Local API listening on http://127.0.0.1:{Port}/api/v1 (loopback only, bearer token required).",
                Port);
            return Port;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StopAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_app is null)
            {
                return;
            }

            _appServices.GetRequiredService<LocalApiState>().SetStopped();
            await _app.StopAsync().ConfigureAwait(false);
            await _app.DisposeAsync().ConfigureAwait(false);
            _app = null;
            Port = 0;
            _logger.LogInformation("Local API stopped.");
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _gate.Dispose();
    }

    private void Forward<T>(IServiceCollection services) where T : class =>
        services.AddSingleton(_appServices.GetRequiredService<T>());

    private static int ResolvePort(WebApplication app)
    {
        var addresses = app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()?.Addresses ?? [];
        foreach (var address in addresses)
        {
            var separator = address.LastIndexOf(':');
            if (separator >= 0 && int.TryParse(address[(separator + 1)..], out var port) && port > 0)
            {
                return port;
            }
        }

        return 0;
    }
}
