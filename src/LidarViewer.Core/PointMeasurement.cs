namespace LidarViewer.Core;

public static class PointMeasurement
{
    public static double DistanceFromOrigin(Point3D point) =>
        Math.Sqrt((double)point.X * point.X +
                  (double)point.Y * point.Y +
                  (double)point.Z * point.Z);
}
