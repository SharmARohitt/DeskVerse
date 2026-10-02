namespace Deskverse.Application;

using Deskverse.Core;
using Deskverse.Core.Abstractions;
using Deskverse.Core.Entities;
using Deskverse.Core.Models;
using Deskverse.Providers;
using Deskverse.Security.FileValidation;
using Deskverse.Security.Network;
using Deskverse.Storage;
using Microsoft.Extensions.Logging;

/// <summary>
/// Central orchestration for the wallpaper library: secure import, provider
/// downloads into the managed cache, applying wallpapers through the engine,
/// usage bookkeeping, and automatic cache cleanup after applies.
/// </summary>
public sealed class WallpaperManager
{
    private readonly IWallpaperRepository _repository;
    private readonly IUsageRepository _usageRepository;
    private readonly IWallpaperEngine _engine;
    private readonly CacheManager _cacheManager;
    private readonly ImportValidator _importValidator;
    private readonly SecureDownloadService _downloadService;
    private readonly ProviderAggregator _providers;
    private readonly IVisualAnalyzer _visualAnalyzer;
    private readonly IThumbnailService _thumbnailService;
    private readonly IAppEnvironment _environment;
    private readonly IPreferencesStore _preferencesStore;
    private readonly ILogger<WallpaperManager> _logger;
    private readonly object _gate = new();

    private Guid? _activeWallpaperId;

    public WallpaperManager(
        IWallpaperRepository repository,
        IUsageRepository usageRepository,
        IWallpaperEngine engine,
        CacheManager cacheManager,
        ImportValidator importValidator,
        SecureDownloadService downloadService,
        ProviderAggregator providers,
        IVisualAnalyzer visualAnalyzer,
        IThumbnailService thumbnailService,
        IAppEnvironment environment,
        IPreferencesStore preferencesStore,
        ILogger<WallpaperManager> logger)
    {
        _repository = repository;
        _usageRepository = usageRepository;
        _engine = engine;
        _cacheManager = cacheManager;
        _importValidator = importValidator;
        _downloadService = downloadService;
        _providers = providers;
        _visualAnalyzer = visualAnalyzer;
        _thumbnailService = thumbnailService;
        _environment = environment;
        _preferencesStore = preferencesStore;
        _logger = logger;
    }

    public event EventHandler<Wallpaper>? WallpaperImported;

    public event EventHandler<Guid>? WallpaperDeleted;

    /// <summary>The wallpaper currently applied to the desktop, when one is known.</summary>
    public Guid? ActiveWallpaperId
    {
        get
        {
            lock (_gate)
            {
                return _activeWallpaperId;
            }
        }
    }

    public Task<WallpaperPage> QueryAsync(WallpaperQuery query, CancellationToken cancellationToken = default) =>
        _repository.QueryAsync(query, cancellationToken);

    public Task<Wallpaper?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        _repository.GetByIdAsync(id, cancellationToken);

    public Task<int> CountAsync(CancellationToken cancellationToken = default) =>
        _repository.CountAsync(cancellationToken);

    public Task<EngineStatus> GetEngineStatusAsync(CancellationToken cancellationToken = default) =>
        _engine.GetStatusAsync(cancellationToken);

    /// <summary>
    /// Imports a local file. The original file is copied into the managed cache
    /// and never modified, moved, or deleted.
    /// </summary>
    public async Task<ImportOutcome> ImportAsync(string sourcePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            return ImportOutcome.Reject(ImportRejectReason.UnreadableFile, "No file was specified.");
        }

        var validation = await _importValidator.ValidateFileAsync(sourcePath, cancellationToken).ConfigureAwait(false);
        if (!validation.Accepted)
        {
            _logger.LogInformation(
                "Import rejected ({Reason}): {Path} — {Detail}",
                validation.Reason, sourcePath, validation.Detail);
            return ImportOutcome.Reject(validation.Reason, validation.Detail ?? "The file did not pass validation.");
        }

        var duplicate = await _repository.GetByHashAsync(validation.FileHash, cancellationToken).ConfigureAwait(false);
        if (duplicate is not null)
        {
            return ImportOutcome.Reject(
                ImportRejectReason.AlreadyImported,
                $"'{duplicate.Title}' already contains this exact file in your library.");
        }

        var space = await _cacheManager.EnsureSpaceAsync(
            validation.FileSizeBytes, ActiveWallpaperId, cancellationToken).ConfigureAwait(false);
        if (!space.EnoughSpace)
        {
            return ImportOutcome.Reject(
                ImportRejectReason.InsufficientCacheSpace,
                space.Message ?? "The cache limit does not leave room for this file.");
        }

        var relativePath = BuildCacheRelativePath(validation.Kind, validation.FileHash, Path.GetExtension(sourcePath));
        var stagedPath = await StageCopyAsync(sourcePath, cancellationToken).ConfigureAwait(false);
        if (stagedPath is null)
        {
            return ImportOutcome.Reject(ImportRejectReason.UnreadableFile,
                $"The file could not be read for copying: {sourcePath}");
        }

        var promoted = await _cacheManager.PromoteToCacheAsync(stagedPath, relativePath, cancellationToken).ConfigureAwait(false);
        if (!promoted.Success)
        {
            TryDeleteSilently(stagedPath);
            return ImportOutcome.Reject(ImportRejectReason.UnreadableFile,
                promoted.Error ?? "The file could not be stored in the cache.");
        }

        VisualFeatures? features = null;
        if (validation.Kind == WallpaperKind.Static)
        {
            var absolute = _cacheManager.ResolveCachePath(relativePath);
            if (absolute is not null)
            {
                features = await TryAnalyzeAsync(absolute, cancellationToken).ConfigureAwait(false);
            }
        }

        var wallpaper = new Wallpaper
        {
            Title = BuildTitle(sourcePath),
            SourceProvider = LocalWallpaperProvider.ProviderName,
            SourceId = validation.FileHash,
            OriginalImportPath = Path.GetFullPath(sourcePath),
            CacheRelativePath = relativePath,
            FileHash = validation.FileHash,
            Kind = validation.Kind,
            Format = validation.Format,
            Width = features?.Width ?? validation.Width,
            Height = features?.Height ?? validation.Height,
            FileSizeBytes = validation.FileSizeBytes,
            Categories = [],
            DominantColor = features?.DominantColorHex,
            Brightness = features?.Brightness,
            VisualDensity = features?.VisualDensity,
            IsUserImported = true,
            IsCached = true,
            SafetyStatus = SafetyStatus.Safe,
        };

        wallpaper = await _repository.AddAsync(wallpaper, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation(
            "Imported wallpaper {Id} '{Title}' ({Kind}, {Bytes:N0} bytes).",
            wallpaper.Id, wallpaper.Title, wallpaper.Kind, wallpaper.FileSizeBytes);

        _ = TryCreateThumbnailAsync(wallpaper, cancellationToken);
        WallpaperImported?.Invoke(this, wallpaper);

        var freedNotice = space.FreedBytes > 0
            ? $" Cache cleanup freed {space.FreedBytes / (1024.0 * 1024):N1} MB first."
            : string.Empty;
        return ImportOutcome.Ok(
            wallpaper,
            $"'{wallpaper.Title}' was imported successfully.{freedNotice}");
    }

    /// <summary>
    /// Downloads provider content into the managed cache and registers it in the
    /// library. The download is validated at every step; nothing is executed.
    /// </summary>
    public async Task<DownloadOutcome> DownloadAsync(
        string providerId,
        string sourceId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(providerId) || string.IsNullOrWhiteSpace(sourceId))
        {
            return DownloadOutcome.Fail("A provider and source identifier are required.");
        }

        var provider = _providers.TryGet(providerId);
        if (provider is null)
        {
            return DownloadOutcome.Fail($"Unknown provider '{providerId}'.");
        }

        var details = await provider.GetDetailsAsync(sourceId, cancellationToken).ConfigureAwait(false);
        if (!details.Success || details.Items is null)
        {
            return DownloadOutcome.Fail(
                details.Error ?? $"Provider '{provider.DisplayName}' could not resolve '{sourceId}'.");
        }

        var item = details.Items;

        var existing = await _repository.GetBySourceAsync(providerId, sourceId, cancellationToken).ConfigureAwait(false);
        if (existing is not null && existing.IsCached)
        {
            var existingPath = _cacheManager.ResolveCachePath(existing.CacheRelativePath);
            if (existingPath is not null && File.Exists(existingPath))
            {
                return DownloadOutcome.AlreadyCached(existing);
            }
        }

        if (item.DownloadUrl is null || !Uri.TryCreate(item.DownloadUrl, UriKind.Absolute, out var downloadUrl))
        {
            return DownloadOutcome.Fail($"'{item.Title}' has no usable download URL.");
        }

        var declaredBytes = item.FileSizeBytes > 0
            ? item.FileSizeBytes
            : 0;
        if (declaredBytes > 0)
        {
            var preSpace = await _cacheManager.EnsureSpaceAsync(declaredBytes, ActiveWallpaperId, cancellationToken).ConfigureAwait(false);
            if (!preSpace.EnoughSpace)
            {
                return DownloadOutcome.Fail(preSpace.Message ?? "The cache limit does not leave room for this file.");
            }
        }

        var extension = ExtensionFor(item.Format);
        var download = await _downloadService
            .DownloadAsync(downloadUrl, _environment.TempDirectory, extension, cancellationToken)
            .ConfigureAwait(false);
        if (!download.Success || download.Value is null)
        {
            return DownloadOutcome.Fail(download.Error ?? "The download failed.");
        }

        var staged = download.Value;
        try
        {
            var hashDuplicate = await _repository.GetByHashAsync(staged.FileHash, cancellationToken).ConfigureAwait(false);
            if (hashDuplicate is not null && hashDuplicate.Id != existing?.Id)
            {
                return DownloadOutcome.Fail(
                    $"'{item.Title}' is byte-identical to '{hashDuplicate.Title}', which is already in your library.");
            }

            var postSpace = await _cacheManager.EnsureSpaceAsync(staged.SizeBytes, ActiveWallpaperId, cancellationToken).ConfigureAwait(false);
            if (!postSpace.EnoughSpace)
            {
                return DownloadOutcome.Fail(postSpace.Message ?? "The cache limit does not leave room for this file.");
            }

            var relativePath = BuildCacheRelativePath(item.Kind, staged.FileHash, extension);
            var promoted = await _cacheManager
                .PromoteToCacheAsync(staged.TempPath, relativePath, cancellationToken)
                .ConfigureAwait(false);
            if (!promoted.Success)
            {
                return DownloadOutcome.Fail(promoted.Error ?? "The file could not be stored in the cache.");
            }

            VisualFeatures? features = null;
            var absolute = _cacheManager.ResolveCachePath(relativePath);
            if (item.Kind == WallpaperKind.Static && absolute is not null)
            {
                features = await TryAnalyzeAsync(absolute, cancellationToken).ConfigureAwait(false);
            }

            var wallpaper = existing ?? new Wallpaper
            {
                Title = item.Title,
                SourceProvider = providerId,
                SourceId = sourceId,
                CreatedAt = DateTimeOffset.UtcNow,
                IsUserImported = false,
                SafetyStatus = SafetyStatus.Safe,
            };

            wallpaper.Title = item.Title;
            wallpaper.Description = item.Description;
            wallpaper.SourceUrl = item.PageUrl;
            wallpaper.CacheRelativePath = relativePath;
            wallpaper.FileHash = staged.FileHash;
            wallpaper.Kind = item.Kind;
            wallpaper.Format = item.Format;
            wallpaper.Width = features?.Width ?? item.Width;
            wallpaper.Height = features?.Height ?? item.Height;
            wallpaper.FileSizeBytes = staged.SizeBytes;
            wallpaper.License = item.License;
            wallpaper.Attribution = item.Attribution;
            wallpaper.Categories = item.Categories;
            wallpaper.DominantColor = features?.DominantColorHex ?? item.DominantColor;
            wallpaper.Brightness = features?.Brightness ?? item.Brightness;
            wallpaper.VisualDensity = features?.VisualDensity;
            wallpaper.IsCached = true;
            wallpaper.SafetyStatus = SafetyStatus.Safe;

            if (existing is null)
            {
                wallpaper = await _repository.AddAsync(wallpaper, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await _repository.UpdateAsync(wallpaper, cancellationToken).ConfigureAwait(false);
            }

            _logger.LogInformation(
                "Downloaded wallpaper {Id} '{Title}' from {Provider} ({Bytes:N0} bytes).",
                wallpaper.Id, wallpaper.Title, providerId, wallpaper.FileSizeBytes);

            _ = TryCreateThumbnailAsync(wallpaper, cancellationToken);
            return DownloadOutcome.Ok(wallpaper);
        }
        finally
        {
            // The staged file was moved on success; on failure paths it must not linger.
            TryDeleteSilently(staged.TempPath);
        }
    }

    /// <summary>
    /// Makes sure the wallpaper's payload exists in the cache, re-importing from the
    /// user's original file or re-downloading from its provider when it was evicted.
    /// </summary>
    public async Task<OperationResult> EnsureCachedAsync(Wallpaper wallpaper, CancellationToken cancellationToken = default)
    {
        if (wallpaper.IsCached)
        {
            var cachedPath = _cacheManager.ResolveCachePath(wallpaper.CacheRelativePath);
            if (cachedPath is not null && File.Exists(cachedPath))
            {
                return OperationResult.Ok();
            }
        }

        if (wallpaper.IsUserImported && !string.IsNullOrWhiteSpace(wallpaper.OriginalImportPath)
            && File.Exists(wallpaper.OriginalImportPath))
        {
            var reimport = await ImportFromOriginalAsync(wallpaper, cancellationToken).ConfigureAwait(false);
            if (reimport.Success)
            {
                return OperationResult.Ok();
            }

            _logger.LogWarning(
                "Re-import from original path failed for {Id}: {Error}; trying the provider next.",
                wallpaper.Id, reimport.Error);
        }

        if (!wallpaper.IsUserImported
            && !string.IsNullOrWhiteSpace(wallpaper.SourceProvider)
            && !string.IsNullOrWhiteSpace(wallpaper.SourceId))
        {
            var download = await DownloadAsync(wallpaper.SourceProvider, wallpaper.SourceId, cancellationToken).ConfigureAwait(false);
            if (download.Success && download.Wallpaper is not null)
            {
                return OperationResult.Ok();
            }

            return OperationResult.Fail(
                $"This wallpaper is no longer in the cache and could not be fetched again: {download.Error}");
        }

        return OperationResult.Fail(
            "This wallpaper's file is no longer in the cache and no source is available to fetch it again. " +
            "Remove it from the library or re-import the original file.");
    }

    /// <summary>Applies a wallpaper to the desktop and records the usage.</summary>
    public async Task<ApplyOutcome> ApplyAsync(
        Guid wallpaperId,
        string? displayDeviceName = null,
        CancellationToken cancellationToken = default)
    {
        var wallpaper = await _repository.GetByIdAsync(wallpaperId, cancellationToken).ConfigureAwait(false);
        if (wallpaper is null)
        {
            return ApplyOutcome.Fail($"No wallpaper with id {wallpaperId} exists in the library.");
        }

        if (wallpaper.Kind == WallpaperKind.Video)
        {
            var preferences = await _preferencesStore.LoadAsync(cancellationToken).ConfigureAwait(false);
            if (!preferences.AllowVideoWallpapers)
            {
                return ApplyOutcome.Fail("Video wallpapers are disabled in Settings.");
            }
        }

        var cached = await EnsureCachedAsync(wallpaper, cancellationToken).ConfigureAwait(false);
        if (!cached.Success)
        {
            return ApplyOutcome.Fail(cached.Error ?? "The wallpaper file is not available.");
        }

        // EnsureCachedAsync may have refreshed the row; reload for current paths.
        wallpaper = await _repository.GetByIdAsync(wallpaperId, cancellationToken).ConfigureAwait(false);
        if (wallpaper is null)
        {
            return ApplyOutcome.Fail("The wallpaper disappeared from the library during the apply.");
        }

        var absolutePath = _cacheManager.ResolveCachePath(wallpaper.CacheRelativePath);
        if (absolutePath is null || !File.Exists(absolutePath))
        {
            return ApplyOutcome.Fail("The wallpaper file could not be resolved inside the managed cache.");
        }

        var request = new ApplyRequest(
            wallpaper.Id,
            wallpaper.Kind,
            wallpaper.Title,
            absolutePath,
            WallpaperPlacement.Fill,
            displayDeviceName);

        var applied = await _engine.ApplyAsync(request, cancellationToken).ConfigureAwait(false);
        if (!applied.Success)
        {
            return ApplyOutcome.Fail(applied.Error ?? "The wallpaper engine rejected the apply.");
        }

        await RecordApplyUsageAsync(wallpaperId, cancellationToken).ConfigureAwait(false);

        string? notice = null;
        var applyPreferences = await _preferencesStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (applyPreferences.AutoCleanupEnabled)
        {
            var cleanup = await _cacheManager
                .EnsureSpaceAsync(0, wallpaperId, cancellationToken)
                .ConfigureAwait(false);
            if (cleanup.FreedBytes > 0 && cleanup.Message is not null)
            {
                notice = cleanup.Message;
            }
        }

        _logger.LogInformation("Applied wallpaper {Id} '{Title}'.", wallpaper.Id, wallpaper.Title);
        return ApplyOutcome.Ok(notice);
    }

    /// <summary>Stops playback and closes the open usage record.</summary>
    public async Task<OperationResult> StopAsync(CancellationToken cancellationToken = default)
    {
        Guid? current;
        lock (_gate)
        {
            current = _activeWallpaperId;
            _activeWallpaperId = null;
        }

        if (current is { } id)
        {
            await _usageRepository.CloseOpenUsageAsync(id, DateTimeOffset.UtcNow, cancellationToken).ConfigureAwait(false);
        }

        return await _engine.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task<OperationResult> PauseAsync(CancellationToken cancellationToken = default) =>
        _engine.PauseAsync(cancellationToken);

    public Task<OperationResult> ResumeAsync(CancellationToken cancellationToken = default) =>
        _engine.ResumeAsync(cancellationToken);

    public Task<OperationResult> RestorePreviousAsync(CancellationToken cancellationToken = default) =>
        _engine.RestorePreviousAsync(cancellationToken);

    public Task SetFavoriteAsync(Guid id, bool isFavorite, CancellationToken cancellationToken = default) =>
        _repository.SetFavoriteAsync(id, isFavorite, cancellationToken);

    public Task SetDislikedAsync(Guid id, bool isDisliked, CancellationToken cancellationToken = default) =>
        _repository.SetDislikedAsync(id, isDisliked, cancellationToken);

    public Task SetPinnedAsync(Guid id, bool isPinned, CancellationToken cancellationToken = default) =>
        _repository.SetPinnedAsync(id, isPinned, cancellationToken);

    /// <summary>
    /// Removes a wallpaper from the library and its payload from the cache. The
    /// user's original import file is never touched. The active wallpaper must be
    /// stopped first so the desktop never breaks mid-playback.
    /// </summary>
    public async Task<OperationResult> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (ActiveWallpaperId == id)
        {
            return OperationResult.Fail(
                "This wallpaper is currently active. Stop it or apply another one before deleting.");
        }

        var wallpaper = await _repository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (wallpaper is null)
        {
            return OperationResult.Ok();
        }

        var absolute = _cacheManager.ResolveCachePath(wallpaper.CacheRelativePath);
        if (absolute is not null && File.Exists(absolute))
        {
            var removal = await _cacheManager
                .RemoveFromCacheAsync(wallpaper, ActiveWallpaperId, cancellationToken)
                .ConfigureAwait(false);
            if (!removal.Success)
            {
                return OperationResult.Fail(removal.Error ?? "The cache file could not be removed.");
            }
        }

        await _thumbnailService.InvalidateAsync(id, cancellationToken).ConfigureAwait(false);
        var deleted = await _repository.DeleteAsync(id, cancellationToken).ConfigureAwait(false);
        if (deleted)
        {
            _logger.LogInformation("Deleted wallpaper {Id} '{Title}'.", wallpaper.Id, wallpaper.Title);
            WallpaperDeleted?.Invoke(this, id);
        }

        return OperationResult.Ok();
    }

    /// <summary>Removes only the cached payload, keeping the library entry and metadata.</summary>
    public async Task<OperationResult> RemoveFromCacheAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var wallpaper = await _repository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (wallpaper is null)
        {
            return OperationResult.Fail($"No wallpaper with id {id} exists in the library.");
        }

        var removal = await _cacheManager
            .RemoveFromCacheAsync(wallpaper, ActiveWallpaperId, cancellationToken)
            .ConfigureAwait(false);

        return removal.Success
            ? OperationResult.Ok()
            : OperationResult.Fail(removal.Error ?? "The cache file could not be removed.");
    }

    private async Task RecordApplyUsageAsync(Guid wallpaperId, CancellationToken cancellationToken)
    {
        Guid? previous;
        lock (_gate)
        {
            previous = _activeWallpaperId;
            _activeWallpaperId = wallpaperId;
        }

        if (previous is { } previousId && previousId != wallpaperId)
        {
            await _usageRepository
                .CloseOpenUsageAsync(previousId, DateTimeOffset.UtcNow, cancellationToken)
                .ConfigureAwait(false);
        }

        await _repository.RecordUsageAsync(wallpaperId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Re-copies the original import file into the cache for an existing row.</summary>
    private async Task<OperationResult> ImportFromOriginalAsync(
        Wallpaper wallpaper,
        CancellationToken cancellationToken)
    {
        var validation = await _importValidator
            .ValidateFileAsync(wallpaper.OriginalImportPath!, cancellationToken)
            .ConfigureAwait(false);
        if (!validation.Accepted)
        {
            return OperationResult.Fail($"The original file no longer passes validation: {validation.Detail}");
        }

        var space = await _cacheManager
            .EnsureSpaceAsync(validation.FileSizeBytes, ActiveWallpaperId, cancellationToken)
            .ConfigureAwait(false);
        if (!space.EnoughSpace)
        {
            return OperationResult.Fail(space.Message ?? "The cache limit does not leave room for this file.");
        }

        var relativePath = BuildCacheRelativePath(
            validation.Kind, validation.FileHash, Path.GetExtension(wallpaper.OriginalImportPath!));
        var stagedPath = await StageCopyAsync(wallpaper.OriginalImportPath!, cancellationToken).ConfigureAwait(false);
        if (stagedPath is null)
        {
            return OperationResult.Fail("The original file could not be read.");
        }

        var promoted = await _cacheManager
            .PromoteToCacheAsync(stagedPath, relativePath, cancellationToken)
            .ConfigureAwait(false);
        if (!promoted.Success)
        {
            TryDeleteSilently(stagedPath);
            return OperationResult.Fail(promoted.Error ?? "The file could not be stored in the cache.");
        }

        wallpaper.CacheRelativePath = relativePath;
        wallpaper.FileHash = validation.FileHash;
        wallpaper.FileSizeBytes = validation.FileSizeBytes;
        wallpaper.IsCached = true;
        await _repository.UpdateAsync(wallpaper, cancellationToken).ConfigureAwait(false);
        return OperationResult.Ok();
    }

    private async Task<string?> StageCopyAsync(string sourcePath, CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(_environment.TempDirectory);
            var staged = Path.Combine(_environment.TempDirectory, $"import-{Guid.NewGuid():N}{Path.GetExtension(sourcePath)}");
            using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 81920, useAsync: true);
            await using var target = new FileStream(staged, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                bufferSize: 81920, useAsync: true);
            await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
            return staged;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>Content-addressed cache path: kind/hash-prefix/hash+extension. No untrusted names ever reach the path.</summary>
    private static string BuildCacheRelativePath(WallpaperKind kind, string fileHash, string originalExtension)
    {
        var folder = kind == WallpaperKind.Video ? "videos" : "images";
        var extension = NormalizeExtension(originalExtension);
        var prefix = fileHash.Length >= 2 ? fileHash[..2] : "00";
        return $"{folder}/{prefix}/{fileHash}{extension}";
    }

    private static string NormalizeExtension(string extension)
    {
        var normalized = extension.Trim().ToLowerInvariant();
        if (normalized.Length > 0 && normalized[0] != '.')
        {
            normalized = "." + normalized;
        }

        return normalized is ".jpg" or ".jpeg" or ".png" or ".bmp" or ".gif" or ".webp"
            or ".mp4" or ".webm" or ".mkv" or ".mov" or ".avi"
            ? normalized
            : ".bin";
    }

    private static string ExtensionFor(WallpaperFormat format) => format switch
    {
        WallpaperFormat.Jpeg => ".jpg",
        WallpaperFormat.Png => ".png",
        WallpaperFormat.Bmp => ".bmp",
        WallpaperFormat.Gif => ".gif",
        WallpaperFormat.WebP => ".webp",
        WallpaperFormat.Mp4 => ".mp4",
        WallpaperFormat.WebM => ".webm",
        WallpaperFormat.Matroska => ".mkv",
        WallpaperFormat.QuickTimeMov => ".mov",
        WallpaperFormat.Avi => ".avi",
        _ => ".bin",
    };

    private async Task<VisualFeatures?> TryAnalyzeAsync(string absolutePath, CancellationToken cancellationToken)
    {
        try
        {
            return await _visualAnalyzer.AnalyzeAsync(absolutePath, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Visual analysis failed for {Path}; metadata stays unanalyzed.", absolutePath);
            return null;
        }
    }

    private async Task TryCreateThumbnailAsync(Wallpaper wallpaper, CancellationToken cancellationToken)
    {
        try
        {
            await _thumbnailService.GetOrCreateThumbnailAsync(wallpaper, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Thumbnail generation failed for {Id}.", wallpaper.Id);
        }
    }

    private void TryDeleteSilently(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not remove staged file {File}.", Path.GetFileName(path));
        }
    }

    private static string BuildTitle(string sourcePath)
    {
        var name = Path.GetFileNameWithoutExtension(sourcePath);
        return string.IsNullOrWhiteSpace(name) ? "Imported wallpaper" : name;
    }
}
