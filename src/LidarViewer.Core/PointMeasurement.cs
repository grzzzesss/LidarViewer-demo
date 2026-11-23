namespace LidarViewer.Core;

public static class PointMeasurement
{
    public static double DistanceFromOrigin(Point3D point) =>
        Math.Sqrt((double)point.X * point.X +
                  (double)point.Y * point.Y +
                  (double)point.Z * point.Z);
    // Ostrożne dopasowanie między klatkami. Nie rozpoznaje tożsamości obiektu.
    public static Point3D? FindTrackedPoint(Point3D previous, IReadOnlyList<Point3D> points)
    {
        double maximumStep = Math.Clamp(0.5 + DistanceFromOrigin(previous) * 0.005, 0.5, 1.0);
        double maximumStepSquared = maximumStep * maximumStep;
        const double depthDifference = 0.3;
        double bestSquared = maximumStepSquared;
        Point3D? nearest = null;
        foreach (var point in points)
        {
            double squared = DistanceSquared(previous, point);
            if (!double.IsFinite(squared) || squared > bestSquared) continue;
            bestSquared = squared;
            nearest = point;
        }
        if (nearest is not { } best) return null;

        // Podobnie bliska, oddzielona powierzchnia oznacza niepewne dopasowanie.
        // Sam odstęp próbek nie oznacza innej powierzchni; sprawdzamy różnicę głębokości.
        foreach (var point in points)
            if (DistanceSquared(previous, point) <= bestSquared * 1.2 * 1.2 &&
                Math.Abs(DistanceFromOrigin(best) - DistanceFromOrigin(point)) > depthDifference)
                return null;
        return best;
    }

    private static double DistanceSquared(Point3D a, Point3D b)
    {
        double dx = (double)a.X - b.X, dy = (double)a.Y - b.Y, dz = (double)a.Z - b.Z;
        return dx * dx + dy * dy + dz * dz;
    }
}
