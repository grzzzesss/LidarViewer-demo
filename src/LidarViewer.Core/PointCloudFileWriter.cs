using System.Globalization;
using System.Text;

namespace LidarViewer.Core;

public static class PointCloudFileWriter
{
    public static void Write(string path, IReadOnlyList<Point3D> points, CancellationToken token = default)
    {
        if (points.Count == 0) throw new InvalidDataException("Brak punktów do zapisania.");
        string extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension is not (".ply" or ".pcd" or ".xyz"))
            throw new InvalidDataException("Wybierz format PLY, PCD lub XYZ.");

        bool intensity = points.All(point => point.HasIntensity);
        if (extension != ".xyz" && !intensity && points.Any(point => point.HasIntensity))
            throw new InvalidDataException("Część punktów nie ma intensywności. Wybierz XYZ, aby zachować brakujące wartości.");

        bool rgb = points.All(point => point.Rgb.HasValue);
        if (extension != ".xyz" && !rgb && points.Any(point => point.Rgb.HasValue))
            throw new InvalidDataException("Część punktów nie ma RGB. Wybierz XYZ, aby zachować kolory bez uzupełniania brakujących wartości.");

        string target = Path.GetFullPath(path);
        string temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var writer = new StreamWriter(temporary, false, new UTF8Encoding(false)))
            {
                WriteHeader(writer, extension, points.Count, rgb, intensity);
                foreach (var point in points)
                {
                    token.ThrowIfCancellationRequested();
                    if (!float.IsFinite(point.X) || !float.IsFinite(point.Y) || !float.IsFinite(point.Z))
                        throw new InvalidDataException("Współrzędne XYZ muszą być skończonymi liczbami.");

                    string row = string.Create(CultureInfo.InvariantCulture,
                        $"{point.X:R} {point.Y:R} {point.Z:R}");
                    if (point.HasIntensity) row += " " + point.Intensity.ToString(CultureInfo.InvariantCulture);
                    if (point.Rgb is uint color)
                    {
                        row += extension == ".pcd"
                            ? " " + (color & 0xffffff).ToString(CultureInfo.InvariantCulture)
                            : string.Create(CultureInfo.InvariantCulture,
                                $" {(color >> 16) & 255} {(color >> 8) & 255} {color & 255}");
                    }
                    writer.WriteLine(row);
                }
            }
            token.ThrowIfCancellationRequested();
            File.Move(temporary, target, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static void WriteHeader(TextWriter writer, string extension, int count, bool rgb, bool intensity)
    {
        if (extension == ".ply")
        {
            writer.WriteLine("ply");
            writer.WriteLine("format ascii 1.0");
            writer.WriteLine("element vertex " + count.ToString(CultureInfo.InvariantCulture));
            writer.WriteLine("property float x");
            writer.WriteLine("property float y");
            writer.WriteLine("property float z");
            if (intensity) writer.WriteLine("property uchar intensity");
            if (rgb)
            {
                writer.WriteLine("property uchar red");
                writer.WriteLine("property uchar green");
                writer.WriteLine("property uchar blue");
            }
            writer.WriteLine("end_header");
        }
        else if (extension == ".pcd")
        {
            writer.WriteLine("VERSION .7");
            writer.WriteLine("FIELDS x y z" + (intensity ? " intensity" : "") + (rgb ? " rgb" : ""));
            writer.WriteLine("SIZE 4 4 4" + (intensity ? " 1" : "") + (rgb ? " 4" : ""));
            writer.WriteLine("TYPE F F F" + (intensity ? " U" : "") + (rgb ? " U" : ""));
            writer.WriteLine("COUNT 1 1 1" + (intensity ? " 1" : "") + (rgb ? " 1" : ""));
            writer.WriteLine("WIDTH " + count.ToString(CultureInfo.InvariantCulture));
            writer.WriteLine("HEIGHT 1");
            writer.WriteLine("POINTS " + count.ToString(CultureInfo.InvariantCulture));
            writer.WriteLine("DATA ascii");
        }
    }
}
