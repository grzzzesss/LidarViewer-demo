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
}
