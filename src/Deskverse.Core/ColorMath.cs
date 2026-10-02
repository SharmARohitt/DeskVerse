namespace Deskverse.Core;

using System.Globalization;

/// <summary>Pure color and pixel math shared by analysis and recommendation scoring.</summary>
public static class ColorMath
{
    /// <summary>Dominant color over a sampled pixel set using a coarse 4-bit-per-channel histogram.</summary>
    public static string DominantColorHex(ReadOnlySpan<byte> bgra, int pixelCount)
    {
        if (pixelCount <= 0)
        {
            return "000000";
        }

        var histogram = new int[16 * 16 * 16];
        for (var i = 0; i < pixelCount; i++)
        {
            var blue = bgra[i * 4] >> 4;
            var green = bgra[i * 4 + 1] >> 4;
            var red = bgra[i * 4 + 2] >> 4;
            histogram[(red << 8) | (green << 4) | blue]++;
        }

        var bestBucket = 0;
        var bestCount = -1;
        for (var i = 0; i < histogram.Length; i++)
        {
            if (histogram[i] > bestCount)
            {
                bestCount = histogram[i];
                bestBucket = i;
            }
        }

        var r4 = bestBucket >> 8;
        var g4 = (bestBucket >> 4) & 0xF;
        var b4 = bestBucket & 0xF;
        var r = (r4 << 4) | r4;
        var g = (g4 << 4) | g4;
        var b = (b4 << 4) | b4;
        return $"{r:X2}{g:X2}{b:X2}";
    }

    /// <summary>Perceived average brightness in [0,1] using Rec. 601 luma.</summary>
    public static double Brightness(ReadOnlySpan<byte> bgra, int pixelCount)
    {
        if (pixelCount <= 0)
        {
            return 0;
        }

        double total = 0;
        for (var i = 0; i < pixelCount; i++)
        {
            total += 0.299 * bgra[i * 4 + 2] + 0.587 * bgra[i * 4 + 1] + 0.114 * bgra[i * 4];
        }

        return Math.Clamp(total / pixelCount / 255.0, 0, 1);
    }

    /// <summary>
    /// Visual density in [0,1]: mean gradient energy of a downsampled grid. Flat
    /// gradients score low; busy textures and hard edges score high.
    /// </summary>
    public static double VisualDensity(ReadOnlySpan<byte> bgra, int width, int height)
    {
        if (width < 2 || height < 2)
        {
            return 0;
        }

        static double Luma(ReadOnlySpan<byte> s, int i) => 0.299 * s[i + 2] + 0.587 * s[i + 1] + 0.114 * s[i];

        double energy = 0;
        var samples = 0;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width - 1; x++)
            {
                var i = (y * width + x) * 4;
                energy += Math.Abs(Luma(bgra, i + 4) - Luma(bgra, i));
                samples++;
            }
        }

        for (var y = 0; y < height - 1; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var i = (y * width + x) * 4;
                energy += Math.Abs(Luma(bgra, i + width * 4) - Luma(bgra, i));
                samples++;
            }
        }

        if (samples == 0)
        {
            return 0;
        }

        // 255 is the maximum per-sample difference; 32 approximates a comfortably busy image.
        return Math.Clamp(energy / samples / 32.0, 0, 1);
    }

    /// <summary>Distance between two hex colors in [0,1].</summary>
    public static double ColorDistance(string hexA, string hexB)
    {
        if (!TryParseHex(hexA, out var a) || !TryParseHex(hexB, out var b))
        {
            return 1;
        }

        var dr = (a >> 16) - (b >> 16);
        var dg = ((a >> 8) & 0xFF) - ((b >> 8) & 0xFF);
        var db = (a & 0xFF) - (b & 0xFF);
        var distance = Math.Sqrt(dr * dr + dg * dg + db * db);
        return Math.Clamp(distance / 441.67, 0, 1); // sqrt(3 * 255^2)
    }

    public static bool TryParseHex(string hex, out int rgb)
    {
        rgb = 0;
        if (string.IsNullOrWhiteSpace(hex))
        {
            return false;
        }

        var normalized = hex.TrimStart('#');
        return normalized.Length == 6
            && int.TryParse(normalized, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out rgb);
    }

    /// <summary>A short human label for a color, used in explanations.</summary>
    public static string DescribeColor(string hex)
    {
        if (!TryParseHex(hex, out var rgb))
        {
            return "unknown color";
        }

        var r = (rgb >> 16) & 0xFF;
        var g = (rgb >> 8) & 0xFF;
        var b = rgb & 0xFF;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var lightness = (max + min) / 510.0;

        string tone = lightness switch
        {
            < 0.2 => "very dark",
            < 0.4 => "dark",
            > 0.85 => "very bright",
            > 0.65 => "bright",
            _ => "mid-tone",
        };

        string hue;
        if (max - min < 24)
        {
            hue = "gray";
        }
        else if (r >= g && r >= b)
        {
            hue = g > b * 1.4 ? "orange" : b > g * 1.4 ? "magenta" : "red";
        }
        else if (g >= r && g >= b)
        {
            hue = r > b * 1.4 ? "green-yellow" : "green";
        }
        else
        {
            hue = r > g * 1.4 ? "violet-blue" : "blue";
        }

        return hue == "gray" ? $"{tone} gray" : $"{tone} {hue}";
    }
}
