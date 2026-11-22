using System.Buffers.Binary;

namespace LidarViewer.Core;

public sealed class PcapRecording
{
    public sealed record Position(long PacketOffset, int Block);
    public sealed record Frame(Position Start, Position? End, TimeSpan Time);
    private sealed record Group(Position Position, byte[] Data, int Offset, int Stride, int Angle, int Step,
        double Seconds, bool InvalidTime);

    private readonly string path;
    private readonly long fileLength;
    private readonly DateTime modified;
    public IReadOnlyList<Frame> Frames { get; }
    public TimeSpan Duration { get; }
    public bool HasTimestampWarnings { get; }

    private PcapRecording(string path, List<Frame> frames, double duration, bool warnings)
    {
        this.path = path;
        Frames = frames.AsReadOnly();
        Duration = TimeSpan.FromSeconds(duration);
        HasTimestampWarnings = warnings;
        var info = new FileInfo(path);
        fileLength = info.Length;
        modified = info.LastWriteTimeUtc;
    }

    public static PcapRecording Open(string path, CancellationToken token = default)
    {
        var frames = new List<Frame>();
        Position? start = null;
        double firstTime = 0, startTime = 0, lastTime = 0;
        int? previousAngle = null;
        int covered = 0, pointCount = 0;
        bool warnings = false;

        foreach (var group in ReadGroups(path, 24, token))
        {
            double time = group.Seconds;
            warnings |= group.InvalidTime;
            if (start == null)
            {
                start = group.Position;
                firstTime = startTime = lastTime = time;
            }
            if (time < lastTime || group.InvalidTime)
            {
                warnings = true;
                time = lastTime + 0.000110592;
            }
            lastTime = time;

            if (previousAngle is int previous)
            {
                int delta = (group.Angle - previous + 36000) % 36000;
                if (delta < 18000) covered += delta;
            }
            if (covered >= 36000 || pointCount >= 100_000)
            {
                if (pointCount > 0)
                    frames.Add(new(start, group.Position, TimeSpan.FromSeconds(startTime - firstTime)));
                start = group.Position;
                startTime = time;
                pointCount = 0;
                covered = 0;
            }
            previousAngle = group.Angle;
            // Indeks liczy tylko niezerowe pomiary, bez obliczania XYZ.
            for (int echo = 0; echo < group.Stride; echo++)
            for (int sample = 0; sample < 32; sample++)
                if (BinaryPrimitives.ReadUInt16LittleEndian(
                    group.Data.AsSpan(group.Offset + echo * 100 + 4 + sample * 3)) != 0)
                    pointCount++;
        }
        if (pointCount > 0 && start != null)
            frames.Add(new(start, null, TimeSpan.FromSeconds(startTime - firstTime)));
        if (frames.Count == 0)
            throw new InvalidDataException("W pliku nie znaleziono klatek VLP-16 z niezerowymi pomiarami.");

        return new(path, frames, Math.Max(0, lastTime - firstTime + 0.000110592), warnings);
    }

    public IReadOnlyList<Point3D> ReadFrame(int index, CancellationToken token = default)
    {
        if (index < 0 || index >= Frames.Count) throw new ArgumentOutOfRangeException(nameof(index));
        var info = new FileInfo(path);
        if (info.Length != fileLength || info.LastWriteTimeUtc != modified)
            throw new IOException("Nagranie zmieniło się od czasu indeksowania. Otwórz je ponownie.");

        var frame = Frames[index];
        var points = new List<Point3D>();
        foreach (var group in ReadGroups(path, frame.Start.PacketOffset, token))
        {
            if (group.Position.PacketOffset == frame.Start.PacketOffset && group.Position.Block < frame.Start.Block)
                continue;
            if (frame.End is { } end &&
                (group.Position.PacketOffset > end.PacketOffset ||
                 (group.Position.PacketOffset == end.PacketOffset && group.Position.Block >= end.Block)))
                break;
            for (int echo = 0; echo < group.Stride; echo++)
                Vlp16PacketDecoder.AppendBlock(group.Data.AsSpan(group.Offset + echo * 100, 100),
                    group.Angle, group.Step, points);
        }
        return points.AsReadOnly();
    }

    public int FindFrame(TimeSpan time)
    {
        // Najbliższy czas początku klatki. Przy remisie wybieramy wcześniejszą.
        if (time <= Frames[0].Time) return 0;
        if (time >= Duration) return Frames.Count - 1;
        int low = 0, high = Frames.Count - 1;
        while (low < high)
        {
            int middle = low + (high - low) / 2;
            if (Frames[middle].Time < time) low = middle + 1;
            else high = middle;
        }
        if (low == 0) return 0;
        return time - Frames[low - 1].Time <= Frames[low].Time - time ? low - 1 : low;
    }

    private static IEnumerable<Group> ReadGroups(string path, long start, CancellationToken token)
    {
        using var stream = File.OpenRead(path);
        var header = new byte[24];
        stream.ReadExactly(header);
        uint magic = BinaryPrimitives.ReadUInt32LittleEndian(header);
        bool little = magic switch
        {
            0xa1b2c3d4 or 0xa1b23c4d => true,
            0xd4c3b2a1 or 0x4d3cb2a1 => false,
            _ => throw new InvalidDataException("Obsługiwany jest klasyczny PCAP, bez PCAPNG.")
        };
        bool nano = magic is 0xa1b23c4d or 0x4d3cb2a1;
        uint Read32(byte[] data, int offset) => little
            ? BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset))
            : BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset));
        ushort major = little ? BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(4))
            : BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(4));
        ushort minor = little ? BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(6))
            : BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(6));
        if (major != 2 || minor != 4 || (Read32(header, 20) & 0xffff) != 1)
            throw new InvalidDataException("Wymagany PCAP 2.4 z ramkami Ethernet.");

        stream.Position = start;
        var record = new byte[16];
        var data = new byte[65536];
        var azimuths = new int[12];
        while (true)
        {
            token.ThrowIfCancellationRequested();
            long position = stream.Position;
            int first = stream.ReadByte();
            if (first < 0) yield break;
            record[0] = (byte)first;
            stream.ReadExactly(record.AsSpan(1));
            uint length = Read32(record, 8);
            if (length > data.Length || length > Read32(record, 12))
                throw new InvalidDataException("Nieprawidłowa długość pakietu PCAP.");
            stream.ReadExactly(data.AsSpan(0, (int)length));
            int payload = Vlp16PcapReader.FindPayload(data.AsSpan(0, (int)length));
            if (payload < 0 || !Vlp16PacketDecoder.ReadPacketHeader(data.AsSpan(payload, 1206), azimuths, out bool dual))
                continue;

            uint fraction = Read32(record, 4);
            double units = nano ? 1_000_000_000 : 1_000_000;
            bool invalidTime = fraction >= units;
            double seconds = Read32(record, 0) + (invalidTime ? 0 : fraction / units);
            int stride = dual ? 2 : 1;
            int step = 0;
            for (int block = 0; block < 12; block += stride)
            {
                if (block + stride < 12)
                {
                    int delta = (azimuths[block + stride] - azimuths[block] + 36000) % 36000;
                    step = delta < 18000 ? delta : 0;
                }
                yield return new(new(position, block), data, payload + block * 100, stride,
                    azimuths[block], step, seconds + (block / stride) * 0.000110592, invalidTime);
            }
        }
    }
}
