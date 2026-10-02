namespace Deskverse.Application;

using Deskverse.Core;
using Deskverse.Core.Entities;

/// <summary>Outcome of a local file import attempt.</summary>
public sealed record ImportOutcome
{
    public bool Success { get; init; }

    public Wallpaper? Wallpaper { get; init; }

    public ImportRejectReason Reason { get; init; }

    public string? Detail { get; init; }

    public string? Message { get; init; }

    public static ImportOutcome Ok(Wallpaper wallpaper, string message) => new()
    {
        Success = true,
        Wallpaper = wallpaper,
        Message = message,
    };

    public static ImportOutcome Reject(ImportRejectReason reason, string detail) => new()
    {
        Success = false,
        Reason = reason,
        Detail = detail,
    };
}

/// <summary>Outcome of fetching provider content into the managed cache.</summary>
public sealed record DownloadOutcome
{
    public bool Success { get; init; }

    public Wallpaper? Wallpaper { get; init; }

    public string? Error { get; init; }

    public bool WasAlreadyCached { get; init; }

    public static DownloadOutcome AlreadyCached(Wallpaper wallpaper) => new()
    {
        Success = true,
        Wallpaper = wallpaper,
        WasAlreadyCached = true,
    };

    public static DownloadOutcome Ok(Wallpaper wallpaper) => new() { Success = true, Wallpaper = wallpaper };

    public static DownloadOutcome Fail(string error) => new() { Success = false, Error = error };
}

/// <summary>Outcome of applying a wallpaper to the desktop.</summary>
public sealed record ApplyOutcome
{
    public bool Success { get; init; }

    public string? Error { get; init; }

    /// <summary>Non-fatal notices, e.g. storage cleanup messages.</summary>
    public string? Notice { get; init; }

    public static ApplyOutcome Ok(string? notice = null) => new() { Success = true, Notice = notice };

    public static ApplyOutcome Fail(string error) => new() { Success = false, Error = error };
}
