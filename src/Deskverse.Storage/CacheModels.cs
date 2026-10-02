namespace Deskverse.Storage;

/// <summary>
/// Live cache policy snapshot. Implemented by the application layer on top of
/// persisted user preferences; the cache manager itself never reads settings.
/// </summary>
public interface ICachePolicy
{
    string CacheRoot { get; }

    long LimitBytes { get; }

    bool AutoCleanup { get; }
}

/// <summary>Outcome of an LRU cleanup run.</summary>
public sealed record CacheCleanupResult
{
    public bool EnoughSpace { get; init; }

    public long FreedBytes { get; init; }

    public long ProjectedUsedBytes { get; init; }

    public IReadOnlyList<string> RemovedTitles { get; init; } = [];

    public string? Message { get; init; }

    public static CacheCleanupResult Insufficient(long projected, long limit, string message) => new()
    {
        EnoughSpace = false,
        ProjectedUsedBytes = projected,
        Message = message,
    };
}

public sealed record CacheRemovalResult
{
    public bool Success { get; init; }

    public long FreedBytes { get; init; }

    public string? Error { get; init; }
}
