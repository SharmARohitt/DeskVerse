namespace Deskverse.Security;

/// <summary>Central size and format policy for untrusted media. All limits are configurable.</summary>
public sealed class SecurityOptions
{
    /// <summary>Maximum accepted size for a static image import, in bytes.</summary>
    public long MaxImageBytes { get; set; } = 200L * 1024 * 1024;

    /// <summary>Maximum accepted size for a video import, in bytes.</summary>
    public long MaxVideoBytes { get; set; } = 2L * 1024 * 1024 * 1024;

    /// <summary>Maximum accepted size of a single remote download, in bytes.</summary>
    public long MaxDownloadBytes { get; set; } = 2L * 1024 * 1024 * 1024;

    /// <summary>Hard timeout for a remote download.</summary>
    public TimeSpan DownloadTimeout { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>Connect/read timeout used by resilient HTTP clients.</summary>
    public TimeSpan HttpConnectTimeout { get; set; } = TimeSpan.FromSeconds(20);

    /// <summary>Only HTTPS provider URLs are accepted.</summary>
    public bool RequireHttps { get; set; } = true;

    /// <summary>
    /// Explicitly trusted loopback origin (the local DeskVerse API). Remote
    /// wallpaper providers can never use loopback or private addresses.
    /// </summary>
    public bool AllowTrustedLoopback { get; set; }

    public static SecurityOptions Default { get; } = new();
}
