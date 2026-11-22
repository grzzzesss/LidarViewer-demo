using System.Numerics;
using LidarViewer.Core;
using LidarViewer.Visualization;
using Xunit;

namespace LidarViewer.Core.Tests;

public sealed class PointColoringTests
{
    [Fact]
    public void SolidModeIsBlueAtAnyPosition()
    {
        var first = PointColoring.GetColor(new(0, 0, 0, 0), PointColorMode.Solid, 0, 1);
        var second = PointColoring.GetColor(new(100, -100, 50, 255), PointColorMode.Solid, -5, 50);
        Assert.Equal(first, second);
        Assert.True(first.Z > first.Y && first.Y > first.X);
    }

    [Fact]
    public void HeightColorsFollowStableRangeIncludingNegativeZ()
    {
        Assert.Equal(new Vector4(0, 0, 1, 1), PointColoring.GetColor(new(0, 0, -2, 0), PointColorMode.Height, -2, 2));
        Assert.Equal(new Vector4(0, 1, 0, 1), PointColoring.GetColor(new(0, 0, 0, 0), PointColorMode.Height, -2, 2));
        Assert.Equal(new Vector4(1, 0, 0, 1), PointColoring.GetColor(new(0, 0, 2, 0), PointColorMode.Height, -2, 2));
        Assert.Equal(new Vector4(0, 1, 0, 1), PointColoring.GetColor(new(0, 0, 3, 0), PointColorMode.Height, 3, 3));
    }

    [Fact]
    public void DistanceKeepsExistingThirtyMeterScale()
    {
        Assert.Equal(new Vector4(0, 1, 0, 1),
            PointColoring.GetColor(new(0, 0, -15, 0), PointColorMode.Distance, 0, 1));
    }

    [Fact]
    public void IntensityDistinguishesMissingFromZero()
    {
        var missing = PointColoring.GetColor(new Point3D(0, 0, 0, 0) { HasIntensity = false }, PointColorMode.Intensity, 0, 1);
        var zero = PointColoring.GetColor(new(0, 0, 0, 0), PointColorMode.Intensity, 0, 1);
        var maximum = PointColoring.GetColor(new(0, 0, 0, 255), PointColorMode.Intensity, 0, 1);
        Assert.True(zero.X < missing.X && missing.X < maximum.X);
    }

    [Fact]
    public void RgbUsesStoredColorAndNeutralFallback()
    {
        Assert.Equal(new Vector4(1, 0, 0, 1),
            PointColoring.GetColor(new Point3D(0, 0, 0, 0) { Rgb = 0xff0000 }, PointColorMode.Rgb, 0, 1));
        Assert.Equal(new Vector4(0.5f, 0.5f, 0.5f, 1),
            PointColoring.GetColor(new(0, 0, 0, 0), PointColorMode.Rgb, 0, 1));
    }
}
