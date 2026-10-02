namespace Deskverse.Core.Models;

/// <summary>System health snapshot reported to the UI and local API.</summary>
public sealed record SystemHealth(
    bool DatabaseConnected,
    bool CacheAvailable,
    bool ApiListening,
    int? ApiPort,
    Guid? ActiveWallpaperId,
    EngineState EngineState,
    TimeSpan Uptime,
    int DisplayCount,
    IReadOnlyList<string> Warnings);
