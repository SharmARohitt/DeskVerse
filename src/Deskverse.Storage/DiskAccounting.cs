namespace Deskverse.Storage;

/// <summary>Cache and disk accounting helpers.</summary>
public static class DiskAccounting
{
    /// <summary>Sum of all file sizes under a directory, without following reparse points.</summary>
    public static long MeasureDirectory(string root)
    {
        if (!Directory.Exists(root))
        {
            return 0;
        }

        long total = 0;
        try
        {
            var info = new DirectoryInfo(root);
            foreach (var file in info.EnumerateFiles("*", SearchOption.AllDirectories))
            {
                if ((file.Attributes & FileAttributes.ReparsePoint) == 0)
                {
                    total += file.Length;
                }
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return total;
    }

    /// <summary>Free bytes on the drive containing the path. Returns null when unavailable.</summary>
    public static long? GetAvailableDiskBytes(string path)
    {
        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(SafeRoot(path)) ?? ".");
            return drive.IsReady ? drive.AvailableFreeSpace : null;
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static string SafeRoot(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
        {
            return ".";
        }
    }
}
