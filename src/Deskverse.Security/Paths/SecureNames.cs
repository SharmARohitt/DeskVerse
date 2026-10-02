namespace Deskverse.Security.Paths;

using System.Security.Cryptography;

/// <summary>
/// Generates internal storage names. User-supplied filenames are never used as
/// cache filenames, which prevents overwriting unrelated files and filename
/// confusion attacks.
/// </summary>
public static class SecureNames
{
    private const string Alphabet = "abcdefghijklmnopqrstuvwxyz0123456789";

    /// <summary>Random subdirectory name (two levels) to avoid huge flat directories.</summary>
    public static string GenerateRelativePath(string extension)
    {
        var normalized = string.IsNullOrWhiteSpace(extension) ? ".bin" : extension.ToLowerInvariant();
        if (!normalized.StartsWith('.'))
        {
            normalized = "." + normalized;
        }

        var bytes = RandomNumberGenerator.GetBytes(4);
        var level1 = bytes[0].ToString("x2") + bytes[1].ToString("x2");
        var level2 = bytes[2].ToString("x2") + bytes[3].ToString("x2");
        return $"{level1}/{level2}/{Guid.NewGuid():N}{normalized}";
    }

    /// <summary>A random scratch file name for in-flight downloads.</summary>
    public static string GenerateTempName(string extension)
    {
        var normalized = string.IsNullOrWhiteSpace(extension) ? ".bin" : extension.ToLowerInvariant();
        if (!normalized.StartsWith('.'))
        {
            normalized = "." + normalized;
        }

        return $"dv-{Guid.NewGuid():N}.part{normalized}";
    }
}
