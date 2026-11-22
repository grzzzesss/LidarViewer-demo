using System.Text;
using LidarViewer.Core;
using Xunit;

namespace LidarViewer.Core.Tests;

public sealed class PointCloudFileReaderTests
{
    private const string Ply = "ply\nformat {0} 1.0\nelement vertex 1\nproperty float z\n" +
        "property ushort intensity\nproperty double x\nproperty float y\nproperty uchar red\n" +
        "property uchar green\nproperty uchar blue\nend_header\n";

    private const string Pcd = "VERSION .7\nFIELDS z normal x rgb intensity y\nSIZE 4 4 4 4 2 4\n" +
        "TYPE F F F F U F\nCOUNT 1 3 1 1 1 1\nWIDTH 1\nHEIGHT 1\nPOINTS 1\nDATA {0}\n";

    private static Point3D Expected => new(1.25f, -2, 3, 150) { Rgb = 0x112233 };

    [Fact]
    public void ReadsXyzWithOptionalIntensityAndColor()
    {
        var points = Read(".xyz", "# chmura\n0 0 0\n0 0 1 42\n1.25 -2 3 150 17 34 51\n");
        Assert.Equal(new Point3D(0, 0, 0, 0) { HasIntensity = false }, points[0]);
        Assert.Equal(new Point3D(0, 0, 1, 42), points[1]);
        Assert.Equal(Expected, points[2]);
    }

    [Fact]
    public void ReadsAsciiPlyWithReorderedProperties() =>
        Assert.Equal(Expected, Assert.Single(Read(".ply", string.Format(Ply, "ascii") + "3 150 1.25 -2 17 34 51")));

    [Fact]
    public void ReadsBinaryPlyWithMixedPropertyTypes()
    {
        var points = Read(".ply", string.Format(Ply, "binary_little_endian"), writer =>
        {
            writer.Write(3f);
            writer.Write((ushort)150);
            writer.Write(1.25d);
            writer.Write(-2f);
            writer.Write(new byte[] { 17, 34, 51 });
        });
        Assert.Equal(Expected, Assert.Single(points));
    }

    [Theory]
    [InlineData("1122867")]
    [InlineData("1.5734718027410144e-39")]
    public void ReadsAsciiPcdWithPackedColorAndVectorField(string rgb) =>
        Assert.Equal(Expected, Assert.Single(Read(".pcd", string.Format(Pcd, "ascii") +
            $"3 0 1 0 1.25 {rgb} 150 -2")));

    [Fact]
    public void ReadsBinaryPcdWithPackedColorAndVectorField()
    {
        var points = Read(".pcd", string.Format(Pcd, "binary"), writer =>
        {
            foreach (float value in new[] { 3f, 0f, 1f, 0f, 1.25f }) writer.Write(value);
            writer.Write(0x00112233);
            writer.Write((ushort)150);
            writer.Write(-2f);
        });
        Assert.Equal(Expected, Assert.Single(points));
    }

    [Fact]
    public void SkipsMissingPointsInOrganizedPcd() =>
        Assert.Equal(new Point3D(1.25f, -2, 3, 0) { HasIntensity = false }, Assert.Single(Read(".pcd",
            "FIELDS x y z\nSIZE 4 4 4\nTYPE F F F\nWIDTH 1\nHEIGHT 2\nDATA ascii\nnan nan nan\n1.25 -2 3")));

    [Theory]
    [InlineData(".xyz", "")]
    [InlineData(".ply", "")]
    [InlineData(".pcd", "")]
    [InlineData(".xyz", "1 dwa 3")]
    [InlineData(".ply", "ply\nformat ascii 1.0\nelement vertex 1\nend_header\n")]
    [InlineData(".pcd", "FIELDS x y z\nSIZE 4 4\nTYPE F F F\nPOINTS 1\nDATA ascii\n")]
    public void RejectsEmptyOrMalformedFiles(string extension, string input) =>
        Assert.Throws<InvalidDataException>(() => Read(extension, input));

    [Theory]
    [InlineData(".ply", Ply, "binary_little_endian")]
    [InlineData(".pcd", Pcd, "binary")]
    public void RejectsTruncatedBinaryFile(string extension, string header, string format) =>
        Assert.Throws<InvalidDataException>(() => Read(extension, string.Format(header, format), writer => writer.Write(3f)));

    [Theory]
    [InlineData(".ply", "ply\nformat binary_big_endian 1.0\n")]
    [InlineData(".pcd", "DATA binary_compressed\n")]
    public void RejectsUnsupportedEncodings(string extension, string input) =>
        Assert.Throws<InvalidDataException>(() => Read(extension, input));

    private static IReadOnlyList<Point3D> Read(string extension, string input, Action<BinaryWriter>? append = null)
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + extension);
        try
        {
            using (var writer = new BinaryWriter(File.Create(path)))
            {
                writer.Write(Encoding.UTF8.GetBytes(input));
                append?.Invoke(writer);
            }
            return PointCloudFileReader.Read(path);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
