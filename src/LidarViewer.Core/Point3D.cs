namespace LidarViewer.Core;

// Współrzędne w metrach; intensywność w zakresie 0–255.
public readonly record struct Point3D(float X, float Y, float Z, byte Intensity)
{
    public bool HasIntensity { get; init; } = true;

    // Opcjonalny kolor zapisany jako 0xRRGGBB.
    public uint? Rgb { get; init; }
}
