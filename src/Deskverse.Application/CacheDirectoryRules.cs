namespace Deskverse.Application;

using Deskverse.Core.Abstractions;

/// <summary>
/// Validation rules for user-chosen cache directories. Guards against system
/// locations and drive roots where bulk cache operations would be dangerous.
/// </summary>
public static class CacheDirectoryRules
{
    /// <summary>Returns an error message, or null when the directory is acceptable.</summary>
    public static string? Validate(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "A cache folder must be specified.";
        }

        string full;
        try
        {
            full = Path.GetFullPath(path.Trim());
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
        {
            return $"The path is not valid: {ex.Message}";
        }

        var root = Path.GetPathRoot(full) ?? string.Empty;
        if (string.Equals(full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase))
        {
            return "The cache folder cannot be the root of a drive; choose a subfolder.";
        }

        foreach (var forbidden in GetForbiddenPrefixes())
        {
            if (full.StartsWith(forbidden, StringComparison.OrdinalIgnoreCase))
            {
                return $"The cache folder cannot live inside '{forbidden}'. Choose a user or data location.";
            }
        }

        if (!string.Equals(full, Path.GetFullPath(full), StringComparison.Ordinal))
        {
            return "The cache folder path could not be normalized.";
        }

        return null;
    }

    /// <summary>Creates the directory (when missing) and confirms it is writable.</summary>
    public static string? TryPrepare(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            var probe = Path.Combine(path, $".deskverse-write-test-{Guid.NewGuid():N}");
            File.WriteAllText(probe, "probe");
            File.Delete(probe);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"The folder exists but DeskVerse cannot write to it: {ex.Message}";
        }
    }

    private static IEnumerable<string> GetForbiddenPrefixes()
    {
        yield return Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.System);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
    }
}
