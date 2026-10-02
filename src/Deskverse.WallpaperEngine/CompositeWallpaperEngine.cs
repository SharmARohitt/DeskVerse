namespace Deskverse.WallpaperEngine;

using Deskverse.Core;
using Deskverse.Core.Abstractions;
using Deskverse.Core.Models;
using Microsoft.Extensions.Logging;

/// <summary>
/// Routes apply/stop/pause calls to the static or video engine by wallpaper kind
/// and remembers the request active before the current one for restore.
/// </summary>
public sealed class CompositeWallpaperEngine : IWallpaperEngine
{
    private readonly StaticWallpaperEngine _staticEngine;
    private readonly VideoWallpaperEngine _videoEngine;
    private readonly ILogger<CompositeWallpaperEngine> _logger;
    private readonly object _gate = new();

    private ApplyRequest? _current;
    private ApplyRequest? _previous;

    public CompositeWallpaperEngine(
        StaticWallpaperEngine staticEngine,
        VideoWallpaperEngine videoEngine,
        ILogger<CompositeWallpaperEngine> logger)
    {
        _staticEngine = staticEngine;
        _videoEngine = videoEngine;
        _logger = logger;

        _staticEngine.StatusChanged += OnChildStatusChanged;
        _videoEngine.StatusChanged += OnChildStatusChanged;
    }

    public event EventHandler<EngineStatus>? StatusChanged;

    public async Task<OperationResult> ApplyAsync(ApplyRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Kind == WallpaperKind.Video)
        {
            _logger.LogInformation("Applying video wallpaper {Id} ({Title}).", request.WallpaperId, request.Title);
        }
        else
        {
            _logger.LogInformation("Applying static wallpaper {Id} ({Title}).", request.WallpaperId, request.Title);
        }

        IWallpaperEngine engine = request.Kind == WallpaperKind.Video ? _videoEngine : _staticEngine;

        // Switching from video to anything else must tear the video layer down first.
        lock (_gate)
        {
            if (_current is { Kind: WallpaperKind.Video } previousVideo
                && request.WallpaperId != previousVideo.WallpaperId)
            {
                _videoEngine.StopAsync(cancellationToken).GetAwaiter().GetResult();
            }
        }

        var result = await engine.ApplyAsync(request, cancellationToken);
        if (result.Success)
        {
            lock (_gate)
            {
                if (_current is not null && _current.WallpaperId != request.WallpaperId)
                {
                    _previous = _current;
                }

                _current = request;
            }
        }

        return result;
    }

    public async Task<OperationResult> StopAsync(CancellationToken cancellationToken = default)
    {
        ApplyRequest? current;
        lock (_gate)
        {
            current = _current;
            _previous = _current;
            _current = null;
        }

        if (current is null)
        {
            return OperationResult.Ok();
        }

        IWallpaperEngine engine = current.Kind == WallpaperKind.Video ? _videoEngine : _staticEngine;
        return await engine.StopAsync(cancellationToken);
    }

    public Task<OperationResult> PauseAsync(CancellationToken cancellationToken = default)
    {
        ApplyRequest? current;
        lock (_gate)
        {
            current = _current;
        }

        return current is null
            ? Task.FromResult(OperationResult.Fail("No wallpaper is active."))
            : _videoEngine.PauseAsync(cancellationToken);
    }

    public Task<OperationResult> ResumeAsync(CancellationToken cancellationToken = default)
    {
        ApplyRequest? current;
        lock (_gate)
        {
            current = _current;
        }

        return current is null
            ? Task.FromResult(OperationResult.Fail("No wallpaper is active."))
            : _videoEngine.ResumeAsync(cancellationToken);
    }

    public async Task<OperationResult> RestorePreviousAsync(CancellationToken cancellationToken = default)
    {
        ApplyRequest? restore;
        lock (_gate)
        {
            restore = _previous;
            _previous = null;
        }

        if (restore is null)
        {
            return OperationResult.Fail("No previous wallpaper is recorded.");
        }

        if (!File.Exists(restore.AbsolutePath))
        {
            return OperationResult.Fail(
                $"The previous wallpaper file no longer exists: {restore.AbsolutePath}");
        }

        _logger.LogInformation("Restoring previous wallpaper {Title}.", restore.Title);
        return await ApplyAsync(restore, cancellationToken);
    }

    public Task<EngineStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        ApplyRequest? current;
        lock (_gate)
        {
            current = _current;
        }

        if (current is null)
        {
            return Task.FromResult(new EngineStatus(EngineState.Idle, null, null, null, null, null));
        }

        return current.Kind == WallpaperKind.Video
            ? _videoEngine.GetStatusAsync(cancellationToken)
            : _staticEngine.GetStatusAsync(cancellationToken);
    }

    public async Task<EngineCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken = default)
    {
        var staticCaps = await _staticEngine.GetCapabilitiesAsync(cancellationToken);
        var videoCaps = await _videoEngine.GetCapabilitiesAsync(cancellationToken);
        return new EngineCapabilities(
            staticCaps.SupportsStatic || videoCaps.SupportsStatic,
            staticCaps.SupportsVideo || videoCaps.SupportsVideo,
            staticCaps.SupportsMultiDisplay && videoCaps.SupportsMultiDisplay,
            [.. staticCaps.KnownLimitations, .. videoCaps.KnownLimitations]);
    }

    private void OnChildStatusChanged(object? sender, EngineStatus status)
    {
        // Only forward status for the engine that owns the current wallpaper.
        ApplyRequest? current;
        lock (_gate)
        {
            current = _current;
        }

        if (current is null)
        {
            return;
        }

        var senderKind = sender is VideoWallpaperEngine ? WallpaperKind.Video : WallpaperKind.Static;
        if (senderKind != current.Kind)
        {
            return;
        }

        StatusChanged?.Invoke(this, status);
    }
}
