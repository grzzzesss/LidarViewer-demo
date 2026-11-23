using System.Numerics;

namespace LidarViewer.Core;

public sealed class PointTracker
{
    private Vector3[] template = [];
    private Vector3 velocity;
    public Point3D? Position { get; private set; }
    public bool IsLost { get; private set; }

    public void Select(Point3D point, IReadOnlyList<Point3D>? cloud = null)
    {
        Position = point;
        IsLost = false;
        velocity = Vector3.Zero;
        var origin = Vector(point);
        // Mały fragment powierzchni, zamiast jednej nietrwałej próbki lasera.
        template = (cloud ?? new[] { point }).Select(Vector)
            .Where(p => Vector3.DistanceSquared(p, origin) <= .6f * .6f)
            .OrderBy(p => Vector3.DistanceSquared(p, origin))
            .Take(24).Select(p => p - origin).ToArray();
    }

    public void Clear()
    {
        Position = null;
        IsLost = false;
        template = [];
        velocity = Vector3.Zero;
    }

    public void Update(IReadOnlyList<Point3D> points)
    {
        if (Position is not { } previous || IsLost) return;
        var origin = Vector(previous);
        var predicted = origin + velocity;
        var nearby = points.Where(p => Vector3.DistanceSquared(Vector(p), predicted) <= 2 * 2)
            .OrderBy(p => Vector3.DistanceSquared(Vector(p), predicted)).Take(512).ToArray();
        if (nearby.Length == 0) { Lose(); return; }

        // Dla zbyt rzadkiej chmury nie ma kształtu do porównania.
        if (template.Length < 6)
        {
            var point = PointMeasurement.FindTrackedPoint(previous, nearby);
            if (point is { } matched) Accept(matched, origin);
            else Lose();
            return;
        }

        Vector3? best = null;
        double bestScore = double.PositiveInfinity;
        // Kilka pozycji początkowych pomaga wyjść poza stare miejsce obiektu.
        var starts = nearby.OrderBy(p => Vector3.DistanceSquared(Vector(p), predicted))
            .Take(80).Where((_, i) => i % 10 == 0).Select(Vector)
            .Prepend(origin).Prepend(predicted);
        foreach (var start in starts)
        {
            var anchor = start;
            for (int iteration = 0; iteration < 5; iteration++)
            {
                Vector3 correction = Vector3.Zero;
                int matches = 0;
                foreach (var offset in template)
                {
                    if (Nearest(anchor + offset, nearby, .3f * .3f) is not { } match) continue;
                    correction += Vector(match) - (anchor + offset);
                    matches++;
                }
                if (matches < Math.Max(6, template.Length * 2 / 3)) break;
                correction /= matches;
                anchor += correction;
                if (correction.LengthSquared() < .000001f) break;
            }
            if (Vector3.DistanceSquared(anchor, origin) > 1.5f * 1.5f) continue;
            double error = 0;
            int supported = 0;
            foreach (var offset in template)
            {
                if (Nearest(anchor + offset, nearby, .2f * .2f) is { } match)
                {
                    error += Vector3.DistanceSquared(anchor + offset, Vector(match));
                    supported++;
                }
                else error += .04;
            }
            if (supported < Math.Max(6, template.Length * 3 / 4)) continue;
            double score = error / template.Length + .001 * Vector3.DistanceSquared(anchor, predicted);
            if (score >= bestScore) continue;
            bestScore = score;
            best = anchor;
        }
        // Wynik jest zawsze rzeczywistym punktem bieżącej klatki, nie prognozą.
        if (best is { } position && bestScore <= .015 &&
            Nearest(position, nearby, .25f * .25f) is { } selected)
            Accept(selected, origin);
        else Lose();
    }

    private void Accept(Point3D point, Vector3 previous)
    {
        velocity = Vector(point) - previous;
        Position = point;
    }

    private void Lose()
    {
        Position = null;
        IsLost = true;
    }

    private static Point3D? Nearest(Vector3 target, IReadOnlyList<Point3D> points, float maximumSquared)
    {
        Point3D? nearest = null;
        foreach (var point in points)
        {
            float distance = Vector3.DistanceSquared(target, Vector(point));
            if (!float.IsFinite(distance) || distance > maximumSquared) continue;
            maximumSquared = distance;
            nearest = point;
        }
        return nearest;
    }

    private static Vector3 Vector(Point3D point) => new(point.X, point.Y, point.Z);
}