using LidarViewer.Core;
using LidarViewer.Visualization;
using Xunit;

namespace LidarViewer.Core.Tests;

public sealed class DistanceRingsTests
{
    [Theory]
    [InlineData(1, 12, 12)]
    [InlineData(5, 3, 15)]
    [InlineData(10, 2, 20)]
    public void RadiiUseRequestedSpacingAndCoverScene(int spacing, int count, int last)
    {
        var radii = DistanceRings.Radii(12, spacing);
        Assert.Equal(count, radii.Length);
        Assert.Equal(spacing, radii[0]);
        Assert.Equal(last, radii[^1]);
        for (int i = 1; i < radii.Length; i++)
            Assert.Equal(spacing, radii[i] - radii[i - 1]);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(10)]
    public void CircleIsClosedAndCenteredInXy(int radius)
    {
        var points = DistanceRings.Circle(radius);
        Assert.Equal(DistanceRings.SegmentsPerCircle + 1, points.Length);
        Assert.Equal(points[0], points[^1]);
        Assert.All(points, point =>
        {
            Assert.Equal(0, point.Z);
            Assert.InRange(Math.Sqrt(point.X * point.X + point.Y * point.Y), radius - 0.00001, radius + 0.00001);
        });
        Assert.InRange(points.Take(points.Length - 1).Average(point => point.X), -0.00001, 0.00001);
        Assert.InRange(points.Take(points.Length - 1).Average(point => point.Y), -0.00001, 0.00001);
    }

    [Fact]
    public void EmptyCloudKeepsDefaultGrid()
    {
        Assert.Equal(10, DistanceRings.SceneRadius(Array.Empty<Point3D>()));
        Assert.Equal(10, DistanceRings.Radii(DistanceRings.SceneRadius(Array.Empty<Point3D>()), 1).Length);
    }

    [Fact]
    public void RadiusUsesHorizontalDistanceFromLidar()
    {
        Assert.Equal(50, DistanceRings.SceneRadius(new[] { new Point3D(-30, 40, 500, 0) }));
    }

    [Fact]
    public void LargeScenesHaveBoundedNumberOfRings()
    {
        var radii = DistanceRings.Radii(1e12, 1);
        Assert.Equal(DistanceRings.MaximumCount, radii.Length);
        Assert.Equal(100, radii[^1]);
    }

    [Fact]
    public void SpacingChangeProducesNewCircleGeometry()
    {
        var oneMeter = DistanceRings.Radii(20, 1);
        var fiveMeters = DistanceRings.Radii(20, 5);
        Assert.Equal(20, oneMeter.Length);
        Assert.Equal(4, fiveMeters.Length);
        Assert.Equal(1, DistanceRings.Circle(oneMeter[0])[0].X);
        Assert.Equal(5, DistanceRings.Circle(fiveMeters[0])[0].X);
    }
}
