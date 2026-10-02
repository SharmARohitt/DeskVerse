namespace Deskverse.App.Services;

/// <summary>A short-lived message shown in the main window's status bar.</summary>
public sealed record AppNotification(string Message, NotificationKind Kind, DateTimeOffset RaisedAt);

public enum NotificationKind
{
    Info,
    Success,
    Warning,
    Error,
}

/// <summary>Application-wide in-app notification bus (no network, no telemetry).</summary>
public sealed class NotificationService
{
    public event EventHandler<AppNotification>? Raised;

    public void Info(string message) => Post(message, NotificationKind.Info);

    public void Success(string message) => Post(message, NotificationKind.Success);

    public void Warning(string message) => Post(message, NotificationKind.Warning);

    public void Error(string message) => Post(message, NotificationKind.Error);

    private void Post(string message, NotificationKind kind) =>
        Raised?.Invoke(this, new AppNotification(message, kind, DateTimeOffset.UtcNow));
}
