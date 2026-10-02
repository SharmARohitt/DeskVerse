namespace Deskverse.Api;

using System.Globalization;
using Deskverse.Application;
using Deskverse.Core;
using Deskverse.Core.Abstractions;
using Deskverse.Core.Models;
using Deskverse.Providers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Primitives;

/// <summary>
/// All local API routes under /api/v1. Every route is loopback-only and bearer-token
/// protected by <see cref="ApiAuthenticationMiddleware"/>; operational outcomes are
/// reported as 200 responses with a success flag, while malformed requests get 4xx.
/// </summary>
public static class ApiEndpoints
{
    public static void Map(IEndpointRouteBuilder routes)
    {
        var api = routes.MapGroup("/api/v1");

        MapWallpapers(api);
        MapImportAndEngine(api);
        MapDiscover(api);
        MapRecommendations(api);
        MapStorage(api);
        MapPreferences(api);
        MapCollections(api);
        MapRotation(api);
        MapHealth(api);
    }

    private static void MapWallpapers(RouteGroupBuilder api)
    {
        api.MapGet("/wallpapers", async (HttpRequest request, WallpaperManager wallpapers, CancellationToken ct) =>
        {
            if (!TryParseQuery(request, out var query, out var error))
            {
                return Results.Json(new ErrorDto(error), statusCode: StatusCodes.Status400BadRequest);
            }

            var page = await wallpapers.QueryAsync(query, ct).ConfigureAwait(false);
            return Results.Json(new WallpaperPageDto(
                page.Items.Select(WallpaperDto.From).ToList(),
                page.TotalCount,
                query.Skip,
                query.Take));
        });

        api.MapGet("/wallpapers/{id:guid}", async (Guid id, WallpaperManager wallpapers, CancellationToken ct) =>
        {
            var wallpaper = await wallpapers.GetByIdAsync(id, ct).ConfigureAwait(false);
            return wallpaper is null
                ? Results.Json(new ErrorDto($"No wallpaper with id {id} exists."), statusCode: StatusCodes.Status404NotFound)
                : Results.Json(WallpaperDto.From(wallpaper));
        });

        api.MapPost("/wallpapers/{id:guid}/apply", async (
            Guid id,
            ApplyRequestDto? body,
            WallpaperManager wallpapers,
            CancellationToken ct) =>
        {
            var outcome = await wallpapers.ApplyAsync(id, body?.DisplayDeviceName, ct).ConfigureAwait(false);
            return Results.Json(new ApplyResultDto(outcome.Success, outcome.Error, outcome.Notice));
        });

        api.MapPut("/wallpapers/{id:guid}/favorite", async (
            Guid id,
            FavoriteRequestDto? body,
            WallpaperManager wallpapers,
            CancellationToken ct) =>
        {
            if (body is null)
            {
                return Results.Json(new ErrorDto("An 'isFavorite' value is required."), statusCode: StatusCodes.Status400BadRequest);
            }

            await wallpapers.SetFavoriteAsync(id, body.IsFavorite, ct).ConfigureAwait(false);
            return Results.Json(new SimpleResultDto(true, null));
        });

        api.MapPut("/wallpapers/{id:guid}/pin", async (
            Guid id,
            PinRequestDto? body,
            WallpaperManager wallpapers,
            CancellationToken ct) =>
        {
            if (body is null)
            {
                return Results.Json(new ErrorDto("An 'isPinned' value is required."), statusCode: StatusCodes.Status400BadRequest);
            }

            await wallpapers.SetPinnedAsync(id, body.IsPinned, ct).ConfigureAwait(false);
            return Results.Json(new SimpleResultDto(true, null));
        });

        api.MapPut("/wallpapers/{id:guid}/dislike", async (
            Guid id,
            DislikeRequestDto? body,
            WallpaperManager wallpapers,
            CancellationToken ct) =>
        {
            if (body is null)
            {
                return Results.Json(new ErrorDto("An 'isDisliked' value is required."), statusCode: StatusCodes.Status400BadRequest);
            }

            await wallpapers.SetDislikedAsync(id, body.IsDisliked, ct).ConfigureAwait(false);
            return Results.Json(new SimpleResultDto(true, null));
        });

        api.MapPost("/wallpapers/{id:guid}/remove-from-cache", async (Guid id, WallpaperManager wallpapers, CancellationToken ct) =>
        {
            var wallpaper = await wallpapers.GetByIdAsync(id, ct).ConfigureAwait(false);
            if (wallpaper is null)
            {
                return Results.Json(new ErrorDto($"No wallpaper with id {id} exists."), statusCode: StatusCodes.Status404NotFound);
            }

            var result = await wallpapers.RemoveFromCacheAsync(id, ct).ConfigureAwait(false);
            return result.Success
                ? Results.Json(new SimpleResultDto(true, null))
                : Results.Json(new SimpleResultDto(false, result.Error), statusCode: StatusCodes.Status409Conflict);
        });

        api.MapDelete("/wallpapers/{id:guid}", async (Guid id, WallpaperManager wallpapers, CancellationToken ct) =>
        {
            var wallpaper = await wallpapers.GetByIdAsync(id, ct).ConfigureAwait(false);
            if (wallpaper is null)
            {
                return Results.Json(new ErrorDto($"No wallpaper with id {id} exists."), statusCode: StatusCodes.Status404NotFound);
            }

            var result = await wallpapers.DeleteAsync(id, ct).ConfigureAwait(false);
            return result.Success
                ? Results.Json(new SimpleResultDto(true, null))
                : Results.Json(new SimpleResultDto(false, result.Error), statusCode: StatusCodes.Status409Conflict);
        });
    }

    private static void MapImportAndEngine(RouteGroupBuilder api)
    {
        api.MapPost("/import", async (ImportRequestDto? body, WallpaperManager wallpapers, CancellationToken ct) =>
        {
            if (body is null || string.IsNullOrWhiteSpace(body.Path))
            {
                return Results.Json(new ErrorDto("A 'path' is required."), statusCode: StatusCodes.Status400BadRequest);
            }

            var outcome = await wallpapers.ImportAsync(body.Path.Trim(), ct).ConfigureAwait(false);
            return Results.Json(new ImportResultDto(
                outcome.Success,
                outcome.Wallpaper is null ? null : WallpaperDto.From(outcome.Wallpaper),
                outcome.Reason.ToString(),
                outcome.Detail,
                outcome.Message));
        });

        api.MapGet("/engine/status", async (WallpaperManager wallpapers, CancellationToken ct) =>
            Results.Json(EngineStatusDto.From(await wallpapers.GetEngineStatusAsync(ct).ConfigureAwait(false))));

        api.MapPost("/engine/stop", async (WallpaperManager wallpapers, CancellationToken ct) =>
            ToSimpleResult(await wallpapers.StopAsync(ct).ConfigureAwait(false)));

        api.MapPost("/engine/pause", async (WallpaperManager wallpapers, CancellationToken ct) =>
            ToSimpleResult(await wallpapers.PauseAsync(ct).ConfigureAwait(false)));

        api.MapPost("/engine/resume", async (WallpaperManager wallpapers, CancellationToken ct) =>
            ToSimpleResult(await wallpapers.ResumeAsync(ct).ConfigureAwait(false)));

        api.MapPost("/engine/restore-previous", async (WallpaperManager wallpapers, CancellationToken ct) =>
            ToSimpleResult(await wallpapers.RestorePreviousAsync(ct).ConfigureAwait(false)));
    }

    private static void MapDiscover(RouteGroupBuilder api)
    {
        api.MapGet("/discover/providers", (DiscoveryService discovery) =>
            Results.Json(discovery.GetProviders()
                .Select(p => new ProviderItemDto(p.ProviderId, p.DisplayName, p.RequiresNetwork))
                .ToList()));

        api.MapGet("/discover/search", async (HttpRequest request, DiscoveryService discovery, CancellationToken ct) =>
        {
            if (!TryParseQuery(request, out var query, out var error))
            {
                return Results.Json(new ErrorDto(error), statusCode: StatusCodes.Status400BadRequest);
            }

            var result = await discovery.SearchAsync(query, ct).ConfigureAwait(false);
            return Results.Json(ToSearchResponse(result));
        });

        api.MapGet("/discover/trending", async (HttpRequest request, DiscoveryService discovery, CancellationToken ct) =>
        {
            var parser = new QueryParser(request);
            var count = parser.Int("count", 1, 100) ?? 24;
            if (parser.Failed)
            {
                return Results.Json(new ErrorDto(parser.Error), statusCode: StatusCodes.Status400BadRequest);
            }

            var result = await discovery.GetTrendingAsync(count, ct).ConfigureAwait(false);
            return Results.Json(ToSearchResponse(result));
        });

        api.MapGet("/discover/categories", async (DiscoveryService discovery, CancellationToken ct) =>
        {
            var result = await discovery.GetCategoriesAsync(ct).ConfigureAwait(false);
            return Results.Json(new CategoriesResponseDto(
                result.Items,
                result.Failures.Select(ToFailureDto).ToList()));
        });

        api.MapGet("/discover/attribution/{providerId}/{sourceId}", async (
            string providerId,
            string sourceId,
            DiscoveryService discovery,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(providerId) || string.IsNullOrWhiteSpace(sourceId))
            {
                return Results.Json(new ErrorDto("A provider id and source id are required."), statusCode: StatusCodes.Status400BadRequest);
            }

            var result = await discovery.GetAttributionAsync(providerId, sourceId, ct).ConfigureAwait(false);
            if (result.Success && result.Items is not null)
            {
                var a = result.Items;
                return Results.Json(new AttributionDto(a.ProviderId, a.SourceId, a.License, a.Attribution, a.PageUrl, a.TermsOfUseSummary));
            }

            return Results.Json(
                new ErrorDto(result.Error ?? "Attribution is unavailable for this item."),
                statusCode: StatusCodes.Status404NotFound);
        });

        api.MapPost("/discover/download", async (DownloadRequestDto? body, DiscoveryService discovery, CancellationToken ct) =>
        {
            if (body is null || string.IsNullOrWhiteSpace(body.ProviderId) || string.IsNullOrWhiteSpace(body.SourceId))
            {
                return Results.Json(
                    new ErrorDto("Both 'providerId' and 'sourceId' are required."),
                    statusCode: StatusCodes.Status400BadRequest);
            }

            var outcome = await discovery.DownloadAsync(body.ProviderId.Trim(), body.SourceId.Trim(), ct).ConfigureAwait(false);
            return Results.Json(new DownloadResultDto(
                outcome.Success,
                outcome.Wallpaper is null ? null : WallpaperDto.From(outcome.Wallpaper),
                outcome.Error,
                outcome.WasAlreadyCached));
        });
    }

    private static void MapRecommendations(RouteGroupBuilder api)
    {
        api.MapGet("/recommendations", async (HttpRequest request, RecommendationService recommendations, CancellationToken ct) =>
        {
            var parser = new QueryParser(request);
            var count = parser.Int("count", 1, 50) ?? 8;
            if (parser.Failed)
            {
                return Results.Json(new ErrorDto(parser.Error), statusCode: StatusCodes.Status400BadRequest);
            }

            var ranked = await recommendations.GetRecommendationsAsync(count, ct).ConfigureAwait(false);
            return Results.Json(ranked.Select(RecommendationDto.From).ToList());
        });

        api.MapPost("/recommendations/surprise", async (RecommendationService recommendations, CancellationToken ct) =>
        {
            var (recommendation, outcome) = await recommendations.SurpriseMeAsync(ct).ConfigureAwait(false);
            return Results.Json(new SurpriseResultDto(
                recommendation is null ? null : RecommendationDto.From(recommendation),
                new ApplyResultDto(outcome.Success, outcome.Error, outcome.Notice)));
        });
    }

    private static void MapStorage(RouteGroupBuilder api)
    {
        api.MapGet("/storage/status", async (StorageService storage, CancellationToken ct) =>
            Results.Json(StorageStatusDto.From(await storage.GetStatusAsync(ct).ConfigureAwait(false))));

        api.MapPost("/storage/cleanup", async (StorageService storage, CancellationToken ct) =>
        {
            var result = await storage.CleanupNowAsync(ct).ConfigureAwait(false);
            return Results.Json(new CleanupResultDto(
                result.EnoughSpace,
                result.FreedBytes,
                result.ProjectedUsedBytes,
                result.RemovedTitles,
                result.Message));
        });

        api.MapPost("/storage/reconcile", async (StorageService storage, CancellationToken ct) =>
        {
            await storage.ReconcileAsync(ct).ConfigureAwait(false);
            return Results.Json(new SimpleResultDto(true, null));
        });

        api.MapPost("/storage/directory", async (ChangeCacheDirectoryRequestDto? body, StorageService storage, CancellationToken ct) =>
        {
            if (body is null || string.IsNullOrWhiteSpace(body.Path))
            {
                return Results.Json(new ErrorDto("A 'path' is required."), statusCode: StatusCodes.Status400BadRequest);
            }

            var result = await storage.ChangeCacheDirectoryAsync(body.Path.Trim(), ct).ConfigureAwait(false);
            return result.Success
                ? Results.Json(new SimpleResultDto(true, null))
                : Results.Json(new SimpleResultDto(false, result.Error), statusCode: StatusCodes.Status400BadRequest);
        });
    }

    private static void MapPreferences(RouteGroupBuilder api)
    {
        api.MapGet("/preferences", async (PreferencesService preferences, CancellationToken ct) =>
            Results.Json(PreferencesDto.From(await preferences.LoadAsync(ct).ConfigureAwait(false))));

        api.MapPut("/preferences", async (UpdatePreferencesRequestDto? body, PreferencesService preferences, CancellationToken ct) =>
        {
            if (body?.Preferences is null)
            {
                return Results.Json(new ErrorDto("A 'preferences' object is required."), statusCode: StatusCodes.Status400BadRequest);
            }

            var current = await preferences.LoadAsync(ct).ConfigureAwait(false);
            body.Preferences.ApplyTo(current);
            var result = await preferences.SaveAsync(current, ct).ConfigureAwait(false);
            return result.Success
                ? Results.Json(new SimpleResultDto(true, null))
                : Results.Json(new SimpleResultDto(false, result.Error), statusCode: StatusCodes.Status400BadRequest);
        });
    }

    private static void MapCollections(RouteGroupBuilder api)
    {
        api.MapGet("/collections", async (CollectionsService collections, CancellationToken ct) =>
            Results.Json((await collections.GetAllAsync(ct).ConfigureAwait(false))
                .Select(CollectionDto.From)
                .ToList()));

        api.MapPost("/collections", async (CreateCollectionRequestDto? body, CollectionsService collections, CancellationToken ct) =>
        {
            if (body is null || string.IsNullOrWhiteSpace(body.Name))
            {
                return Results.Json(new ErrorDto("A 'name' is required."), statusCode: StatusCodes.Status400BadRequest);
            }

            var result = await collections.CreateAsync(body.Name, ct).ConfigureAwait(false);
            return result.Success
                ? Results.Json(CollectionDto.From(result.Value!), statusCode: StatusCodes.Status201Created)
                : Results.Json(new ErrorDto(result.Error ?? "The collection could not be created."), statusCode: StatusCodes.Status400BadRequest);
        });

        api.MapGet("/collections/{id:guid}", async (Guid id, CollectionsService collections, CancellationToken ct) =>
        {
            var collection = await collections.GetAsync(id, ct).ConfigureAwait(false);
            return collection is null
                ? Results.Json(new ErrorDto($"No collection with id {id} exists."), statusCode: StatusCodes.Status404NotFound)
                : Results.Json(CollectionDto.From(collection));
        });

        api.MapPost("/collections/{id:guid}/rename", async (Guid id, RenameCollectionRequestDto? body, CollectionsService collections, CancellationToken ct) =>
        {
            if (body is null || string.IsNullOrWhiteSpace(body.Name))
            {
                return Results.Json(new ErrorDto("A 'name' is required."), statusCode: StatusCodes.Status400BadRequest);
            }

            try
            {
                await collections.RenameAsync(id, body.Name, ct).ConfigureAwait(false);
                return Results.Json(new SimpleResultDto(true, null));
            }
            catch (ArgumentException ex)
            {
                return Results.Json(new ErrorDto(ex.Message), statusCode: StatusCodes.Status400BadRequest);
            }
        });

        api.MapDelete("/collections/{id:guid}", async (Guid id, CollectionsService collections, CancellationToken ct) =>
        {
            var deleted = await collections.DeleteAsync(id, ct).ConfigureAwait(false);
            return deleted
                ? Results.Json(new SimpleResultDto(true, null))
                : Results.Json(
                    new SimpleResultDto(false, "The collection does not exist or is a protected system collection."),
                    statusCode: StatusCodes.Status404NotFound);
        });

        api.MapPost("/collections/{id:guid}/items", async (Guid id, CollectionItemRequestDto? body, CollectionsService collections, CancellationToken ct) =>
        {
            if (body is null || body.WallpaperId == Guid.Empty)
            {
                return Results.Json(new ErrorDto("A 'wallpaperId' is required."), statusCode: StatusCodes.Status400BadRequest);
            }

            var result = await collections.AddWallpaperAsync(id, body.WallpaperId, ct).ConfigureAwait(false);
            return result.Success
                ? Results.Json(new SimpleResultDto(true, null))
                : Results.Json(new SimpleResultDto(false, result.Error), statusCode: StatusCodes.Status400BadRequest);
        });

        api.MapDelete("/collections/{id:guid}/items/{wallpaperId:guid}", async (
            Guid id,
            Guid wallpaperId,
            CollectionsService collections,
            CancellationToken ct) =>
        {
            await collections.RemoveWallpaperAsync(id, wallpaperId, ct).ConfigureAwait(false);
            return Results.Json(new SimpleResultDto(true, null));
        });
    }

    private static void MapHealth(RouteGroupBuilder api)
    {
        api.MapGet("/health", async (SystemHealthService health, CancellationToken ct) =>
            Results.Json(SystemHealthDto.From(await health.GetHealthAsync(ct).ConfigureAwait(false))));
    }

    private static void MapRotation(RouteGroupBuilder api)
    {
        api.MapGet("/rotation/status", (RotationScheduler scheduler) =>
            Results.Json(new RotationStatusDto(
                scheduler.IsRunning,
                scheduler.NextAt == DateTimeOffset.MaxValue ? null : scheduler.NextAt)));

        api.MapPost("/rotation/start", async (RotationScheduler scheduler, CancellationToken ct) =>
        {
            await scheduler.StartAsync(ct).ConfigureAwait(false);
            return Results.Json(new SimpleResultDto(true, null));
        });

        api.MapPost("/rotation/stop", async (RotationScheduler scheduler, CancellationToken ct) =>
        {
            await scheduler.StopAsync().ConfigureAwait(false);
            return Results.Json(new SimpleResultDto(true, null));
        });

        api.MapPost("/rotation/advance", async (RotationScheduler scheduler, CancellationToken ct) =>
        {
            await scheduler.AdvanceNowAsync(ct).ConfigureAwait(false);
            return Results.Json(new SimpleResultDto(true, null));
        });
    }

    private static IResult ToSimpleResult(OperationResult result) =>
        result.Success
            ? Results.Json(new SimpleResultDto(true, null))
            : Results.Json(new SimpleResultDto(false, result.Error));

    private static SearchResponseDto ToSearchResponse(AggregatedResult<ProviderWallpaper> result) => new(
        result.Items.Select(ProviderWallpaperDto.From).ToList(),
        result.Failures.Select(ToFailureDto).ToList(),
        result.AllFailed);

    private static ProviderFailureDto ToFailureDto(ProviderFailure failure) =>
        new(failure.ProviderId, failure.DisplayName, failure.Error, failure.TimedOut);

    private static bool TryParseQuery(HttpRequest request, out WallpaperQuery query, out string error)
    {
        var parser = new QueryParser(request);
        query = new WallpaperQuery
        {
            SearchText = parser.Text("search"),
            Categories = parser.List("category"),
            Kind = parser.EnumValue<WallpaperKind>("kind"),
            MinWidth = parser.Int("minWidth", 0, 100_000),
            MinHeight = parser.Int("minHeight", 0, 100_000),
            AspectRatio = parser.Double("aspect", 0.05, 32),
            ColorHex = parser.Hex("color"),
            MinBrightness = parser.Double("minBrightness", 0, 1),
            MaxBrightness = parser.Double("maxBrightness", 0, 1),
            FavoritesOnly = parser.Flag("favorites") ?? false,
            PinnedOnly = parser.Flag("pinned") ?? false,
            CachedOnly = parser.Flag("cached") ?? false,
            UserImportedOnly = parser.Flag("imported") ?? false,
            ExcludeDisliked = !(parser.Flag("includeDisliked") ?? false),
            SortBy = parser.EnumValue<SortOrder>("sort") ?? SortOrder.NewestFirst,
            Skip = parser.Int("skip", 0, 10_000_000) ?? 0,
            Take = parser.Int("take", 1, 200) ?? 60,
        };

        error = parser.Error;
        return !parser.Failed;
    }

    /// <summary>Strict query-string parsing: every malformed value becomes a 400, never a silent default.</summary>
    private sealed class QueryParser
    {
        private readonly HttpRequest _request;
        private readonly List<string> _errors = [];

        public QueryParser(HttpRequest request) => _request = request;

        public bool Failed => _errors.Count > 0;

        public string Error => string.Join(' ', _errors);

        public string? Text(string name)
        {
            if (!_request.Query.TryGetValue(name, out var values) || StringValues.IsNullOrEmpty(values))
            {
                return null;
            }

            var text = values.ToString().Trim();
            return text.Length == 0 ? null : text;
        }

        public string[] List(string name)
        {
            if (!_request.Query.TryGetValue(name, out var values) || StringValues.IsNullOrEmpty(values))
            {
                return [];
            }

            return values
                .SelectMany(v => v!.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        public T? EnumValue<T>(string name) where T : struct, Enum
        {
            var text = Text(name);
            if (text is null)
            {
                return null;
            }

            if (Enum.TryParse(text, ignoreCase: true, out T value))
            {
                return value;
            }

            _errors.Add($"'{name}' must be one of: {string.Join(", ", Enum.GetNames<T>())}.");
            return null;
        }

        public bool? Flag(string name)
        {
            var text = Text(name);
            if (text is null)
            {
                return null;
            }

            if (bool.TryParse(text, out var value))
            {
                return value;
            }

            _errors.Add($"'{name}' must be 'true' or 'false'.");
            return null;
        }

        public int? Int(string name, int min, int max)
        {
            var text = Text(name);
            if (text is null)
            {
                return null;
            }

            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
                && value >= min && value <= max)
            {
                return value;
            }

            _errors.Add($"'{name}' must be an integer between {min} and {max}.");
            return null;
        }

        public double? Double(string name, double min, double max)
        {
            var text = Text(name);
            if (text is null)
            {
                return null;
            }

            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                && value >= min && value <= max)
            {
                return value;
            }

            _errors.Add($"'{name}' must be a number between {min} and {max}.");
            return null;
        }

        public string? Hex(string name)
        {
            var text = Text(name);
            if (text is null)
            {
                return null;
            }

            if (text.Length == 6 && text.All(Uri.IsHexDigit))
            {
                return text.ToUpperInvariant();
            }

            _errors.Add($"'{name}' must be a six-digit RRGGBB hex color.");
            return null;
        }
    }
}

public sealed record ChangeCacheDirectoryRequestDto(string Path);

public sealed record RenameCollectionRequestDto(string Name);

public sealed record SurpriseResultDto(RecommendationDto? Recommendation, ApplyResultDto Outcome);

public sealed record RotationStatusDto(bool IsRunning, DateTimeOffset? NextAt);
