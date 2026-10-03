namespace Deskverse.Providers;

using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Deskverse.Core;
using Deskverse.Core.Abstractions;
using Deskverse.Core.Models;
using Deskverse.Security.Network;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// Wallhaven.cc provider adapter. Wallhaven offers a free public API (SFW content
/// without a key; authenticated requests unlock higher rate limits and NSFW).
///
/// Privacy: only search terms and category filters are sent to Wallhaven.
/// No user data, usage history, or taste profile leaves the device.
///
/// Terms: https://wallhaven.cc/terms
/// Attribution: wallhaven.cc — content is user-uploaded with various licenses.
/// </summary>
public sealed class WallhavenProvider : IWallpaperProvider
{
    public const string ProviderName = "wallhaven";

    private const string BaseUrl = "https://wallhaven.cc/api/v1";
    private const string ThumbBase = "https://th.wallhaven.cc/small";
    private const string FullBase = "https://w.wallhaven.cc/full";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly string[] BuiltInCategories =
    [
        "General", "Anime", "People",
        "Nature", "Architecture", "Abstract", "Cars", "Sci-Fi", "Space",
        "Fantasy", "Minimalist", "Cyberpunk", "Landscape", "Game",
    ];

    private readonly HttpClient _http;
    private readonly WallhavenOptions _options;
    private readonly UrlPolicy _urlPolicy;
    private readonly ILogger<WallhavenProvider> _logger;

    public WallhavenProvider(
        HttpClient httpClient,
        IOptions<WallhavenOptions> options,
        UrlPolicy urlPolicy,
        ILogger<WallhavenProvider> logger)
    {
        _http = httpClient;
        _options = options.Value;
        _urlPolicy = urlPolicy;
        _logger = logger;
        ConfigureClient();
    }

    public string ProviderId => ProviderName;
    public string DisplayName => "Wallhaven";
    public bool RequiresNetwork => true;

    public async Task<ProviderResult<IReadOnlyList<ProviderWallpaper>>> SearchAsync(
        WallpaperQuery query,
        CancellationToken cancellationToken = default)
    {
        var q = query.SearchText ?? string.Empty;
        if (query.Categories.Length > 0)
        {
            q = string.IsNullOrWhiteSpace(q)
                ? string.Join(" ", query.Categories)
                : $"{q} {string.Join(" ", query.Categories)}";
        }

        var purity = BuildPurity();
        var url = $"{BaseUrl}/search?q={Uri.EscapeDataString(q)}&purity={purity}&sorting={Uri.EscapeDataString(_options.DefaultSort)}&atleast={ResolutionParam(query)}&page=1";

        return await FetchWallpapersAsync(url, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ProviderResult<IReadOnlyList<ProviderWallpaper>>> GetTrendingAsync(
        int count,
        CancellationToken cancellationToken = default)
    {
        var purity = BuildPurity();
        var url = $"{BaseUrl}/search?purity={purity}&sorting=toplist&topRange=1M&page=1";
        return await FetchWallpapersAsync(url, cancellationToken).ConfigureAwait(false);
    }

    public Task<ProviderResult<IReadOnlyList<string>>> GetCategoriesAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult(ProviderResult<IReadOnlyList<string>>.Ok(BuiltInCategories));

    public async Task<ProviderResult<ProviderWallpaper>> GetDetailsAsync(
        string sourceId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourceId))
        {
            return ProviderResult<ProviderWallpaper>.Fail("sourceId is required.");
        }

        // The id is interpolated into the request path, so it must stay a single
        // path segment: no traversal, query, or fragment injection.
        if (!IsValidSourceId(sourceId))
        {
            return ProviderResult<ProviderWallpaper>.Fail("sourceId has an unexpected format.");
        }

        var url = $"{BaseUrl}/w/{sourceId}";
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

            var response = await _http.GetAsync(url, cts.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return ProviderResult<ProviderWallpaper>.Fail(
                    $"Wallhaven returned {(int)response.StatusCode} for wallpaper {sourceId}.");
            }

            var json = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
            var envelope = JsonSerializer.Deserialize<WallhavenDetailEnvelope>(json, JsonOpts);
            var item = envelope?.Data;
            if (item is null)
            {
                return ProviderResult<ProviderWallpaper>.Fail("Wallhaven returned an empty detail response.");
            }

            return ProviderResult<ProviderWallpaper>.Ok(Map(item));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ProviderResult<ProviderWallpaper>.Fail($"Wallhaven timed out fetching details for {sourceId}.", timedOut: true);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Wallhaven HTTP error fetching details for {Id}", sourceId);
            return ProviderResult<ProviderWallpaper>.Fail($"Wallhaven network error: {ex.Message}");
        }
    }

    public Task<ProviderResult<ProviderAttribution>> GetAttributionAsync(
        string sourceId,
        CancellationToken cancellationToken = default)
    {
        var attribution = new ProviderAttribution(
            ProviderId,
            sourceId,
            License: null,
            Attribution: "Wallhaven.cc — uploaded by the community",
            PageUrl: $"https://wallhaven.cc/w/{sourceId}",
            TermsOfUseSummary: "Images are user-uploaded to Wallhaven. Refer to wallhaven.cc/terms for full terms of service.");

        return Task.FromResult(ProviderResult<ProviderAttribution>.Ok(attribution));
    }

    // ── Private helpers ──────────────────────────────────────────────────

    private async Task<ProviderResult<IReadOnlyList<ProviderWallpaper>>> FetchWallpapersAsync(
        string url,
        CancellationToken cancellationToken)
    {
        // Validate the URL before sending (SSRF protection).
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return ProviderResult<IReadOnlyList<ProviderWallpaper>>.Fail("Constructed an invalid Wallhaven URL.");
        }

        var policy = _urlPolicy.Validate(uri);
        if (!policy.Allowed)
        {
            _logger.LogWarning("Wallhaven URL blocked by policy: {Reason}", policy.Reason);
            return ProviderResult<IReadOnlyList<ProviderWallpaper>>.Fail($"URL blocked: {policy.Reason}");
        }

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

            var response = await _http.GetAsync(url, cts.Token).ConfigureAwait(false);

            if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                return ProviderResult<IReadOnlyList<ProviderWallpaper>>.Fail(
                    "Wallhaven rate limit reached. Try again in a moment.");
            }

            if (!response.IsSuccessStatusCode)
            {
                return ProviderResult<IReadOnlyList<ProviderWallpaper>>.Fail(
                    $"Wallhaven returned HTTP {(int)response.StatusCode}.");
            }

            var json = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
            var envelope = JsonSerializer.Deserialize<WallhavenSearchEnvelope>(json, JsonOpts);
            if (envelope?.Data is null)
            {
                return ProviderResult<IReadOnlyList<ProviderWallpaper>>.Fail("Wallhaven returned an empty response.");
            }

            var items = envelope.Data
                .Take(_options.PageSize)
                .Select(Map)
                .ToList();

            _logger.LogInformation("Wallhaven returned {Count} results.", items.Count);
            return ProviderResult<IReadOnlyList<ProviderWallpaper>>.Ok(items);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ProviderResult<IReadOnlyList<ProviderWallpaper>>.Fail("Wallhaven request timed out.", timedOut: true);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Wallhaven HTTP error");
            return ProviderResult<IReadOnlyList<ProviderWallpaper>>.Fail($"Wallhaven network error: {ex.Message}");
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Wallhaven JSON parse error");
            return ProviderResult<IReadOnlyList<ProviderWallpaper>>.Fail("Wallhaven returned unexpected JSON.");
        }
    }

    private static ProviderWallpaper Map(WallhavenWallpaper w)
    {
        // Derive kind and format from the path extension.
        var ext = Path.GetExtension(w.Path ?? string.Empty).TrimStart('.').ToLowerInvariant();
        var (kind, format) = ext switch
        {
            "mp4" => (WallpaperKind.Video, WallpaperFormat.Mp4),
            "webm" => (WallpaperKind.Video, WallpaperFormat.WebM),
            "gif" => (WallpaperKind.Static, WallpaperFormat.Gif),
            "png" => (WallpaperKind.Static, WallpaperFormat.Png),
            "webp" => (WallpaperKind.Static, WallpaperFormat.WebP),
            _ => (WallpaperKind.Static, WallpaperFormat.Jpeg),
        };

        var tags = (w.Tags ?? [])
            .Select(t => t.Name ?? string.Empty)
            .Where(t => t.Length > 0)
            .Take(8)
            .ToArray();

        return new ProviderWallpaper(
            ProviderId: ProviderName,
            SourceId: w.Id ?? string.Empty,
            Title: $"Wallhaven {w.Id}",
            Description: null,
            PageUrl: w.Url,
            DownloadUrl: w.Path,
            Kind: kind,
            Format: format,
            Width: w.Dimension_X,
            Height: w.Dimension_Y,
            FileSizeBytes: w.File_Size,
            License: null,
            Attribution: "wallhaven.cc",
            Categories: tags,
            DominantColor: NormalizeHex(w.Colors?.FirstOrDefault()),
            Brightness: null);
    }

    /// <summary>Wallhaven ids are six alphanumeric characters used as a single path segment.</summary>
    private static bool IsValidSourceId(string sourceId) =>
        sourceId.Length is > 0 and <= 16
        && sourceId.All(char.IsAsciiLetterOrDigit);

    private static string? NormalizeHex(string? color)
    {
        if (color is null)
        {
            return null;
        }

        var hex = color.TrimStart('#');
        return hex.Length == 6 ? hex.ToUpperInvariant() : null;
    }

    private void ConfigureClient()
    {
        _http.BaseAddress = null;
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("DeskVerse/1.0 (wallpaper-manager; +https://github.com/deskverse)");
        _http.Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds + 5);

        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            _http.DefaultRequestHeaders.Remove("X-API-Key");
            _http.DefaultRequestHeaders.Add("X-API-Key", _options.ApiKey);
        }
    }

    private string BuildPurity()
    {
        // Build purity bitmask: sfw=1xx, sketchy=x1x, nsfw=xx1
        var purity = _options.Purity.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.ToLowerInvariant())
            .ToHashSet();

        var sfw = purity.Contains("sfw") ? "1" : "0";
        var sketchy = purity.Contains("sketchy") ? "1" : "0";
        var nsfw = _options.AllowNsfw && purity.Contains("nsfw") ? "1" : "0";
        return $"{sfw}{sketchy}{nsfw}";
    }

    private static string ResolutionParam(WallpaperQuery query)
    {
        var w = query.MinWidth ?? 1920;
        var h = query.MinHeight ?? 1080;
        return $"{w}x{h}";
    }

    // ── JSON shapes ───────────────────────────────────────────────────────

    private sealed class WallhavenSearchEnvelope
    {
        [JsonPropertyName("data")] public List<WallhavenWallpaper>? Data { get; set; }
    }

    private sealed class WallhavenDetailEnvelope
    {
        [JsonPropertyName("data")] public WallhavenWallpaper? Data { get; set; }
    }

    private sealed class WallhavenWallpaper
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("url")] public string? Url { get; set; }
        [JsonPropertyName("path")] public string? Path { get; set; }
        [JsonPropertyName("file_size")] public long File_Size { get; set; }
        [JsonPropertyName("dimension_x")] public int Dimension_X { get; set; }
        [JsonPropertyName("dimension_y")] public int Dimension_Y { get; set; }
        [JsonPropertyName("colors")] public List<string>? Colors { get; set; }
        [JsonPropertyName("tags")] public List<WallhavenTag>? Tags { get; set; }
    }

    private sealed class WallhavenTag
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
    }
}
