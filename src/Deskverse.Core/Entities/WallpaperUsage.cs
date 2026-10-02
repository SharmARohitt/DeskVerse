namespace Deskverse.Core.Entities;

using Deskverse.Core;

/// <summary>One applied session of a wallpaper, with optional explicit feedback.</summary>
public class WallpaperUsage
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid WallpaperId { get; set; }

    public Wallpaper? Wallpaper { get; set; }

    public DateTimeOffset AppliedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Total applied duration in seconds, updated when the wallpaper is replaced or the app exits.</summary>
    public long DurationSeconds { get; set; }

    public UserFeedback UserFeedback { get; set; } = UserFeedback.None;
}
