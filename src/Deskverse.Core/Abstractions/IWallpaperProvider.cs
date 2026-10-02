namespace Deskverse.Core.Abstractions;

using Deskverse.Core;
using Deskverse.Core.Models;

/// <summary>A wallpaper descriptor returned by a provider before download.</summary>
public sealed record ProviderWallpaper(
    string ProviderId,
    string SourceId,
    string Title,
    string? Description,
    string? PageUrl,
    string? DownloadUrl,
    WallpaperKind Kind,
    WallpaperFormat Format,
    int Width,
    int Height,
    long FileSizeBytes,
    string? License,
    string? Attribution,
    string[] Categories,
    string? DominantColor,
    double? Brightness);

/// <summary>Attribution and license details for provider content.</summary>
public sealed record ProviderAttribution(
    string ProviderId,
    string SourceId,
    string? License,
    string? Attribution,
    string? PageUrl,
    string TermsOfUseSummary);

/// <summary>Outcome of one provider call: items or a failure description, never both.</summary>
public sealed record ProviderResult<T>
{
    public T? Items { get; init; }

    public bool Success { get; init; }

    public string? Error { get; init; }

    public bool TimedOut { get; init; }

    public static ProviderResult<T> Ok(T items) => new() { Success = true, Items = items };

    public static ProviderResult<T> Fail(string error, bool timedOut = false) =>
        new() { Success = false, Error = error, TimedOut = timedOut };
}

/// <summary>
/// A source of wallpaper content. External providers are added as adapters;
/// the local library provider is always available so the app works offline.
/// </summary>
public interface IWallpaperProvider
{
    string ProviderId { get; }

    string DisplayName { get; }

    bool RequiresNetwork { get; }

    Task<ProviderResult<IReadOnlyList<ProviderWallpaper>>> SearchAsync(
        WallpaperQuery query,
        CancellationToken cancellationToken = default);

    Task<ProviderResult<IReadOnlyList<ProviderWallpaper>>> GetTrendingAsync(
        int count,
        CancellationToken cancellationToken = default);

    Task<ProviderResult<ProviderWallpaper>> GetDetailsAsync(
        string sourceId,
        CancellationToken cancellationToken = default);

    Task<ProviderResult<IReadOnlyList<string>>> GetCategoriesAsync(
        CancellationToken cancellationToken = default);

    Task<ProviderResult<ProviderAttribution>> GetAttributionAsync(
        string sourceId,
        CancellationToken cancellationToken = default);
}
