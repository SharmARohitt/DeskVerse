namespace Deskverse.App.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Deskverse.App.Messaging;
using Deskverse.App.Services;

/// <summary>
/// Cross-cutting shell state: startup readiness, in-app notifications, and the
/// wallpaper currently selected for the Studio.
/// </summary>
public partial class MainViewModel : ViewModelBase, IRecipient<WallpaperSelectedMessage>
{
    private readonly NotificationService _notifications;

    [ObservableProperty]

    private bool _isReady;

    [ObservableProperty]

    private string _statusMessage= "Preparing your library\u2026";

    [ObservableProperty]

    private string _infoMessage= string.Empty;

    [ObservableProperty]

    private bool _isInfoOpen;

    [ObservableProperty]

    private Microsoft.UI.Xaml.Controls.InfoBarSeverity _infoSeverity= Microsoft.UI.Xaml.Controls.InfoBarSeverity.Informational;

    public MainViewModel(NotificationService notifications)
    {
        _notifications = notifications;
        _notifications.Raised += (_, notification) => RunOnUi(() => Show(notification));
        WeakReferenceMessenger.Default.RegisterAll(this);
    }

    public WallpaperItemViewModel? SelectedForStudio { get; private set; }

    public event EventHandler? SelectedForStudioChanged;

    public void MarkReady()
    {
        IsReady = true;
        StatusMessage = "Ready";
    }

    public void ShowFatal(string message)
    {
        StatusMessage = "Startup failed";
        InfoMessage = message;
        InfoSeverity = Microsoft.UI.Xaml.Controls.InfoBarSeverity.Error;
        IsInfoOpen = true;
    }

    public void Receive(WallpaperSelectedMessage message)
    {
        SelectedForStudio = message.Item;
        SelectedForStudioChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Show(AppNotification notification)
    {
        InfoMessage = notification.Message;
        InfoSeverity = notification.Kind switch
        {
            NotificationKind.Success => Microsoft.UI.Xaml.Controls.InfoBarSeverity.Success,
            NotificationKind.Warning => Microsoft.UI.Xaml.Controls.InfoBarSeverity.Warning,
            NotificationKind.Error => Microsoft.UI.Xaml.Controls.InfoBarSeverity.Error,
            _ => Microsoft.UI.Xaml.Controls.InfoBarSeverity.Informational,
        };
        IsInfoOpen = true;
    }
}
