namespace Deskverse.Core.Entities;

using Deskverse.Core;

/// <summary>Single-row user preference profile persisted in SQLite.</summary>
public class UserPreferences
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;

    public string[] PreferredCategories { get; set; } = [];

    /// <summary>Preferred dominant colors as RRGGBB hex values.</summary>
    public string[] PreferredColors { get; set; } = [];

    /// <summary>Preferred average brightness in [0,1]; null means no preference.</summary>
    public double? PreferredBrightness { get; set; }

    /// <summary>Free-form style tags such as "minimal", "nature", "abstract".</summary>
    public string[] PreferredStyles { get; set; } = [];

    public bool AllowVideoWallpapers { get; set; } = true;

    public bool PauseVideoOnBattery { get; set; } = true;

    public bool PauseVideoOnFullscreen { get; set; } = true;

    public bool AnimatedPreviewsEnabled { get; set; } = true;

    public BackgroundResourcePolicy BackgroundPolicy { get; set; } = BackgroundResourcePolicy.Balanced;

    public long StorageLimitBytes { get; set; } = 2L * 1024 * 1024 * 1024;

    /// <summary>Absolute path of the managed wallpaper cache root.</summary>
    public string CacheDirectory { get; set; } = string.Empty;

    public bool AutoCleanupEnabled { get; set; } = true;

    public bool RunAtStartup { get; set; }

    public bool MinimizeToTray { get; set; }

    public bool TelemetryOptIn { get; set; }

    /// <summary>True once the user has chosen a cache location during first-run setup.</summary>
    public bool FirstRunComplete { get; set; }

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
