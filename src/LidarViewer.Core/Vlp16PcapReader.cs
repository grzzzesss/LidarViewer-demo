using System.Buffers.Binary;

namespace LidarViewer.Core;

public static class Vlp16PcapReader
{
    private const int PayloadSize = 1206;

    public static IEnumerable<IReadOnlyList<Point3D>> ReadFrames(string path, CancellationToken token = default)
    {
        using var stream = File.OpenRead(path);
        var header = new byte[24];
        stream.ReadExactly(header);
        bool littleEndian = BinaryPrimitives.ReadUInt32LittleEndian(header) switch
        {
            0xa1b2c3d4 or 0xa1b23c4d => true,
            0xd4c3b2a1 or 0x4d3cb2a1 => false,
            _ => throw new InvalidDataException("Wybierz klasyczny plik PCAP. Format PCAPNG nie jest obsługiwany.")
        };

        if (Read16(header.AsSpan(4), littleEndian) != 2 || Read16(header.AsSpan(6), littleEndian) != 4)
            throw new InvalidDataException("Obsługiwana jest wersja PCAP 2.4.");
        if ((Read32(header.AsSpan(20), littleEndian) & 0xffff) != 1)
            throw new InvalidDataException("Plik PCAP musi zawierać ramki Ethernet.");

        var record = new byte[16];
        var ethernet = new byte[65_536];
        var decoder = new Vlp16PacketDecoder();
        bool foundPoints = false;

        while (true)
        {
            token.ThrowIfCancellationRequested();
            int firstByte = stream.ReadByte();
            if (firstByte < 0) break;
            record[0] = (byte)firstByte;
            stream.ReadExactly(record.AsSpan(1));
            uint length = Read32(record.AsSpan(8), littleEndian);
            if (length > ethernet.Length || length > Read32(record.AsSpan(12), littleEndian))
                throw new InvalidDataException("Nieprawidłowa długość pakietu w pliku PCAP.");
            stream.ReadExactly(ethernet.AsSpan(0, (int)length));

            int offset = FindPayload(ethernet.AsSpan(0, (int)length));
            if (offset < 0 || !decoder.TryDecodePacket(ethernet.AsSpan(offset, PayloadSize), out var completed))
                continue;
            foreach (var frame in completed)
            {
                foundPoints = true;
                yield return frame;
            }
        }

        var points = decoder.Finish();
        if (points.Count > 0) yield return points;
        else if (!foundPoints) throw new InvalidDataException("W pliku nie znaleziono pomiarów VLP-16 z niezerową odległością.");
    }

    internal static int FindPayload(ReadOnlySpan<byte> frame)
    {
        if (frame.Length < 14) return -1;
        int ip = 14;
        ushort type = BinaryPrimitives.ReadUInt16BigEndian(frame[12..]);
        while (type is 0x8100 or 0x88a8)
        {
            if (frame.Length < ip + 4) return -1;
            type = BinaryPrimitives.ReadUInt16BigEndian(frame[(ip + 2)..]);
            ip += 4;
        }

        if (type != 0x0800 || frame.Length < ip + 20 || frame[ip] >> 4 != 4 || frame[ip + 9] != 17)
            return -1;
        int ipHeaderSize = (frame[ip] & 0x0f) * 4;
        int packetLength = BinaryPrimitives.ReadUInt16BigEndian(frame[(ip + 2)..]);
        ushort fragmentation = BinaryPrimitives.ReadUInt16BigEndian(frame[(ip + 6)..]);
        int transport = ip + ipHeaderSize;
        if (ipHeaderSize < 20 || (fragmentation & 0x3fff) != 0 ||
            packetLength < ipHeaderSize + 8 + PayloadSize || frame.Length < transport + 8 + PayloadSize)
            return -1;

        return BinaryPrimitives.ReadUInt16BigEndian(frame[(transport + 4)..]) == 8 + PayloadSize
            ? transport + 8
            : -1;
    }


    private static ushort Read16(ReadOnlySpan<byte> bytes, bool littleEndian) => littleEndian
        ? BinaryPrimitives.ReadUInt16LittleEndian(bytes) : BinaryPrimitives.ReadUInt16BigEndian(bytes);

    private static uint Read32(ReadOnlySpan<byte> bytes, bool littleEndian) => littleEndian
        ? BinaryPrimitives.ReadUInt32LittleEndian(bytes) : BinaryPrimitives.ReadUInt32BigEndian(bytes);
}

