using System.Numerics;
using LidarViewer.Core;

namespace LidarViewer.Visualization;

public enum PointColorMode { Distance, Height, Intensity, Rgb, Solid }

public static class PointColoring
{
    public static Vector4 GetColor(Point3D point, PointColorMode mode, double minimumZ, double maximumZ)
    {
        switch (mode)
        {
            case PointColorMode.Solid:
                return new Vector4(0.15f, 0.55f, 1, 1);
            case PointColorMode.Rgb:
                if (point.Rgb is not uint rgb) return new Vector4(0.5f, 0.5f, 0.5f, 1);
                return new Vector4(((rgb >> 16) & 255) / 255f,
                    ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, 1);
            case PointColorMode.Intensity:
                float brightness = point.HasIntensity ? 0.1f + 0.9f * point.Intensity / 255 : 0.5f;
                return new Vector4(brightness, brightness, brightness, 1);
            case PointColorMode.Height:
                return Gradient(maximumZ > minimumZ ? (point.Z - minimumZ) / (maximumZ - minimumZ) : 0.5);
            default:
                return Gradient(PointMeasurement.DistanceFromOrigin(point) / 30);
        }
    }

    private static Vector4 Gradient(double value)
    {
        float ratio = (float)Math.Clamp(value, 0, 1);
        return new Vector4(Math.Max(2 * ratio - 1, 0),
            1 - Math.Abs(2 * ratio - 1), Math.Max(1 - 2 * ratio, 0), 1);
    }
}
