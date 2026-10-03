namespace Deskverse.Api;

using Deskverse.Core;
using Deskverse.Core.Abstractions;
using Deskverse.Core.Entities;
using Deskverse.Core.Models;

/// <summary>Stable wire format for a library wallpaper.</summary>
public sealed record WallpaperDto(
    Guid Id,
    string Title,
    string? Description,
    WallpaperKind Kind,
    WallpaperFormat Format,
    int Width,
    int Height,
    long FileSizeBytes,
    string[] Categories,
    string? DominantColor,
    double? Brightness,
    double? VisualDensity,
    bool IsFavorite,
    bool IsPinned,
    bool IsCached,
    bool IsDisliked,
    bool IsUserImported,
    int UseCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastUsedAt,
    string? SourceProvider,
    string? License,
    string? Attribution,
    double AspectRatio)
{
    public static WallpaperDto From(Wallpaper w) => new(
        w.Id,
        w.Title,
        w.Description,
        w.Kind,
        w.Format,
        w.Width,
        w.Height,
        w.FileSizeBytes,
        w.Categories,
        w.DominantColor,
        w.Brightness,
        w.VisualDensity,
        w.IsFavorite,
        w.IsPinned,
        w.IsCached,
        w.IsDisliked,
        w.IsUserImported,
        w.UseCount,
        w.CreatedAt,
        w.LastUsedAt,
        w.SourceProvider,
        w.License,
        w.Attribution,
        w.AspectRatio);
}

public sealed record WallpaperPageDto(IReadOnlyList<WallpaperDto> Items, int TotalCount, int Skip, int Take);

public sealed record ApplyRequestDto(string? DisplayDeviceName);

public sealed record ImportRequestDto(string Path);

public sealed record DownloadRequestDto(string ProviderId, string SourceId);

public sealed record FavoriteRequestDto(bool IsFavorite);

public sealed record PinRequestDto(bool IsPinned);

public sealed record DislikeRequestDto(bool IsDisliked);

public sealed record CreateCollectionRequestDto(string Name);

public sealed record CollectionItemRequestDto(Guid WallpaperId);


public sealed record CollectionDto(Guid Id, string Name, bool IsSystem, DateTimeOffset CreatedAt)
{
    public static CollectionDto From(WallpaperCollection c) => new(c.Id, c.Name, c.IsSystem, c.CreatedAt);
}

public sealed record ProviderItemDto(string ProviderId, string DisplayName, bool RequiresNetwork);

public sealed record ProviderWallpaperDto(
    string ProviderId,
    string SourceId,
    string Title,
    string? Description,
    string? PageUrl,
    WallpaperKind Kind,
    WallpaperFormat Format,
    int Width,
    int Height,
    long FileSizeBytes,
    string? License,
    string? Attribution,
    string[] Categories,
    string? DominantColor,
    double? Brightness)
{
    public static ProviderWallpaperDto From(ProviderWallpaper p) => new(
        p.ProviderId,
        p.SourceId,
        p.Title,
        p.Description,
        p.PageUrl,
        p.Kind,
        p.Format,
        p.Width,
        p.Height,
        p.FileSizeBytes,
        p.License,
        p.Attribution,
        p.Categories,
        p.DominantColor,
        p.Brightness);
}

public sealed record ProviderFailureDto(string ProviderId, string DisplayName, string Error, bool TimedOut);

public sealed record SearchResponseDto(
    IReadOnlyList<ProviderWallpaperDto> Items,
    IReadOnlyList<ProviderFailureDto> Failures,
    bool AllFailed);

public sealed record CategoriesResponseDto(
    IReadOnlyList<string> Categories,
    IReadOnlyList<ProviderFailureDto> Failures);

public sealed record AttributionDto(string ProviderId, string SourceId, string? License, string? Attribution, string? PageUrl, string TermsOfUseSummary);

public sealed record RecommendationDto(
    WallpaperDto Wallpaper,
    double Score,
    IReadOnlyList<RecommendationFactorDto> Factors,
    string Explanation)
{
    public static RecommendationDto From(Recommendation r) => new(
        WallpaperDto.From(r.Wallpaper),
        r.Score,
        r.Factors.Select(f => new RecommendationFactorDto(f.Name, f.Weight, f.Contribution, f.Detail)).ToList(),
        r.Explanation);
}

public sealed record RecommendationFactorDto(string Name, double Weight, double Contribution, string Detail);

public sealed record StorageStatusDto(
    string CacheDirectory,
    long LimitBytes,
    long UsedBytes,
    long AvailableDiskBytes,
    long ActiveWallpaperBytes,
    long PinnedBytes,
    int CachedEntryCount,
    StorageHealth Health,
    string HealthMessage)
{
    public static StorageStatusDto From(StorageStatus s) => new(
        s.CacheDirectory,
        s.LimitBytes,
        s.UsedBytes,
        s.AvailableDiskBytes,
        s.ActiveWallpaperBytes,
        s.PinnedBytes,
        s.CachedEntryCount,
        s.Health,
        s.HealthMessage);
}

public sealed record CleanupResultDto(bool EnoughSpace, long FreedBytes, long ProjectedUsedBytes, IReadOnlyList<string> RemovedTitles, string? Message);

public sealed record PreferencesDto(
    string[] PreferredCategories,
    string[] PreferredColors,
    double? PreferredBrightness,
    string[] PreferredStyles,
    bool AllowVideoWallpapers,
    bool PauseVideoOnBattery,
    bool PauseVideoOnFullscreen,
    bool AnimatedPreviewsEnabled,
    string BackgroundPolicy,
    long StorageLimitBytes,
    string CacheDirectory,
    bool AutoCleanupEnabled,
    bool RunAtStartup,
    bool MinimizeToTray,
    bool TelemetryOptIn,
    bool FirstRunComplete)
{
    public static PreferencesDto From(UserPreferences p) => new(
        p.PreferredCategories,
        p.PreferredColors,
        p.PreferredBrightness,
        p.PreferredStyles,
        p.AllowVideoWallpapers,
        p.PauseVideoOnBattery,
        p.PauseVideoOnFullscreen,
        p.AnimatedPreviewsEnabled,
        p.BackgroundPolicy.ToString(),
        p.StorageLimitBytes,
        p.CacheDirectory,
        p.AutoCleanupEnabled,
        p.RunAtStartup,
        p.MinimizeToTray,
        p.TelemetryOptIn,
        p.FirstRunComplete);

    public void ApplyTo(UserPreferences p)
    {
        p.PreferredCategories = PreferredCategories;
        p.PreferredColors = PreferredColors;
        p.PreferredBrightness = PreferredBrightness;
        p.PreferredStyles = PreferredStyles;
        p.AllowVideoWallpapers = AllowVideoWallpapers;
        p.PauseVideoOnBattery = PauseVideoOnBattery;
        p.PauseVideoOnFullscreen = PauseVideoOnFullscreen;
        p.AnimatedPreviewsEnabled = AnimatedPreviewsEnabled;
        p.BackgroundPolicy = Enum.TryParse<BackgroundResourcePolicy>(BackgroundPolicy, ignoreCase: true, out var policy)
            ? policy
            : p.BackgroundPolicy;
        p.StorageLimitBytes = StorageLimitBytes;
        p.AutoCleanupEnabled = AutoCleanupEnabled;
        p.RunAtStartup = RunAtStartup;
        p.MinimizeToTray = MinimizeToTray;
        p.TelemetryOptIn = TelemetryOptIn;
    }
}

public sealed record UpdatePreferencesRequestDto(PreferencesDto Preferences);

public sealed record SystemHealthDto(
    bool DatabaseConnected,
    bool CacheAvailable,
    bool ApiListening,
    int? ApiPort,
    Guid? ActiveWallpaperId,
    string EngineState,
    TimeSpan Uptime,
    int DisplayCount,
    IReadOnlyList<string> Warnings)
{
    public static SystemHealthDto From(SystemHealth h) => new(
        h.DatabaseConnected,
        h.CacheAvailable,
        h.ApiListening,
        h.ApiPort,
        h.ActiveWallpaperId,
        h.EngineState.ToString(),
        h.Uptime,
        h.DisplayCount,
        h.Warnings);
}

public sealed record EngineStatusDto(
    string State,
    string? ActiveKind,
    Guid? ActiveWallpaperId,
    string? ActiveWallpaperTitle,
    DateTimeOffset? StartedAt,
    string? LastError)
{
    public static EngineStatusDto From(EngineStatus s) => new(
        s.State.ToString(),
        s.ActiveKind?.ToString(),
        s.ActiveWallpaperId,
        s.ActiveWallpaperTitle,
        s.StartedAt,
        s.LastError);
}

public sealed record ImportResultDto(bool Success, WallpaperDto? Wallpaper, string? Reason, string? Detail, string? Message);

public sealed record DownloadResultDto(bool Success, WallpaperDto? Wallpaper, string? Error, bool WasAlreadyCached);

public sealed record ApplyResultDto(bool Success, string? Error, string? Notice);

public sealed record ErrorDto(string Error);

public sealed record SimpleResultDto(bool Success, string? Error);
