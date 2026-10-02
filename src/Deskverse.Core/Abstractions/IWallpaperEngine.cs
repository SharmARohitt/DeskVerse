namespace Deskverse.Core.Abstractions;

using Deskverse.Core.Models;

/// <summary>
/// Contract for wallpaper playback engines. Implementations exist for static
/// images and video; both must fail safe and report accurate status.
/// </summary>
public interface IWallpaperEngine
{
    /// <summary>Applies the given wallpaper. Must be idempotent and switch cleanly from the previous one.</summary>
    Task<OperationResult> ApplyAsync(ApplyRequest request, CancellationToken cancellationToken = default);

    Task<OperationResult> StopAsync(CancellationToken cancellationToken = default);

    Task<OperationResult> PauseAsync(CancellationToken cancellationToken = default);

    Task<OperationResult> ResumeAsync(CancellationToken cancellationToken = default);

    Task<EngineStatus> GetStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>Restores the wallpaper that was active before the last apply, when known.</summary>
    Task<OperationResult> RestorePreviousAsync(CancellationToken cancellationToken = default);

    Task<EngineCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken = default);

    /// <summary>Raised whenever the engine state changes, including failures and fallbacks.</summary>
    event EventHandler<EngineStatus>? StatusChanged;
}
