using System.Buffers.Binary;
using LidarViewer.Core;
using Xunit;

namespace LidarViewer.Core.Tests;

public sealed class Vlp16PcapReaderTests
{
    [Theory]
    [InlineData(true, false, 30, 6)]
    [InlineData(false, false, 30, 6)]
    [InlineData(true, true, 60, 12)]
    public void ReadsScansAcrossAzimuthWrap(bool littleEndian, bool dual, int firstCount, int lastCount)
    {
        using var capture = new TestCapture(littleEndian);
        // Start przed 360°, aby sprawdzić, że przejście przez zero nie urywa pierwszej klatki.
        for (int packet = 0; packet < (dual ? 6 : 3); packet++)
            capture.WritePacket(packet, dual);

        var frames = Vlp16PcapReader.ReadFrames(capture.Path).ToArray();

        Assert.Equal(2, frames.Length);
        Assert.Equal(firstCount, frames[0].Count);
        Assert.Equal(lastCount, frames[1].Count);
        var point = frames[0][0];
        Assert.InRange(point.X, -0.403f, -0.401f);
        Assert.InRange(point.Y, 1.888f, 1.891f);
        Assert.InRange(point.Z, -0.519f, -0.517f);
        Assert.Equal(77, point.Intensity);
    }

    [Fact]
    public void RejectsCaptureWithNoVlp16Points()
    {
        using var capture = new TestCapture(true);
        Assert.Throws<InvalidDataException>(() => Vlp16PcapReader.ReadFrames(capture.Path).ToArray());
    }

    [Fact]
    public void RejectsTruncatedCapture()
    {
        using var capture = new TestCapture(true);
        capture.WritePacket(0, false);
        using (var file = File.OpenWrite(capture.Path)) file.SetLength(file.Length - 1);
        Assert.Throws<EndOfStreamException>(() => Vlp16PcapReader.ReadFrames(capture.Path).ToArray());
    }

    internal sealed class TestCapture : IDisposable
    {
        public string Path { get; } = System.IO.Path.GetTempFileName();
        private readonly bool littleEndian;
        private readonly bool nanoseconds;

        public TestCapture(bool littleEndian, bool nanoseconds = false)
        {
            this.littleEndian = littleEndian;
            this.nanoseconds = nanoseconds;
            var header = new byte[24];
            Write32(header, nanoseconds ? 0xa1b23c4du : 0xa1b2c3d4u);
            Write16(header.AsSpan(4), 2);
            Write16(header.AsSpan(6), 4);
            Write32(header.AsSpan(16), 65535);
            Write32(header.AsSpan(20), 1);
            File.WriteAllBytes(Path, header);
        }

        public void WritePacket(int index, bool dual, double seconds = 0, int angleStep = 1200)
        {
            const int frameSize = 1248;
            var record = new byte[16 + frameSize];
            Write32(record, (uint)seconds);
            Write32(record.AsSpan(4), (uint)Math.Round((seconds - Math.Floor(seconds)) * (nanoseconds ? 1_000_000_000 : 1_000_000)));
            Write32(record.AsSpan(8), frameSize);
            Write32(record.AsSpan(12), frameSize);
            var frame = record.AsSpan(16);
            frame[12] = 0x08;
            frame[14] = 0x45;
            BinaryPrimitives.WriteUInt16BigEndian(frame[16..], 1234);
            frame[23] = 17;
            BinaryPrimitives.WriteUInt16BigEndian(frame[38..], 1214);
            var payload = frame[42..];
            payload[1204] = dual ? (byte)0x39 : (byte)0x37;
            payload[1205] = 0x22;
            int stride = dual ? 2 : 1;
            for (int block = 0; block < 12; block++)
            {
                var data = payload[(block * 100)..];
                data[0] = 0xff;
                data[1] = 0xee;
                int angle = (34800 + (index * (12 / stride) + block / stride) * angleStep) % 36000;
                BinaryPrimitives.WriteUInt16LittleEndian(data[2..], (ushort)angle);
                BinaryPrimitives.WriteUInt16LittleEndian(data[4..], 1000);
                data[6] = 77;
            }
            using var file = new FileStream(Path, FileMode.Append);
            file.Write(record);
        }

        private void Write16(Span<byte> data, ushort value)
        {
            if (littleEndian) BinaryPrimitives.WriteUInt16LittleEndian(data, value);
            else BinaryPrimitives.WriteUInt16BigEndian(data, value);
        }

        private void Write32(Span<byte> data, uint value)
        {
            if (littleEndian) BinaryPrimitives.WriteUInt32LittleEndian(data, value);
            else BinaryPrimitives.WriteUInt32BigEndian(data, value);
        }

        public void Dispose() => File.Delete(Path);
    }
}

