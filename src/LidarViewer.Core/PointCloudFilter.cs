namespace LidarViewer.Core;

public sealed record PointCloudFilterSettings
{
    public bool DistanceEnabled { get; init; }
    public double MinimumDistance { get; init; }
    public double MaximumDistance { get; init; } = 20;
    public bool HeightEnabled { get; init; }
    public double MinimumHeight { get; init; } = -1;
    public double MaximumHeight { get; init; } = 3;
    public bool IntensityEnabled { get; init; }
    public double MinimumIntensity { get; init; }
    public double MaximumIntensity { get; init; } = 255;
}

public static class PointCloudFilter
{
    public static IReadOnlyList<Point3D> ByDistance(IReadOnlyList<Point3D> points,
        double minimum, double maximum, CancellationToken token = default) =>
        Apply(points, new PointCloudFilterSettings
        {
            DistanceEnabled = true, MinimumDistance = minimum, MaximumDistance = maximum
        }, token);

    public static IReadOnlyList<Point3D> Apply(IReadOnlyList<Point3D> points,
        PointCloudFilterSettings settings, CancellationToken token = default)
    {
        Validate(settings);
        if (!settings.DistanceEnabled && !settings.HeightEnabled && !settings.IntensityEnabled)
            return points;

        var visible = new List<Point3D>();
        foreach (var point in points)
        {
            token.ThrowIfCancellationRequested();
            if (Matches(point, settings)) visible.Add(point);
        }
        return visible.AsReadOnly();
    }

    public static void Validate(PointCloudFilterSettings settings)
    {
        if (settings.DistanceEnabled &&
            (!ValidRange(settings.MinimumDistance, settings.MaximumDistance) || settings.MinimumDistance < 0))
            throw new ArgumentOutOfRangeException(nameof(settings), "Odległość: 0 ≤ minimum ≤ maksimum.");
        if (settings.HeightEnabled && !ValidRange(settings.MinimumHeight, settings.MaximumHeight))
            throw new ArgumentOutOfRangeException(nameof(settings), "Wysokość: minimum ≤ maksimum.");
        if (settings.IntensityEnabled &&
            (!ValidRange(settings.MinimumIntensity, settings.MaximumIntensity) ||
             settings.MinimumIntensity < 0 || settings.MaximumIntensity > 255))
            throw new ArgumentOutOfRangeException(nameof(settings), "Intensywność: 0 ≤ minimum ≤ maksimum ≤ 255.");
    }

    public static bool Matches(Point3D point, PointCloudFilterSettings settings)
    {
        if (settings.DistanceEnabled)
        {
            double distance = PointMeasurement.DistanceFromOrigin(point);
            if (distance < settings.MinimumDistance || distance > settings.MaximumDistance) return false;
        }
        if (settings.HeightEnabled && (point.Z < settings.MinimumHeight || point.Z > settings.MaximumHeight))
            return false;
        if (settings.IntensityEnabled && (!point.HasIntensity ||
            point.Intensity < settings.MinimumIntensity || point.Intensity > settings.MaximumIntensity))
            return false;
        return true;
    }

    private static bool ValidRange(double minimum, double maximum) =>
        double.IsFinite(minimum) && double.IsFinite(maximum) && minimum <= maximum;
}
