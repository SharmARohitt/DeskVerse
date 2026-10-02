namespace Deskverse.Providers;

/// <summary>
/// Wallhaven provider configuration. The API key is optional — unauthenticated
/// requests can access SFW content. Set DESKVERSE_WALLHAVEN_APIKEY in the
/// environment or appsettings.json to unlock adult/sketchy content and higher
/// rate limits. Never store it in committed source.
/// </summary>
public sealed class WallhavenOptions
{
    public const string SectionKey = "Wallhaven";

    /// <summary>API key read from DESKVERSE_WALLHAVEN_APIKEY environment variable or configuration.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Whether to include NSFW results. Requires an API key. Default: false.</summary>
    public bool AllowNsfw { get; set; }

    /// <summary>Purity filter: sfw, sketchy, or nsfw (comma-separated). Default "sfw".</summary>
    public string Purity { get; set; } = "sfw";

    /// <summary>Sorting for search results (date_added, relevance, random, views, favorites, toplist).</summary>
    public string DefaultSort { get; set; } = "date_added";

    /// <summary>Request timeout in seconds. Default 15.</summary>
    public int TimeoutSeconds { get; set; } = 15;

    /// <summary>Max wallpapers to return per search or trending call.</summary>
    public int PageSize { get; set; } = 24;
}
