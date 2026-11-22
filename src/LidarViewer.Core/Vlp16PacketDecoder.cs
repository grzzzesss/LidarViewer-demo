using System.Buffers.Binary;

namespace LidarViewer.Core;

public sealed class Vlp16PacketDecoder
{
    private const int BlockSize = 100;
    private const int BlockCount = 12;
    private const int FullTurn = 36000;
    private readonly int[] azimuths = new int[BlockCount];
    private List<Point3D> points = new();
    private int? previousAzimuth;
    private int coveredAngle;

    public bool TryDecodePacket(ReadOnlySpan<byte> payload, out List<IReadOnlyList<Point3D>> completed)
    {
        completed = [];
        if (payload.Length != 1206 || !ReadPacketHeader(payload, azimuths, out bool dual))
            return false;

        int stride = dual ? 2 : 1;
        int angleStep = 0;
        for (int block = 0; block < BlockCount; block += stride)
        {
            int angle = azimuths[block];
            if (previousAzimuth is int previous)
            {
                int delta = (angle - previous + FullTurn) % FullTurn;
                if (delta < FullTurn / 2) coveredAngle += delta;
            }
            if (coveredAngle >= FullTurn || points.Count >= 100_000)
            {
                if (points.Count > 0) completed.Add(points.AsReadOnly());
                points = new List<Point3D>();
                coveredAngle = 0;
            }
            previousAzimuth = angle;
            if (block + stride < BlockCount)
            {
                int delta = (azimuths[block + stride] - angle + FullTurn) % FullTurn;
                angleStep = delta < FullTurn / 2 ? delta : 0;
            }
            for (int echo = 0; echo < stride; echo++)
                AppendBlock(payload.Slice((block + echo) * BlockSize, BlockSize), angle, angleStep, points);
        }
        return true;
    }

    internal IReadOnlyList<Point3D> Finish()
    {
        var remaining = points.AsReadOnly();
        points = new List<Point3D>();
        return remaining;
    }

    private static readonly int[] Elevation = [-15, 1, -13, 3, -11, 5, -9, 7, -7, 9, -5, 11, -3, 13, -1, 15];
    private static readonly (float Sin, float Cos)[] ElevationTrig =
        Elevation.Select(angle => MathF.SinCos(angle * MathF.PI / 180)).ToArray();

    internal static bool ReadPacketHeader(ReadOnlySpan<byte> payload, int[] azimuths, out bool dual)
    {
        byte mode = payload[1204];
        dual = mode == 0x39;
        if (payload[1205] != 0x22 || mode is not (0x37 or 0x38 or 0x39)) return false;
        for (int block = 0; block < BlockCount; block++)
        {
            int start = block * BlockSize;
            if (payload[start] != 0xff || payload[start + 1] != 0xee) return false;
            azimuths[block] = BinaryPrimitives.ReadUInt16LittleEndian(payload[(start + 2)..]);
            if (azimuths[block] >= FullTurn) return false;
            if (dual && block % 2 == 1 && azimuths[block] != azimuths[block - 1]) return false;
        }
        return true;
    }

    internal static void AppendBlock(ReadOnlySpan<byte> block, int azimuth, int angleStep, List<Point3D> points)
    {
        for (int sample = 0; sample < 32; sample++)
        {
            int offset = 4 + sample * 3;
            ushort distance = BinaryPrimitives.ReadUInt16LittleEndian(block[offset..]);
            if (distance == 0) continue;

            int laser = sample % 16;
            // Dwa cykle strzelania po 55,296 µs na blok, 2,304 µs między laserami.
            float fraction = ((sample / 16) * 24 + laser) / 48f;
            float radians = (azimuth + fraction * angleStep) * (MathF.PI / 18000);
            var (sinAzimuth, cosAzimuth) = MathF.SinCos(radians);
            var (sinElevation, cosElevation) = ElevationTrig[laser];
            float meters = distance * 0.002f;
            float horizontal = meters * cosElevation;
            points.Add(new(horizontal * sinAzimuth, horizontal * cosAzimuth,
                meters * sinElevation, block[offset + 2]));
        }
    }

}
