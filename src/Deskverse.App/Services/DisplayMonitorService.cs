namespace Deskverse.App.Services;

using CommunityToolkit.Mvvm.Messaging;
using Deskverse.App.Messaging;
using Deskverse.Infrastructure.Interop;
using Microsoft.UI.Dispatching;

/// <summary>
/// Polls Win32DisplayService for topology changes every 5 seconds on a background
/// thread. Fires DisplayTopologyChangedMessage so ViewModels that depend on display
/// lists (Studio, RotationViewModel) can refresh themselves.
/// </summary>
public sealed class DisplayMonitorService : IDisposable
{
    private readonly Win32DisplayService _displayService;
    private readonly IMessenger _messenger;
    private readonly DispatcherQueue? _dispatcher;
    private readonly Timer _timer;
    private bool _disposed;

    public DisplayMonitorService(Win32DisplayService displayService)
    {
        _displayService = displayService;
        _messenger = WeakReferenceMessenger.Default;
        _dispatcher = DispatcherQueue.GetForCurrentThread();

        _displayService.DisplaysChanged += OnDisplaysChanged;
        _timer = new Timer(
            _ => _displayService.CheckForChanges(),
            state: null,
            dueTime: TimeSpan.FromSeconds(5),
            period: TimeSpan.FromSeconds(5));
    }

    private void OnDisplaysChanged(object? sender, EventArgs e)
    {
        if (_dispatcher is { HasThreadAccess: false } dq)
        {
            dq.TryEnqueue(() => _messenger.Send(new DisplayTopologyChangedMessage()));
        }
        else
        {
            _messenger.Send(new DisplayTopologyChangedMessage());
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _displayService.DisplaysChanged -= OnDisplaysChanged;
        _timer.Dispose();
    }
}
