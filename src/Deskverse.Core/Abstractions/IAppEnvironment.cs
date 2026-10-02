namespace Deskverse.Core.Abstractions;

/// <summary>
/// Well-known application data locations. Abstracted so tests and tools can
/// redirect the entire DeskVerse data footprint to a scratch directory.
/// </summary>
public interface IAppEnvironment
{
    /// <summary>Root folder for all DeskVerse-managed data (database, logs, thumbnails).</summary>
    string DataRoot { get; }

    /// <summary>Default managed cache directory proposed on first run.</summary>
    string DefaultCacheDirectory { get; }

    string DatabasePath { get; }

    string LogsDirectory { get; }

    string ThumbnailsDirectory { get; }

    /// <summary>Scratch space for in-flight downloads and imports. Cleared on startup.</summary>
    string TempDirectory { get; }
}

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
