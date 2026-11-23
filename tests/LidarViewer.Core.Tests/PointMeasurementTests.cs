using LidarViewer.Core;
using Xunit;

namespace LidarViewer.Core.Tests;

public sealed class PointMeasurementTests
{
    [Theory]
    [InlineData(0, 0, 0, 0)]
    [InlineData(3, 4, 0, 5)]
    [InlineData(-3, -4, 0, 5)]
    [InlineData(2, -3, 6, 7)]
    [InlineData(0, 0, -10, 10)]
    [InlineData(0.3f, -0.4f, 0, 0.5)]
    public void ComputesSpatialDistance(float x, float y, float z, double expected)
    {
        var point = new Point3D(x, y, z, 0);
        Assert.Equal(expected, PointMeasurement.DistanceFromOrigin(point), 6);
    }

    [Fact]
    public void LargeFiniteCoordinatesDoNotOverflowFloat()
    {
        double distance = PointMeasurement.DistanceFromOrigin(new Point3D(float.MaxValue, 0, 0, 0));
        Assert.True(double.IsFinite(distance));
        Assert.Equal((double)float.MaxValue, distance);
    }
    [Fact]
    public void TrackingFollowsSmallMotionAndPreservesPointAttributes()
    {
        var previous = new Point3D(1, 2, 3, 10);
        var moved = new Point3D(1.1f, 2, 3, 90) { Rgb = 0x123456 };
        Assert.Equal(moved, PointMeasurement.FindTrackedPoint(previous, new[] { new Point3D(4, 2, 3, 0), moved }));
    }

    [Fact]
    public void TrackingRejectsLargeJumpEmptyCloudAndInvalidCoordinates()
    {
        var previous = new Point3D(0, 0, 0, 0);
        Assert.Null(PointMeasurement.FindTrackedPoint(previous, new[] { new Point3D(.6f, 0, 0, 0) }));
        Assert.Null(PointMeasurement.FindTrackedPoint(previous, Array.Empty<Point3D>()));
        Assert.Null(PointMeasurement.FindTrackedPoint(previous, new[] { new Point3D(float.NaN, 0, 0, 0) }));
        Assert.NotNull(PointMeasurement.FindTrackedPoint(previous, new[] { new Point3D(.5f, 0, 0, 0) }));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TrackingRejectsAmbiguousSurfacesRegardlessOfOrder(bool reverse)
    {
        var candidates = new[] { new Point3D(2.2f, 0, 0, 0), new Point3D(1.79f, 0, 0, 0) };
        if (reverse) Array.Reverse(candidates);
        Assert.Null(PointMeasurement.FindTrackedPoint(new(2, 0, 0, 0), candidates));
    }

    [Fact]
    public void DenseNeighboursDoNotCauseFalseLossAndExactPointRemainsSelected()
    {
        var previous = new Point3D(0, 0, 0, 0);
        var nearest = new Point3D(.2f, 0, 0, 20);
        Assert.Equal(nearest, PointMeasurement.FindTrackedPoint(previous,
            new[] { nearest, new Point3D(.21f, .01f, 0, 30), new Point3D(.4f, 0, 0, 40) }));
        Assert.Equal(previous, PointMeasurement.FindTrackedPoint(previous, new[] { nearest, previous }));
    }
}
