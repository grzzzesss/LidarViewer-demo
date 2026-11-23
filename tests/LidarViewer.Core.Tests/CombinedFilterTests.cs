using LidarViewer.Core;
using Xunit;

namespace LidarViewer.Core.Tests;

public sealed class CombinedFilterTests
{
    private static Point3D[] Cloud() =>
    [
        new(0, 0, -2, 0),
        new(0, 0, -1, 50),
        new(3, 0, 0, 100) { Rgb = 0x123456 },
        new(0, 0, 3, 200),
        new(30, 0, 4, 255),
        new(3, 0, 0, 0) { HasIntensity = false }
    ];

    [Fact]
    public void HeightIncludesNegativeAndBoundaryValues()
    {
        var points = Cloud();
        var result = PointCloudFilter.Apply(points, new() { HeightEnabled = true, MinimumHeight = -1, MaximumHeight = 3 });
        Assert.Equal(new[] { points[1], points[2], points[3], points[5] }, result.ToArray());
    }

    [Fact]
    public void IntensityIncludesZeroAndExcludesMissingValues()
    {
        var result = PointCloudFilter.Apply(Cloud(), new() { IntensityEnabled = true, MaximumIntensity = 0 });
        Assert.Equal(new Point3D(0, 0, -2, 0), Assert.Single(result));
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    public void CombinesActiveFiltersWithoutChangingSource(bool distance, bool height, bool intensity)
    {
        var points = Cloud();
        var before = points.ToArray();
        var settings = new PointCloudFilterSettings
        {
            DistanceEnabled = distance, MinimumDistance = 2, MaximumDistance = 20,
            HeightEnabled = height, MinimumHeight = 0, MaximumHeight = 3,
            IntensityEnabled = intensity, MinimumIntensity = 50, MaximumIntensity = 200
        };
        var expected = points.Where(point =>
            (!distance || (PointMeasurement.DistanceFromOrigin(point) >= 2 && PointMeasurement.DistanceFromOrigin(point) <= 20)) &&
            (!height || (point.Z >= 0 && point.Z <= 3)) &&
            (!intensity || (point.HasIntensity && point.Intensity >= 50 && point.Intensity <= 200))).ToArray();
        Assert.Equal(expected, PointCloudFilter.Apply(points, settings).ToArray());
        Assert.Equal(before, points);
        Assert.Same(points, PointCloudFilter.Apply(points, new()));
        Assert.Equal(expected, PointCloudFilter.Apply(points, settings).ToArray());
    }

    [Fact]
    public void EmptyCloudAndAllMissingIntensitiesProduceNoMatches()
    {
        var settings = new PointCloudFilterSettings { HeightEnabled = true, IntensityEnabled = true };
        Assert.Empty(PointCloudFilter.Apply(Array.Empty<Point3D>(), settings));
        Assert.Empty(PointCloudFilter.Apply(new[] { new Point3D(0, 0, 0, 0) { HasIntensity = false } }, settings));
    }

    [Theory]
    [InlineData(5, -5)]
    [InlineData(double.NaN, 3)]
    public void RejectsInvalidHeight(double minimum, double maximum) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => PointCloudFilter.Apply(Cloud(),
            new() { HeightEnabled = true, MinimumHeight = minimum, MaximumHeight = maximum }));

    [Theory]
    [InlineData(-1, 100)]
    [InlineData(0, 256)]
    [InlineData(200, 50)]
    public void RejectsInvalidIntensity(double minimum, double maximum) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => PointCloudFilter.Apply(Cloud(),
            new() { IntensityEnabled = true, MinimumIntensity = minimum, MaximumIntensity = maximum }));

    [Theory]
    [InlineData(".xyz")]
    [InlineData(".ply")]
    [InlineData(".pcd")]
    public void ImportAndExportPreserveMissingIntensity(string extension)
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + extension);
        var points = new[] { new Point3D(-1, 2, 3, 0) { HasIntensity = false, Rgb = 0x123456 } };
        try
        {
            PointCloudFileWriter.Write(path, points);
            var imported = PointCloudFileReader.Read(path);
            Assert.Equal(points, imported.ToArray());
            Assert.Empty(PointCloudFilter.Apply(imported, new() { IntensityEnabled = true }));
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(".xyz")]
    [InlineData(".ply")]
    [InlineData(".pcd")]
    public void ExportPreservesCombinedFilterResult(string extension)
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + extension);
        var settings = new PointCloudFilterSettings
        {
            DistanceEnabled = true, MinimumDistance = 2, MaximumDistance = 5,
            HeightEnabled = true, MinimumHeight = 0, MaximumHeight = 3,
            IntensityEnabled = true, MinimumIntensity = 100, MaximumIntensity = 100
        };
        var visible = PointCloudFilter.Apply(Cloud(), settings);
        try
        {
            PointCloudFileWriter.Write(path, visible);
            Assert.Equal(visible.ToArray(), PointCloudFileReader.Read(path).ToArray());
            Assert.Single(visible);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void SyntheticCloudCanBeFilteredByHeightAndIntensity()
    {
        var source = new SyntheticPointSource().Generate();
        var visible = PointCloudFilter.Apply(source, new()
        {
            HeightEnabled = true, MinimumHeight = 3.1, MaximumHeight = 5,
            IntensityEnabled = true, MinimumIntensity = 220, MaximumIntensity = 220
        });
        Assert.NotEmpty(visible);
        Assert.All(visible, point => { Assert.InRange(point.Z, 3.1f, 5); Assert.Equal(220, point.Intensity); });
    }
    [Fact]
    public void PassingCloudIsReusedAndLateRejectionPreservesPrefixAndOrder()
    {
        var points = new Point3D[] { new(1, 0, 0, 80), new(2, 0, 0, 90), new(3, 0, 0, 100) };
        var settings = new PointCloudFilterSettings { DistanceEnabled = true, MaximumDistance = 5 };
        Assert.Same(points, PointCloudFilter.Apply(points, settings));
        var filtered = PointCloudFilter.Apply(points, settings with { MaximumDistance = 2 });
        Assert.Equal(points.Take(2), filtered);
        Assert.Equal(3, points.Length);
        Assert.Equal(new[] { points[1] }, PointCloudFilter.Apply(points,
            settings with { MinimumDistance = 2, MaximumDistance = 2 }));
    }
}
