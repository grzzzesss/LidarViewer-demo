using System.Globalization;
using LidarViewer.Core;
using Xunit;

namespace LidarViewer.Core.Tests;

public sealed class PointCloudFileWriterTests
{
    [Theory]
    [InlineData(".ply", false)]
    [InlineData(".pcd", false)]
    [InlineData(".xyz", false)]
    [InlineData(".ply", true)]
    [InlineData(".pcd", true)]
    [InlineData(".xyz", true)]
    public void RoundTripPreservesPointOrderCoordinatesAndAttributes(string extension, bool rgb)
    {
        var points = new[]
        {
            new Point3D(-1.2345678f, 2.3456789f, -3.456789f, 255),
            new Point3D(0, -0.000001234f, 123456.78f, 0)
        };
        if (rgb)
        {
            points[0] = points[0] with { Rgb = 0x123456 };
            points[1] = points[1] with { Rgb = 0 };
        }
        var original = points.ToArray();
        string path = TemporaryPath(extension);
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pl-PL");
            PointCloudFileWriter.Write(path, points);
            Assert.Equal(original, PointCloudFileReader.Read(path).ToArray());
            Assert.Equal(original, points);
            if (extension == ".ply") Assert.Contains("format ascii 1.0", File.ReadAllText(path));
            if (extension == ".pcd") Assert.Contains("DATA ascii", File.ReadAllText(path));
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(".ply")]
    [InlineData(".pcd")]
    [InlineData(".xyz")]
    public void EmptyCloudDoesNotOverwriteExistingFile(string extension)
    {
        string path = TemporaryPath(extension);
        try
        {
            File.WriteAllText(path, "previous content");
            Assert.Throws<InvalidDataException>(() => PointCloudFileWriter.Write(path, Array.Empty<Point3D>()));
            Assert.Equal("previous content", File.ReadAllText(path));
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedOrCancelledWritePreservesExistingFile(bool cancel)
    {
        string path = TemporaryPath(".ply");
        using var cancellation = new CancellationTokenSource();
        if (cancel) cancellation.Cancel();
        try
        {
            File.WriteAllText(path, "previous content");
            var points = new[] { new Point3D(cancel ? 1 : float.NaN, 2, 3, 4) };
            if (cancel)
                Assert.Throws<OperationCanceledException>(() => PointCloudFileWriter.Write(path, points, cancellation.Token));
            else
                Assert.Throws<InvalidDataException>(() => PointCloudFileWriter.Write(path, points));
            Assert.Equal("previous content", File.ReadAllText(path));
            Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, Path.GetFileName(path) + ".*.tmp"));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void XyzPreservesPartiallyAvailableRgb()
    {
        string path = TemporaryPath(".xyz");
        var points = new[]
        {
            new Point3D(1, 2, 3, 4),
            new Point3D(5, 6, 7, 8) { Rgb = 0x123456 }
        };
        try
        {
            PointCloudFileWriter.Write(path, points);
            Assert.Equal(points, PointCloudFileReader.Read(path).ToArray());
        }
        finally { File.Delete(path); }
    }

    private static string TemporaryPath(string extension) =>
        Path.Combine(Path.GetTempPath(), Guid.NewGuid() + extension);
}
