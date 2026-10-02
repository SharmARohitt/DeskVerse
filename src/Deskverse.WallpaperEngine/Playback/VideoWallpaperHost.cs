namespace Deskverse.WallpaperEngine.Playback;

using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;

/// <summary>Surface for the video playback layer, fakeable in tests.</summary>
public interface IVideoWallpaperHost
{
    bool IsRunning { get; }

    string? CurrentVideoPath { get; }

    /// <summary>Creates the desktop-layered video window covering the given rectangle.</summary>
    Task<bool> StartAsync(string videoPath, int x, int y, int width, int height);

    Task PauseAsync();

    Task ResumeAsync();

    Task StopAsync();
}

/// <summary>
/// Owns the XAML video wallpaper window. All window manipulation is marshaled to
/// the UI thread captured at construction (the host must be created on that thread).
/// </summary>
public sealed class VideoWallpaperHost : IVideoWallpaperHost
{
    private readonly DispatcherQueue _dispatcher;
    private readonly ILogger<VideoWallpaperHost> _logger;
    private readonly object _gate = new();

    private VideoWallpaperWindow? _window;

    public VideoWallpaperHost(ILogger<VideoWallpaperHost> logger)
    {
        _logger = logger;
        _dispatcher = DispatcherQueue.GetForCurrentThread()
            ?? throw new InvalidOperationException(
                "VideoWallpaperHost must be constructed on the UI thread.");
    }

    public bool IsRunning
    {
        get
        {
            lock (_gate)
            {
                return _window is not null;
            }
        }
    }

    public string? CurrentVideoPath { get; private set; }

    public Task<bool> StartAsync(string videoPath, int x, int y, int width, int height)
    {
        if (string.IsNullOrWhiteSpace(videoPath) || !File.Exists(videoPath))
        {
            _logger.LogWarning("Video wallpaper file is missing: {Path}", videoPath);
            return Task.FromResult(false);
        }

        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_dispatcher.TryEnqueue(() =>
            {
                try
                {
                    StopWindowLocked();

                    var window = new VideoWallpaperWindow(videoPath);
                    window.Show();

                    var attached = Interop.WorkerWInterop.AttachToWorkerW(window.Hwnd, x, y, width, height);
                    if (!attached)
                    {
                        _logger.LogWarning("Could not attach the video window to the desktop layer.");
                        window.Close();
                        lock (_gate)
                        {
                            _window = null;
                        }

                        completion.TrySetResult(false);
                        return;
                    }

                    lock (_gate)
                    {
                        _window = window;
                        CurrentVideoPath = videoPath;
                    }

                    completion.TrySetResult(true);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Video wallpaper window creation failed.");
                    completion.TrySetResult(false);
                }
            }))
        {
            return Task.FromResult(false);
        }

        return completion.Task;
    }

    public Task PauseAsync()
    {
        lock (_gate)
        {
            _window?.Player.Pause();
        }

        return Task.CompletedTask;
    }

    public Task ResumeAsync()
    {
        lock (_gate)
        {
            _window?.Player.Play();
        }

        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        if (!_dispatcher.TryEnqueue(StopWindowLocked))
        {
            StopWindowLocked();
        }

        return Task.CompletedTask;
    }

    private void StopWindowLocked()
    {
        lock (_gate)
        {
            CurrentVideoPath = null;
            if (_window is null)
            {
                return;
            }

            try
            {
                _window.Close();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error while closing the video wallpaper window.");
            }
            finally
            {
                _window = null;
            }
        }
    }
}
