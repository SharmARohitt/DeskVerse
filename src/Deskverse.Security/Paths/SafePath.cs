namespace Deskverse.Security.Paths;

/// <summary>
/// Path canonicalization and containment checks. Every file operation on
/// untrusted names must go through these helpers.
/// </summary>
public static class SafePath
{
    /// <summary>Resolves a path to its canonical absolute form without touching the filesystem.</summary>
    public static string Canonicalize(string path)
    {
        var full = Path.GetFullPath(path);
        return full;
    }

    /// <summary>
    /// True when <paramref name="candidate"/> stays inside <paramref name="root"/> after
    /// canonicalization. Comparison is ordinal and case-insensitive on Windows.
    /// </summary>
    public static bool IsWithinRoot(string root, string candidate)
    {
        if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        string canonicalRoot;
        string canonicalCandidate;
        try
        {
            canonicalRoot = Canonicalize(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            canonicalCandidate = Canonicalize(candidate);
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
        {
            return false;
        }

        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return canonicalCandidate.StartsWith(canonicalRoot, comparison);
    }

    /// <summary>
    /// Combines a root with a relative name and verifies containment in one step.
    /// Returns null when the combination escapes the root.
    /// </summary>
    public static string? SafeCombine(string root, string relativeName)
    {
        if (string.IsNullOrWhiteSpace(relativeName))
        {
            return null;
        }

        if (Path.IsPathRooted(relativeName))
        {
            return null;
        }

        try
        {
            var combined = Path.Combine(root, relativeName);
            var canonical = Canonicalize(combined);
            return IsWithinRoot(root, canonical) ? canonical : null;
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>
    /// Validates an archive entry name before extraction. Rejects rooted paths,
    /// parent traversal, alternate separators that could escape, and device names.
    /// Returns the safe relative path or null.
    /// </summary>
    public static string? ValidateArchiveEntry(string entryName, string destinationRoot)
    {
        if (string.IsNullOrWhiteSpace(entryName))
        {
            return null;
        }

        if (entryName.Contains('\0'))
        {
            return null;
        }

        if (Path.IsPathRooted(entryName) || entryName.StartsWith('/') || entryName.StartsWith('\\'))
        {
            return null;
        }

        // Backslashes are legal in zip entry names on Windows, but normalize them
        // first so the traversal check cannot be bypassed with mixed separators.
        var normalized = entryName.Replace('\\', '/');
        var parts = normalized.Split('/');
        foreach (var part in parts)
        {
            if (part.Length == 0 || part == ".")
            {
                continue;
            }

            if (part == "..")
            {
                return null;
            }

            if (part.Contains(':'))
            {
                return null;
            }

            if (IsWindowsDeviceName(part))
            {
                return null;
            }
        }

        return SafeCombine(destinationRoot, normalized.Replace('/', Path.DirectorySeparatorChar));
    }

    private static bool IsWindowsDeviceName(string part)
    {
        var stem = part.Split('.')[0];
        return stem.Length is 3 or 4 && stem.ToLowerInvariant() is
            "con" or "prn" or "aux" or "nul" or
            "com1" or "com2" or "com3" or "com4" or "com5" or "com6" or "com7" or "com8" or "com9" or
            "lpt1" or "lpt2" or "lpt3" or "lpt4" or "lpt5" or "lpt6" or "lpt7" or "lpt8" or "lpt9";
    }

    /// <summary>
    /// True when any existing component of the path below <paramref name="root"/> is a
    /// reparse point (symlink/junction). Such files must never be followed into or
    /// silently deleted, because they can point outside the managed cache.
    /// </summary>
    public static bool ContainsReparsePoint(string root, string path)
    {
        if (!IsWithinRoot(root, path))
        {
            return false;
        }

        var canonicalRoot = Canonicalize(root).TrimEnd(Path.DirectorySeparatorChar);
        var canonicalPath = Canonicalize(path);

        var current = canonicalRoot;
        var relative = canonicalPath[canonicalRoot.Length..].TrimStart(Path.DirectorySeparatorChar);
        foreach (var segment in relative.Split(Path.DirectorySeparatorChar))
        {
            if (segment.Length == 0)
            {
                continue;
            }

            current = Path.Combine(current, segment);
            try
            {
                var attrs = File.GetAttributes(current);
                if ((attrs & FileAttributes.ReparsePoint) != 0)
                {
                    return true;
                }
            }
            catch (FileNotFoundException)
            {
                return false;
            }
            catch (DirectoryNotFoundException)
            {
                return false;
            }
            catch (IOException)
            {
                // Attribute probe failures are treated as unsafe.
                return true;
            }
        }

        return false;
    }

    /// <summary>Safe delete that refuses to remove reparse points or paths outside a root.</summary>
    public static bool TrySafeDeleteFile(string root, string path)
    {
        if (!IsWithinRoot(root, path))
        {
            return false;
        }

        if (ContainsReparsePoint(root, path))
        {
            return false;
        }

        try
        {
            File.Delete(path);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
