namespace Deskverse.WallpaperEngine;

using System.Runtime.InteropServices;
using Deskverse.Core;
using Deskverse.Core.Abstractions;
using Deskverse.Core.Models;
using Microsoft.Extensions.Logging;
using Windows.System.Power;

/// <summary>
/// Pauses video wallpaper playback while a fullscreen application is in the
/// foreground or while the machine runs on battery, per user settings. Never
/// resumes a wallpaper the user paused manually.
/// </summary>
public sealed class ResourceGovernor : IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    private readonly IWallpaperEngine _engine;
    private readonly IPreferencesStore _preferences;
    private readonly IDisplayService _displayService;
    private readonly ILogger<ResourceGovernor> _logger;
    private readonly CancellationTokenSource _cancellation = new();

    private Task? _monitorLoop;
    private bool _pausedByGovernor;

    public ResourceGovernor(
        IWallpaperEngine engine,
        IPreferencesStore preferences,
        IDisplayService displayService,
        ILogger<ResourceGovernor> logger)
    {
        _engine = engine;
        _preferences = preferences;
        _displayService = displayService;
        _logger = logger;
    }

    /// <summary>True when the governor paused playback and no other reason to stay paused remains.</summary>
    public bool IsGovernorPaused => _pausedByGovernor;

    public void Start()
    {
        if (_monitorLoop is not null)
        {
            return;
        }

        _monitorLoop = Task.Run(() => MonitorAsync(_cancellation.Token));
    }

    public async Task StopAsync()
    {
        _cancellation.Cancel();
        if (_monitorLoop is not null)
        {
            try
            {
                await _monitorLoop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        if (_pausedByGovernor)
        {
            await _engine.ResumeAsync().ConfigureAwait(false);
            _pausedByGovernor = false;
        }
    }

    private async Task MonitorAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(PollInterval);
        while (true)
        {
            try
            {
                if (!await timer.WaitForNextTickAsync(cancellationToken))
                {
                    return;
                }

                await TickAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                // A monitor tick must never crash the app or the loop.
                _logger.LogWarning(ex, "Resource governor tick failed.");
            }
        }
    }

    private async Task TickAsync(CancellationToken cancellationToken)
    {
        var status = await _engine.GetStatusAsync(cancellationToken);
        if (status.ActiveKind != WallpaperKind.Video)
        {
            return;
        }

        var preferences = await _preferences.LoadAsync(cancellationToken);
        var shouldPause = (preferences.PauseVideoOnFullscreen && IsForegroundFullscreen())
                          || (preferences.PauseVideoOnBattery && IsOnBattery());

        switch (status.State)
        {
            case EngineState.Playing when shouldPause:
                var paused = await _engine.PauseAsync(cancellationToken);
                if (paused.Success)
                {
                    _pausedByGovernor = true;
                    _logger.LogInformation(
                        "Paused video wallpaper (fullscreen: {Fullscreen}, battery: {Battery}).",
                        IsForegroundFullscreen(), IsOnBattery());
                }

                break;

            case EngineState.Paused when !shouldPause && _pausedByGovernor:
                var resumed = await _engine.ResumeAsync(cancellationToken);
                if (resumed.Success)
                {
                    _pausedByGovernor = false;
                    _logger.LogInformation("Resumed video wallpaper; pause reasons cleared.");
                }

                break;
        }
    }

    private bool IsOnBattery() =>
        PowerManager.BatteryStatus == BatteryStatus.Discharging;

    private bool IsForegroundFullscreen()
    {
        try
        {
            var foreground = GetForegroundWindow();
            if (foreground == IntPtr.Zero)
            {
                return false;
            }

            var className = new string('\0', 64);
            if (GetClassName(foreground, className, 64) > 0)
            {
                var name = className.TrimEnd('\0');
                if (name is "Progman" or "WorkerW" or "Shell_TrayWnd" or "SysListView32")
                {
                    return false; // the desktop itself is not "a fullscreen app"
                }
            }

            if (!GetWindowRect(foreground, out var rect))
            {
                return false;
            }

            var windowBounds = (rect.Right - rect.Left) * (rect.Bottom - rect.Top);
            if (windowBounds <= 0)
            {
                return false;
            }

            foreach (var display in _displayService.GetDisplays())
            {
                if (rect.Left <= display.X
                    && rect.Top <= display.Y
                    && rect.Right >= display.X + display.Width
                    && rect.Bottom >= display.Y + display.Height)
                {
                    return true;
                }
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        _cancellation.Cancel();
        _cancellation.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;

        public int Top;

        public int Right;

        public int Bottom;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hwnd, string className, int maxCount);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
}
