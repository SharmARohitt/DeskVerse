namespace Deskverse.Core;

public enum WallpaperKind
{
    Static = 0,
    Video = 1,
}

public enum WallpaperFormat
{
    Unknown = 0,
    Jpeg = 1,
    Png = 2,
    Bmp = 3,
    Gif = 4,
    WebP = 5,
    Mp4 = 6,
    WebM = 7,
    Matroska = 8,
    QuickTimeMov = 9,
    Avi = 10,
}

public enum SafetyStatus
{
    Pending = 0,
    Safe = 1,
    Rejected = 2,
    Quarantined = 3,
}

public enum WallpaperPlacement
{
    /// <summary>Fills the screen, cropping as needed (default).</summary>
    Fill = 0,
    /// <summary>Fits entirely inside the screen with letterboxing.</summary>
    Fit = 1,
    /// <summary>Stretches to the screen ignoring aspect ratio.</summary>
    Stretch = 2,
    /// <summary>Tiles across the screen at native size.</summary>
    Tile = 3,
    /// <summary>Centers at native size.</summary>
    Center = 4,
    /// <summary>Spans a single image across all displays.</summary>
    Span = 5,
}

public enum UserFeedback
{
    None = 0,
    Favorite = 1,
    Dislike = 2,
}

public enum SortOrder
{
    NewestFirst = 0,
    RecentlyUsed = 1,
    MostUsedFirst = 2,
    TitleAZ = 3,
    LargestFirst = 4,
    Random = 5,
}

public enum BackgroundResourcePolicy
{
    /// <summary>Aggressively pause playback and release resources.</summary>
    Conservative = 0,
    Balanced = 1,
    /// <summary>Keep highest playback quality whenever possible.</summary>
    HighQuality = 2,
}

public enum StorageHealth
{
    Healthy = 0,
    Warning = 1,
    Critical = 2,
    Unknown = 3,
}

public enum EngineState
{
    Idle = 0,
    Starting = 1,
    Playing = 2,
    Paused = 3,
    Failed = 4,
}

public enum ImportRejectReason
{
    None = 0,
    UnsupportedExtension = 1,
    SignatureMismatch = 2,
    FileTooLarge = 3,
    EmptyFile = 4,
    PathTraversal = 5,
    UnreadableFile = 6,
    AlreadyImported = 7,
    InvalidMedia = 8,
    InsufficientCacheSpace = 9,
}
