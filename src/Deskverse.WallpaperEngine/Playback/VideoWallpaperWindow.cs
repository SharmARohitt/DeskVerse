namespace Deskverse.WallpaperEngine.Playback;

using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;
/// <summary>
/// A borderless XAML window hosting a looping muted MediaPlayerElement. The host
/// re-parents its HWND to the desktop WorkerW layer so it renders behind icons.
/// </summary>
internal sealed class VideoWallpaperWindow
{
    private readonly Window _window;

    private readonly MediaPlayerElement _playerElement;

    public VideoWallpaperWindow(string videoPath)
    {
        Player = new MediaPlayer
        {
            AutoPlay = true,
            IsLoopingEnabled = true,
            IsMuted = true,
        };
        Player.Source = MediaSource.CreateFromUri(new Uri(videoPath));

        _playerElement = new MediaPlayerElement
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            AreTransportControlsEnabled = false,
            Stretch = Stretch.UniformToFill,
        };
        _playerElement.SetMediaPlayer(Player);

        _window = new Window
        {
            Content = new Grid
            {
                Background = new SolidColorBrush(Microsoft.UI.Colors.Black),
                Children =
                {
                    _playerElement,
                },
            },
        };

        // No title bar, no border, no taskbar presence.
        if (_window.AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(hasBorder: false, hasTitleBar: false);
        }

        _window.AppWindow.IsShownInSwitchers = false;
    }

    public MediaPlayer Player { get; }

    public IntPtr Hwnd => WinRT.Interop.WindowNative.GetWindowHandle(_window);

    public void Show()
    {
        _window.Activate();
    }

    public void Close()
    {
        try
        {
            Player.Pause();
            Player.Source = null;
            _playerElement.SetMediaPlayer(null);
        }
        finally
        {
            _window.Close();
        }
    }
}
