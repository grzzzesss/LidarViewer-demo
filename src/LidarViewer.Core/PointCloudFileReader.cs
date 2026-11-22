using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace LidarViewer.Core;

public static class PointCloudFileReader
{
    private enum Scalar { I8, U8, I16, U16, I32, U32, I64, U64, F32, F64 }
    private sealed record Field(string Name, Scalar Type, int Count = 1, Scalar? ListCount = null);
    private sealed record Element(string Name, int Count, List<Field> Fields);

    public static IReadOnlyList<Point3D> Read(string path, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        using var stream = File.OpenRead(path);
        if (stream.Length == 0) throw new InvalidDataException("Plik jest pusty.");

        try
        {
            var points = Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".xyz" => ReadXyz(stream, token),
                ".ply" => ReadPly(stream, token),
                ".pcd" => ReadPcd(stream, token),
                _ => throw new InvalidDataException("Wybierz plik XYZ, PLY lub PCD.")
            };
            if (points.Count == 0) throw new InvalidDataException("Plik nie zawiera poprawnych punktów.");
            return points.AsReadOnly();
        }
        catch (EndOfStreamException ex)
        {
            throw new InvalidDataException("Plik jest niekompletny. Brakuje nagłówka lub danych punktów.", ex);
        }
        catch (OverflowException ex)
        {
            throw new InvalidDataException("Nagłówek zawiera zbyt duży rozmiar danych.", ex);
        }
    }

    private static List<Point3D> ReadXyz(Stream stream, CancellationToken token)
    {
        using var reader = new StreamReader(stream);
        var points = new List<Point3D>();
        int lineNumber = 0;
        while (reader.ReadLine() is { } line)
        {
            token.ThrowIfCancellationRequested();
            lineNumber++;
            var values = Split(line.Split('#', 2)[0]);
            if (values.Length == 0) continue;
            if (values.Length is not (3 or 4 or 6 or 7))
                throw new InvalidDataException($"Wiersz {lineNumber}: oczekiwano XYZ, XYZ z intensywnością lub z RGB.");
            var point = MakePoint(Number(values[0]), Number(values[1]), Number(values[2]),
                values.Length is 4 or 7 ? Number(values[3]) : 0) with { HasIntensity = values.Length is 4 or 7 };
            if (values.Length >= 6)
                point = point with { Rgb = PackRgb(Number(values[^3]), Number(values[^2]), Number(values[^1])) };
            points.Add(point);
        }
        return points;
    }

    private static List<Point3D> ReadPly(Stream stream, CancellationToken token)
    {
        if (HeaderLine(stream) != "ply") throw new InvalidDataException("Brak nagłówka PLY.");
        var format = Split(HeaderLine(stream));
        if (format.Length != 3 || format[0] != "format" || format[2] != "1.0")
            throw new InvalidDataException("Obsługiwana jest wersja PLY 1.0.");
        bool binary = format[1] switch
        {
            "ascii" => false,
            "binary_little_endian" => true,
            _ => throw new InvalidDataException("Obsługiwane warianty PLY to ASCII i binary little-endian.")
        };

        var elements = new List<Element>();
        while (true)
        {
            token.ThrowIfCancellationRequested();
            var parts = Split(HeaderLine(stream));
            if (parts.Length == 0 || parts[0] is "comment" or "obj_info") continue;
            if (parts is ["end_header"]) break;
            if (parts is ["element", var name, var count])
                elements.Add(new(name, Count(count), []));
            else if (elements.Count > 0 && parts is ["property", var type, var field])
                elements[^1].Fields.Add(new(field, PlyType(type)));
            else if (elements.Count > 0 && parts is ["property", "list", var countType, var itemType, var list])
            {
                Scalar listCount = PlyType(countType);
                if (listCount is Scalar.F32 or Scalar.F64)
                    throw new InvalidDataException("Licznik elementów listy PLY musi być liczbą całkowitą.");
                elements[^1].Fields.Add(new(list, PlyType(itemType), ListCount: listCount));
            }
            else throw new InvalidDataException("Nieprawidłowa deklaracja w nagłówku PLY.");
        }

        if (!elements.Any(element => element.Name == "vertex"))
            throw new InvalidDataException("Plik PLY nie zawiera elementu vertex.");

        using var ascii = binary ? null : new StreamReader(stream, leaveOpen: true);
        Span<byte> listSize = stackalloc byte[8];
        foreach (var element in elements)
        {
            if (element.Name == "vertex")
                return ReadPoints(stream, ascii, element.Fields, element.Count, false, token);

            // Dane elementów zapisanych przed wierzchołkami trzeba ominąć w kolejności z nagłówka.
            for (int row = 0; row < element.Count; row++)
            {
                token.ThrowIfCancellationRequested();
                if (ascii != null) ReadDataLine(ascii);
                else foreach (var field in element.Fields)
                {
                    int count = 1;
                    if (field.ListCount is { } listType)
                    {
                        stream.ReadExactly(listSize[..Size(listType)]);
                        double length = BinaryNumber(listSize, listType);
                        if (length < 0 || length > int.MaxValue)
                            throw new InvalidDataException("Nieprawidłowa długość listy PLY.");
                        count = (int)length;
                    }
                    long skip = (long)count * Size(field.Type);
                    if (skip > stream.Length - stream.Position) throw new EndOfStreamException();
                    stream.Seek(skip, SeekOrigin.Current);
                }
            }
        }
        throw new InvalidDataException("Brak punktów PLY.");
    }

    private static List<Point3D> ReadPcd(Stream stream, CancellationToken token)
    {
        var header = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        while (true)
        {
            token.ThrowIfCancellationRequested();
            var parts = Split(HeaderLine(stream).Split('#', 2)[0]);
            if (parts.Length == 0) continue;
            if (!header.TryAdd(parts[0], parts[1..]))
                throw new InvalidDataException($"Powtórzona deklaracja PCD: {parts[0]}.");
            if (parts[0].Equals("DATA", StringComparison.OrdinalIgnoreCase)) break;
        }

        string[] Required(string key) => header.TryGetValue(key, out var values) && values.Length > 0
            ? values : throw new InvalidDataException($"Brakuje deklaracji {key} w nagłówku PCD.");
        string Single(string key) => Required(key) is [var value]
            ? value : throw new InvalidDataException($"Nieprawidłowa deklaracja {key} w nagłówku PCD.");

        bool binary = Single("DATA") switch
        {
            "ascii" => false,
            "binary" => true,
            _ => throw new InvalidDataException("Obsługiwane warianty PCD to ASCII i binary bez kompresji.")
        };

        var names = Required("FIELDS");
        var sizes = Required("SIZE");
        var types = Required("TYPE");
        header.TryGetValue("COUNT", out var counts);
        if (sizes.Length != names.Length || types.Length != names.Length ||
            (counts != null && counts.Length != names.Length))
            throw new InvalidDataException("Deklaracje FIELDS, SIZE, TYPE i COUNT w PCD muszą mieć zgodną długość.");

        var fields = new List<Field>(names.Length);
        for (int i = 0; i < names.Length; i++)
        {
            int count = counts == null ? 1 : Count(counts[i]);
            if (count == 0) throw new InvalidDataException("PCD: COUNT pola musi być dodatni.");
            fields.Add(new(names[i], PcdType(types[i], Count(sizes[i])), count));
        }
        int pointCount = header.ContainsKey("POINTS")
            ? Count(Single("POINTS"))
            : checked(Count(Single("WIDTH")) * Count(Single("HEIGHT")));
        if (header.ContainsKey("WIDTH") && header.ContainsKey("HEIGHT") &&
            pointCount != (long)Count(Single("WIDTH")) * Count(Single("HEIGHT")))
            throw new InvalidDataException("PCD: POINTS musi być równe WIDTH × HEIGHT.");

        using var ascii = binary ? null : new StreamReader(stream);
        return ReadPoints(stream, ascii, fields, pointCount, true, token);
    }

    private static List<Point3D> ReadPoints(Stream stream, StreamReader? ascii, List<Field> fields,
        int count, bool pcd, CancellationToken token)
    {
        if (fields.Any(field => field.ListCount != null))
            throw new InvalidDataException("Właściwości listowe w wierzchołkach PLY nie są obsługiwane.");
        int Find(string name) => fields.FindIndex(field => field.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        int x = Find("x"), y = Find("y"), z = Find("z"), intensity = Find("intensity");
        if (x < 0 || y < 0 || z < 0)
            throw new InvalidDataException("Brakuje pól X, Y lub Z.");
        int red = Find("red"), green = Find("green"), blue = Find("blue");
        int rgb = pcd ? Find("rgb") : -1;
        if (pcd && rgb < 0) rgb = Find("rgba");
        foreach (int index in new[] { x, y, z, intensity, red, green, blue, rgb })
            if (index >= 0 && fields[index].Count != 1)
                throw new InvalidDataException($"Pole {fields[index].Name} musi zawierać jedną wartość.");
        if (rgb >= 0 && Size(fields[rgb].Type) != 4)
            throw new InvalidDataException("Pole RGB w PCD musi zajmować 4 bajty.");

        var offsets = new int[fields.Count];
        int rowSize = 0;
        for (int i = 0; i < fields.Count; i++)
        {
            offsets[i] = rowSize;
            rowSize = checked(rowSize + fields[i].Count * (ascii == null ? Size(fields[i].Type) : 1));
        }
        if (rowSize > 1_048_576) throw new InvalidDataException("Rekord punktu jest zbyt szeroki.");
        if (ascii == null && (long)rowSize * count > stream.Length - stream.Position)
            throw new EndOfStreamException();
        byte[] bytes = ascii == null ? new byte[rowSize] : [];
        var points = new List<Point3D>(Math.Min(count, 65_536));

        for (int row = 0; row < count; row++)
        {
            token.ThrowIfCancellationRequested();
            string[]? values = null;
            if (ascii == null) stream.ReadExactly(bytes);
            else
            {
                values = ReadDataLine(ascii);
                if (values.Length != rowSize)
                    throw new InvalidDataException($"Punkt {row + 1}: liczba wartości różni się od nagłówka.");
            }

            double Value(int field) => values == null
                ? BinaryNumber(bytes.AsSpan(offsets[field]), fields[field].Type)
                : Number(values[offsets[field]]);

            double px = Value(x), py = Value(y), pz = Value(z);
            // Zorganizowane chmury PCD mogą zawierać brakujące pomiary zapisane jako NaN.
            if (pcd && (double.IsNaN(px) || double.IsNaN(py) || double.IsNaN(pz))) continue;
            var point = MakePoint(px, py, pz, intensity < 0 ? 0 : Value(intensity)) with { HasIntensity = intensity >= 0 };
            if (red >= 0 && green >= 0 && blue >= 0)
                point = point with { Rgb = PackRgb(Value(red), Value(green), Value(blue)) };
            else if (rgb >= 0)
            {
                uint packed;
                if (values == null) packed = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offsets[rgb]));
                else
                {
                    string value = values[offsets[rgb]];
                    // PCL zapisuje też RGB oznaczone jako F jako liczbę całkowitą w tekście.
                    if (uint.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint integer))
                        packed = integer;
                    else if (fields[rgb].Type == Scalar.I32 &&
                             int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int signed))
                        packed = unchecked((uint)signed);
                    else if (fields[rgb].Type == Scalar.F32)
                        packed = BitConverter.SingleToUInt32Bits((float)Number(value));
                    else throw new InvalidDataException("Nieprawidłowe pole RGB w PCD.");
                }
                point = point with { Rgb = packed & 0x00ffffff };
            }
            points.Add(point);
        }

        return points;
    }

    private static Point3D MakePoint(double x, double y, double z, double intensity)
    {
        if (!float.IsFinite((float)x) || !float.IsFinite((float)y) || !float.IsFinite((float)z))
            throw new InvalidDataException("Współrzędne XYZ muszą być skończonymi liczbami.");
        return new((float)x, (float)y, (float)z, Channel(intensity));
    }

    private static uint PackRgb(double red, double green, double blue) =>
        (uint)Channel(red) << 16 | (uint)Channel(green) << 8 | Channel(blue);

    private static byte Channel(double value) => double.IsFinite(value)
        ? (byte)Math.Round(Math.Clamp(value, 0, 255))
        : throw new InvalidDataException("Intensywność i kanały RGB muszą być skończonymi liczbami.");

    private static string HeaderLine(Stream stream)
    {
        // Nagłówek czytamy bajtowo, aby nie pobrać części danych binarnych do bufora tekstowego.
        var line = new StringBuilder();
        for (int value; (value = stream.ReadByte()) != -1;)
        {
            if (value == '\n') return line.ToString().TrimEnd('\r');
            if (line.Length >= 65_536 || stream.Position > 1_048_576)
                throw new InvalidDataException("Nagłówek pliku jest zbyt duży.");
            line.Append((char)value);
        }
        throw new EndOfStreamException();
    }

    private static string[] ReadDataLine(StreamReader reader)
    {
        while (reader.ReadLine() is { } line)
        {
            var values = Split(line);
            if (values.Length > 0 && !values[0].StartsWith('#')) return values;
        }
        throw new EndOfStreamException();
    }

    private static string[] Split(string line) => line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    private static double Number(string value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number)
            ? number : throw new InvalidDataException($"Nieprawidłowa liczba: {value}.");

    private static int Count(string value) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int count)
            ? count : throw new InvalidDataException($"Nieprawidłowa liczba elementów: {value}.");

    private static Scalar PlyType(string type) => type switch
    {
        "char" or "int8" => Scalar.I8, "uchar" or "uint8" => Scalar.U8,
        "short" or "int16" => Scalar.I16, "ushort" or "uint16" => Scalar.U16,
        "int" or "int32" => Scalar.I32, "uint" or "uint32" => Scalar.U32,
        "float" or "float32" => Scalar.F32, "double" or "float64" => Scalar.F64,
        _ => throw new InvalidDataException($"Nieobsługiwany typ PLY: {type}.")
    };

    private static Scalar PcdType(string type, int size) => (type, size) switch
    {
        ("I", 1) => Scalar.I8, ("U", 1) => Scalar.U8,
        ("I", 2) => Scalar.I16, ("U", 2) => Scalar.U16,
        ("I", 4) => Scalar.I32, ("U", 4) => Scalar.U32,
        ("I", 8) => Scalar.I64, ("U", 8) => Scalar.U64,
        ("F", 4) => Scalar.F32, ("F", 8) => Scalar.F64,
        _ => throw new InvalidDataException($"Nieobsługiwany typ PCD: TYPE {type}, SIZE {size}.")
    };

    private static int Size(Scalar type) => type switch
    {
        Scalar.I8 or Scalar.U8 => 1,
        Scalar.I16 or Scalar.U16 => 2,
        Scalar.I32 or Scalar.U32 or Scalar.F32 => 4,
        _ => 8
    };

    private static double BinaryNumber(ReadOnlySpan<byte> bytes, Scalar type) => type switch
    {
        Scalar.I8 => unchecked((sbyte)bytes[0]), Scalar.U8 => bytes[0],
        Scalar.I16 => BinaryPrimitives.ReadInt16LittleEndian(bytes),
        Scalar.U16 => BinaryPrimitives.ReadUInt16LittleEndian(bytes),
        Scalar.I32 => BinaryPrimitives.ReadInt32LittleEndian(bytes),
        Scalar.U32 => BinaryPrimitives.ReadUInt32LittleEndian(bytes),
        Scalar.I64 => BinaryPrimitives.ReadInt64LittleEndian(bytes),
        Scalar.U64 => BinaryPrimitives.ReadUInt64LittleEndian(bytes),
        Scalar.F32 => BinaryPrimitives.ReadSingleLittleEndian(bytes),
        _ => BinaryPrimitives.ReadDoubleLittleEndian(bytes)
    };
}
