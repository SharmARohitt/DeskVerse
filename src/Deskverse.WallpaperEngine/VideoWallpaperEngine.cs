namespace Deskverse.WallpaperEngine;

using Deskverse.Core;
using Deskverse.Core.Abstractions;
using Deskverse.Core.Models;
using Deskverse.WallpaperEngine.Playback;
using Microsoft.Extensions.Logging;

/// <summary>
/// Plays video wallpapers in a desktop-layered window. When playback cannot start,
/// it reports failure immediately so the caller can keep the previous wallpaper.
/// </summary>
public sealed class VideoWallpaperEngine : IWallpaperEngine
{
    private readonly IVideoWallpaperHost _host;
    private readonly IDisplayService _displayService;
    private readonly ILogger<VideoWallpaperEngine> _logger;
    private readonly object _gate = new();

    private EngineState _state = EngineState.Idle;
    private Guid? _activeWallpaperId;
    private string? _activeWallpaperTitle;
    private DateTimeOffset? _startedAt;
    private string? _lastError;

    public VideoWallpaperEngine(
        IVideoWallpaperHost host,
        IDisplayService displayService,
        ILogger<VideoWallpaperEngine> logger)
    {
        _host = host;
        _displayService = displayService;
        _logger = logger;
    }

    public event EventHandler<EngineStatus>? StatusChanged;

    public async Task<OperationResult> ApplyAsync(ApplyRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Kind != WallpaperKind.Video)
        {
            return OperationResult.Fail("The video engine only plays video wallpapers.");
        }

        SetState(EngineState.Starting, null);

        var display = ResolveDisplay(request.DisplayDeviceName);
        var started = await _host.StartAsync(
            request.AbsolutePath,
            display.X,
            display.Y,
            display.Width,
            display.Height);
        if (!started)
        {
            var error = $"Video playback could not start for {request.Title}. " +
                        "The previous wallpaper is still active.";
            _logger.LogWarning("{Error}", error);
            SetState(EngineState.Failed, error);
            return OperationResult.Fail(error);
        }

        lock (_gate)
        {
            _activeWallpaperId = request.WallpaperId;
            _activeWallpaperTitle = request.Title;
            _startedAt = DateTimeOffset.UtcNow;
        }

        SetState(EngineState.Playing, null);
        return OperationResult.Ok();
    }

    public Task<OperationResult> StopAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _host.StopAsync().GetAwaiter().GetResult();
            lock (_gate)
            {
                _activeWallpaperId = null;
                _activeWallpaperTitle = null;
                _startedAt = null;
            }

            SetState(EngineState.Idle, null);
            return Task.FromResult(OperationResult.Ok());
        }
        catch (Exception ex)
        {
            SetState(EngineState.Failed, ex.Message);
            return Task.FromResult(OperationResult.Fail($"Video stop failed: {ex.Message}"));
        }
    }

    public Task<OperationResult> PauseAsync(CancellationToken cancellationToken = default)
    {
        if (!_host.IsRunning)
        {
            return Task.FromResult(OperationResult.Fail("No video wallpaper is playing."));
        }

        _host.PauseAsync().GetAwaiter().GetResult();
        SetState(EngineState.Paused, null);
        return Task.FromResult(OperationResult.Ok());
    }

    public Task<OperationResult> ResumeAsync(CancellationToken cancellationToken = default)
    {
        if (!_host.IsRunning)
        {
            return Task.FromResult(OperationResult.Fail("No video wallpaper is playing."));
        }

        _host.ResumeAsync().GetAwaiter().GetResult();
        SetState(EngineState.Playing, null);
        return Task.FromResult(OperationResult.Ok());
    }

    public Task<OperationResult> RestorePreviousAsync(CancellationToken cancellationToken = default) =>
        // The video layer has no persistent "previous" image; the composite engine
        // owns restore semantics and re-applies the recorded previous wallpaper.
        StopAsync(cancellationToken);

    public Task<EngineStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult(new EngineStatus(
                _state,
                _state == EngineState.Idle ? null : WallpaperKind.Video,
                _activeWallpaperId,
                _activeWallpaperTitle,
                _startedAt,
                _lastError));
        }
    }

    public Task<EngineCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new EngineCapabilities(
            SupportsStatic: false,
            SupportsVideo: true,
            SupportsMultiDisplay: true,
            KnownLimitations:
            [
                "Video wallpapers run in a window layered behind the desktop icons; DRM-protected media will not render.",
                "Per-display video is supported by targeting one display at a time.",
            ]));

    private DisplayInfo ResolveDisplay(string? deviceName)
    {
        var displays = _displayService.GetDisplays();
        if (deviceName is not null)
        {
            var match = displays.FirstOrDefault(d =>
                string.Equals(d.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                return match;
            }

            _logger.LogWarning("Display {Device} not found; falling back to primary.", deviceName);
        }

        return displays.FirstOrDefault(d => d.IsPrimary) ?? new DisplayInfo("PRIMARY", 0, 0, 1920, 1080, true, 1.0);
    }

    private void SetState(EngineState state, string? error)
    {
        EngineStatus status;
        lock (_gate)
        {
            _state = state;
            _lastError = error;
            status = new EngineStatus(
                state,
                state == EngineState.Idle ? null : WallpaperKind.Video,
                _activeWallpaperId,
                _activeWallpaperTitle,
                _startedAt,
                error);
        }

        StatusChanged?.Invoke(this, status);
    }
}
