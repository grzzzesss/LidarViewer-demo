using LidarViewer.Core;
using Xunit;

namespace LidarViewer.Core.Tests;

public sealed class PointCloudFilterTests
{
    private static Point3D[] Cloud() =>
    [
        new(0, 0, 0, 0),
        new(0, 0, -2, 20),
        new(-3, 4, 0, 50) { Rgb = 0x123456 },
        new(0, 0, 6, 60)
    ];

    [Fact]
    public void IncludesBoundariesAndPreservesAttributesAndOrder()
    {
        var source = Cloud();
        var original = source.ToArray();
        var visible = PointCloudFilter.ByDistance(source, 2, 5);
        Assert.Equal(new[] { source[1], source[2] }, visible.ToArray());
        Assert.Equal(original, source);
    }

    [Fact]
    public void UsesSpatialDistanceRatherThanXyOnly()
    {
        var point = new Point3D(2, -3, 6, 42);
        Assert.Single(PointCloudFilter.ByDistance(new[] { point }, 7, 7));
        Assert.Empty(PointCloudFilter.ByDistance(new[] { point }, 0, 6));
    }

    [Fact]
    public void IncludesOriginAtZero()
    {
        var visible = PointCloudFilter.ByDistance(Cloud(), 0, 0);
        Assert.Equal(new Point3D(0, 0, 0, 0), Assert.Single(visible));
    }

    [Fact]
    public void EmptyAndNonMatchingCloudsAreSupported()
    {
        Assert.Empty(PointCloudFilter.ByDistance(Array.Empty<Point3D>(), 0, 20));
        Assert.Empty(PointCloudFilter.ByDistance(Cloud(), 100, 200));
    }

    [Theory]
    [InlineData(-1, 20)]
    [InlineData(20, 2)]
    [InlineData(double.NaN, 20)]
    [InlineData(0, double.PositiveInfinity)]
    public void RejectsInvalidRange(double minimum, double maximum) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => PointCloudFilter.ByDistance(Cloud(), minimum, maximum));

    [Fact]
    public void RefilteringOriginalRestoresAllPoints()
    {
        var source = Cloud();
        Assert.Single(PointCloudFilter.ByDistance(source, 0, 0));
        Assert.Equal(4, PointCloudFilter.ByDistance(source, 0, 100).Count);
        Assert.Equal(4, source.Length);
    }

    [Fact]
    public void DoesNotLimitMaximumDistance()
    {
        var point = new Point3D(1_000_000, 0, 0, 0);
        Assert.Single(PointCloudFilter.ByDistance(new[] { point }, 0, 2_000_000));
    }

    [Fact]
    public void FiltersSyntheticCloudUsingSameRule()
    {
        var source = new SyntheticPointSource().Generate();
        var visible = PointCloudFilter.ByDistance(source, 2, 5);
        Assert.NotEmpty(visible);
        Assert.True(visible.Count < source.Count);
        Assert.All(visible, point => Assert.InRange(PointMeasurement.DistanceFromOrigin(point), 2, 5));
    }

    [Theory]
    [InlineData(".ply")]
    [InlineData(".pcd")]
    [InlineData(".xyz")]
    public void ExportContainsOnlyVisiblePoints(string extension)
    {
        var source = Cloud();
        var visible = PointCloudFilter.ByDistance(source, 2, 5);
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + extension);
        // Zachowujemy jednolite RGB dla formatów o wspólnym nagłówku atrybutów.
        var colored = visible.Select(point => point with { Rgb = 0x123456 }).ToArray();
        try
        {
            PointCloudFileWriter.Write(path, colored);
            Assert.Equal(colored, PointCloudFileReader.Read(path).ToArray());
            Assert.Equal(4, source.Length);
        }
        finally { File.Delete(path); }
    }
}
