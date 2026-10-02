namespace Deskverse.Core.Models;

/// <summary>Snapshot of one connected display.</summary>
public sealed record DisplayInfo(
    string DeviceName,
    int X,
    int Y,
    int Width,
    int Height,
    bool IsPrimary,
    double DpiScale)
{
    public double AspectRatio => Height > 0 ? (double)Width / Height : 0;

    public bool MatchesResolution(int width, int height) => Width == width && Height == height;
}
