namespace Deskverse.Infrastructure;

using Deskverse.Core.Abstractions;

/// <summary>
/// Real application data locations under %LOCALAPPDATA%\DeskVerse. Tests substitute
/// a scratch-directory implementation instead.
/// </summary>
public sealed class WindowsAppEnvironment : IAppEnvironment
{
    public WindowsAppEnvironment()
    {
        DataRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DeskVerse");
        DefaultCacheDirectory = Path.Combine(DataRoot, "Cache");
        DatabasePath = Path.Combine(DataRoot, "deskverse.db");
        LogsDirectory = Path.Combine(DataRoot, "logs");
        ThumbnailsDirectory = Path.Combine(DataRoot, "thumbnails");
        TempDirectory = Path.Combine(DataRoot, "tmp");
    }

    public string DataRoot { get; }

    public string DefaultCacheDirectory { get; }

    public string DatabasePath { get; }

    public string LogsDirectory { get; }

    public string ThumbnailsDirectory { get; }

    public string TempDirectory { get; }

    /// <summary>Creates every well-known directory. Safe to call repeatedly.</summary>
    public void EnsureDirectories()
    {
        Directory.CreateDirectory(DataRoot);
        Directory.CreateDirectory(DefaultCacheDirectory);
        Directory.CreateDirectory(LogsDirectory);
        Directory.CreateDirectory(ThumbnailsDirectory);
        Directory.CreateDirectory(TempDirectory);
    }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
