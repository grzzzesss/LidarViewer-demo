using System.Numerics;
using LidarViewer.Core;

namespace LidarViewer.Visualization;

public static class DistanceRings
{
    public const int MaximumCount = 100;
    public const int SegmentsPerCircle = 128;

    public static double SceneRadius(IReadOnlyList<Point3D> points)
    {
        double radius = 10;
        foreach (var point in points)
            radius = Math.Max(radius, Math.Sqrt((double)point.X * point.X + (double)point.Y * point.Y));
        return radius;
    }

    public static int[] Radii(double sceneRadius, int spacing)
    {
        if (spacing is not (1 or 5 or 10))
            throw new ArgumentOutOfRangeException(nameof(spacing));
        if (!double.IsFinite(sceneRadius) || sceneRadius < 0)
            throw new ArgumentOutOfRangeException(nameof(sceneRadius));

        int count = (int)Math.Min(MaximumCount, Math.Ceiling(sceneRadius / spacing));
        return Enumerable.Range(1, count).Select(index => index * spacing).ToArray();
    }

    public static Vector3[] Circle(int radius)
    {
        var points = new Vector3[SegmentsPerCircle + 1];
        for (int index = 0; index < SegmentsPerCircle; index++)
        {
            double angle = index * 2 * Math.PI / SegmentsPerCircle;
            points[index] = new Vector3((float)(radius * Math.Cos(angle)),
                (float)(radius * Math.Sin(angle)), 0);
        }
        points[^1] = points[0];
        return points;
    }
}
