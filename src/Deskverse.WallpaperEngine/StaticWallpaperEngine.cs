namespace Deskverse.WallpaperEngine;

using Deskverse.Core;
using Deskverse.Core.Abstractions;
using Deskverse.Core.Models;
using Microsoft.Extensions.Logging;

/// <summary>
/// Applies static images through the system wallpaper API. Remembers the wallpaper
/// active before the first DeskVerse apply so it can be restored on request.
/// </summary>
public sealed class StaticWallpaperEngine : IWallpaperEngine
{
    private readonly ISystemWallpaperApi _systemApi;
    private readonly ILogger<StaticWallpaperEngine> _logger;
    private readonly object _gate = new();

    private EngineStatus _status = new(EngineState.Idle, null, null, null, null, null);
    private string? _previousWallpaperPath;
    private bool _hasCapturedPrevious;
    private Guid? _activeWallpaperId;
    private string? _activeWallpaperTitle;
    private DateTimeOffset? _startedAt;

    public StaticWallpaperEngine(ISystemWallpaperApi systemApi, ILogger<StaticWallpaperEngine> logger)
    {
        _systemApi = systemApi;
        _logger = logger;
    }

    public event EventHandler<EngineStatus>? StatusChanged;

    public Task<OperationResult> ApplyAsync(ApplyRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Kind != WallpaperKind.Static)
        {
            return Task.FromResult(OperationResult.Fail(
                "The static engine only applies images."));
        }

        if (!File.Exists(request.AbsolutePath))
        {
            SetStatus(EngineState.Failed, $"Wallpaper file is missing: {request.AbsolutePath}");
            return Task.FromResult(OperationResult.Fail(
                $"Wallpaper file is missing: {request.AbsolutePath}"));
        }

        try
        {
            CapturePreviousWallpaper();
            if (!_systemApi.SetWallpaper(request.AbsolutePath, request.Placement))
            {
                SetStatus(EngineState.Failed, "The system rejected the wallpaper change.");
                return Task.FromResult(OperationResult.Fail(
                    "Windows did not accept the wallpaper change."));
            }

            lock (_gate)
            {
                _activeWallpaperId = request.WallpaperId;
                _activeWallpaperTitle = request.Title;
                _startedAt = DateTimeOffset.UtcNow;
            }

            SetStatus(EngineState.Playing, null);
            return Task.FromResult(OperationResult.Ok());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Static wallpaper apply failed for {Path}.", request.AbsolutePath);
            SetStatus(EngineState.Failed, ex.Message);
            return Task.FromResult(OperationResult.Fail($"Static wallpaper apply failed: {ex.Message}"));
        }
    }

    public Task<OperationResult> StopAsync(CancellationToken cancellationToken = default)
    {
        // "Stopping" a static wallpaper means releasing the engine's claim; the
        // desktop keeps showing the image until something else replaces it.
        lock (_gate)
        {
            _activeWallpaperId = null;
            _activeWallpaperTitle = null;
            _startedAt = null;
        }

        SetStatus(EngineState.Idle, null);
        return Task.FromResult(OperationResult.Ok());
    }

    public Task<OperationResult> PauseAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(OperationResult.Fail("Static images cannot be paused."));

    public Task<OperationResult> ResumeAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(OperationResult.Fail("Static images cannot be paused, so there is nothing to resume."));

    public Task<OperationResult> RestorePreviousAsync(CancellationToken cancellationToken = default)
    {
        string? restore;
        lock (_gate)
        {
            restore = _hasCapturedPrevious ? _previousWallpaperPath : null;
        }

        if (restore is null)
        {
            return Task.FromResult(OperationResult.Fail(
                "No pre-DeskVerse wallpaper was recorded, so there is nothing to restore."));
        }

        if (!File.Exists(restore))
        {
            return Task.FromResult(OperationResult.Fail(
                $"The previous wallpaper file no longer exists: {restore}"));
        }

        return _systemApi.SetWallpaper(restore, WallpaperPlacement.Fill)
            ? Task.FromResult(OperationResult.Ok())
            : Task.FromResult(OperationResult.Fail("Windows did not accept the restore."));
    }

    public Task<EngineStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult(new EngineStatus(
                _status.State,
                WallpaperKind.Static,
                _activeWallpaperId,
                _activeWallpaperTitle,
                _startedAt,
                _status.LastError));
        }
    }

    public Task<EngineCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new EngineCapabilities(
            SupportsStatic: true,
            SupportsVideo: false,
            SupportsMultiDisplay: true,
            KnownLimitations:
            [
                "Static wallpapers apply to all displays at once; per-display images require the video engine or a future per-monitor integration.",
            ]));

    private void CapturePreviousWallpaper()
    {
        lock (_gate)
        {
            if (_hasCapturedPrevious)
            {
                return;
            }

            _previousWallpaperPath = _systemApi.GetWallpaperPath();
            _hasCapturedPrevious = true;
            if (_previousWallpaperPath is not null)
            {
                _logger.LogInformation("Recorded pre-DeskVerse wallpaper: {Path}", _previousWallpaperPath);
            }
        }
    }

    private void SetStatus(EngineState state, string? error)
    {
        EngineStatus status;
        lock (_gate)
        {
            _status = new EngineStatus(state, WallpaperKind.Static, _activeWallpaperId, _activeWallpaperTitle, _startedAt, error);
            status = _status;
        }

        StatusChanged?.Invoke(this, status);
    }
}
