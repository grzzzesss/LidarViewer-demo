using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using LidarViewer.Core;
using Xunit;

namespace LidarViewer.Core.Tests;

public sealed class PcapRecorderTests
{
    private static readonly IPEndPoint Source = new(IPAddress.Parse("192.168.1.201"), 2368);
    private static readonly IPEndPoint Destination = new(IPAddress.Parse("192.168.1.100"), 2368);
    private static readonly DateTimeOffset Time = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000).AddTicks(1234560);

    private static byte[] Packet(int index, bool dual = false)
    {
        using var capture = new Vlp16PcapReaderTests.TestCapture(true);
        capture.WritePacket(index, dual);
        return File.ReadAllBytes(capture.Path).AsSpan(82, 1206).ToArray();
    }

    private sealed class Output : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"recording-{Guid.NewGuid()}.pcap");
        public void Dispose() => File.Delete(Path);
    }

    private sealed record Record(uint Seconds, uint Microseconds, byte[] Frame);

    // Niezależny od importera odczyt nagłówków i długości każdego rekordu.
    private static List<Record> ReadRecords(string path)
    {
        using var reader = new BinaryReader(File.OpenRead(path));
        Assert.Equal(0xa1b2c3d4u, reader.ReadUInt32());
        Assert.Equal(2, reader.ReadUInt16());
        Assert.Equal(4, reader.ReadUInt16());
        Assert.Equal(0u, reader.ReadUInt32());
        Assert.Equal(0u, reader.ReadUInt32());
        Assert.Equal(65535u, reader.ReadUInt32());
        Assert.Equal(1u, reader.ReadUInt32());
        var records = new List<Record>();
        while (reader.BaseStream.Position < reader.BaseStream.Length)
        {
            uint seconds = reader.ReadUInt32(), microseconds = reader.ReadUInt32();
            Assert.InRange(microseconds, 0u, 999999u);
            Assert.Equal(1248u, reader.ReadUInt32());
            Assert.Equal(1248u, reader.ReadUInt32());
            var frame = reader.ReadBytes(1248);
            Assert.Equal(1248, frame.Length);
            records.Add(new(seconds, microseconds, frame));
        }
        return records;
    }

    // Suma słów powinna wynosić 0xffff, łącznie z polem checksum.
    private static ushort Sum(ReadOnlySpan<byte> bytes, uint initial = 0)
    {
        for (int i = 0; i < bytes.Length; i += 2)
            initial += (uint)(bytes[i] * 256 + (i + 1 < bytes.Length ? bytes[i + 1] : 0));
        while (initial > 65535) initial = (initial & 65535) + (initial >> 16);
        return (ushort)initial;
    }

    [Fact]
    public async Task WritesCompleteEthernetIpv4UdpWithChecksumsAndUnchangedPayload()
    {
        using var output = new Output();
        await using var recorder = new PcapRecorder(output.Path);
        var packet = Packet(0);
        var original = packet.ToArray();
        Assert.True(recorder.TryRecord(packet, Source, Destination, Time));
        Array.Clear(packet); // Odbiornik ponownie wykorzystuje bufor.
        await recorder.StopAsync();
        await recorder.StopAsync(); // Wielokrotne zatrzymanie jest bezpieczne.
        var record = Assert.Single(ReadRecords(output.Path));
        Assert.Equal(1_700_000_000u, record.Seconds);
        Assert.Equal(123456u, record.Microseconds);
        var f = record.Frame;
        Assert.Equal(0x0800, BinaryPrimitives.ReadUInt16BigEndian(f.AsSpan(12)));
        Assert.Equal(0x45, f[14]);
        Assert.Equal(1234, BinaryPrimitives.ReadUInt16BigEndian(f.AsSpan(16)));
        Assert.Equal(17, f[23]);
        Assert.Equal(Source.Address.GetAddressBytes(), f.AsSpan(26, 4).ToArray());
        Assert.Equal(Destination.Address.GetAddressBytes(), f.AsSpan(30, 4).ToArray());
        Assert.Equal(2368, BinaryPrimitives.ReadUInt16BigEndian(f.AsSpan(34)));
        Assert.Equal(2368, BinaryPrimitives.ReadUInt16BigEndian(f.AsSpan(36)));
        Assert.Equal(1214, BinaryPrimitives.ReadUInt16BigEndian(f.AsSpan(38)));
        Assert.Equal(65535, Sum(f.AsSpan(14, 20)));
        Assert.NotEqual(0, BinaryPrimitives.ReadUInt16BigEndian(f.AsSpan(40)));
        uint pseudo = (uint)Sum(f.AsSpan(26, 8)) + 17 + 1214;
        Assert.Equal(65535, Sum(f.AsSpan(34), pseudo));
        Assert.Equal(original, f.AsSpan(42).ToArray());
        Assert.Equal(1, recorder.WrittenPackets);
        Assert.Equal(new FileInfo(output.Path).Length, recorder.WrittenBytes);
        Assert.False(recorder.TryRecord(original, Source, Destination, Time));
        using var exclusive = new FileStream(output.Path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    [Theory]
    [InlineData(0x37, false, 6)]
    [InlineData(0x38, false, 6)]
    [InlineData(0x39, true, 12)]
    public async Task RoundTripPreservesPointsFrameBoundariesOrderAndTimeline(int mode, bool dual, int count)
    {
        using var output = new Output();
        using var expected = new Vlp16PcapReaderTests.TestCapture(true);
        await using var recorder = new PcapRecorder(output.Path);
        for (int i = 0; i < count; i++)
        {
            var payload = Packet(i, dual);
            payload[1204] = (byte)mode;
            Assert.True(recorder.TryRecord(payload, Source, Destination, Time.AddMilliseconds(i * 10)));
            expected.WritePacket(i, dual, i * .01);
        }
        await recorder.StopAsync();
        var frames = Vlp16PcapReader.ReadFrames(output.Path).ToArray();
        var expectedFrames = Vlp16PcapReader.ReadFrames(expected.Path).ToArray();
        Assert.Equal(expectedFrames.Length, frames.Length);
        for (int i = 0; i < frames.Length; i++) Assert.Equal(expectedFrames[i].ToArray(), frames[i].ToArray());
        var records = ReadRecords(output.Path);
        Assert.Equal(count, records.Count);
        for (int i = 0; i < count; i++)
        {
            var payload = Packet(i, dual); payload[1204] = (byte)mode;
            Assert.Equal(payload, records[i].Frame.AsSpan(42).ToArray());
            Assert.Equal((uint)(123456 + i * 10000), records[i].Microseconds);
        }
        var index = PcapRecording.Open(output.Path);
        Assert.Equal(frames.Length, index.Frames.Count);
        for (int i = 0; i < frames.Length; i++) Assert.Equal(frames[i].ToArray(), index.ReadFrame(i).ToArray());
        Assert.True(index.Duration > TimeSpan.Zero);
    }

    [Fact]
    public async Task EmptyRecordingIsValidHeaderAndReleasesFile()
    {
        using var output = new Output();
        await using (var recorder = new PcapRecorder(output.Path))
        {
            await recorder.StopAsync();
            Assert.Equal(24, recorder.WrittenBytes);
            Assert.Empty(ReadRecords(output.Path));
        }
        // Importer zgłasza brak pomiarów, a nie uszkodzenie pliku.
        var error = Assert.Throws<InvalidDataException>(() => Vlp16PcapReader.ReadFrames(output.Path).ToArray());
        Assert.Contains("nie znaleziono pomiarów", error.Message);
        using var exclusive = new FileStream(output.Path, FileMode.Open, FileAccess.Write, FileShare.None);
    }

    [Fact]
    public async Task BackwardsClockDoesNotReversePacketOrderOrTime()
    {
        using var output = new Output();
        await using var recorder = new PcapRecorder(output.Path);
        recorder.TryRecord(Packet(0), Source, Destination, Time);
        recorder.TryRecord(Packet(1), Source, Destination, Time.AddSeconds(-1));
        await recorder.StopAsync();
        var records = ReadRecords(output.Path);
        Assert.Equal(records[0].Seconds, records[1].Seconds);
        Assert.Equal(records[0].Microseconds, records[1].Microseconds);
        Assert.Equal(Packet(1), records[1].Frame.AsSpan(42).ToArray());
    }

    [Fact]
    public void DoesNotOverwriteWithoutConsentAndReportsInvalidLocation()
    {
        using var output = new Output();
        File.WriteAllText(output.Path, "do not overwrite");
        Assert.Throws<IOException>(() => new PcapRecorder(output.Path));
        Assert.Equal("do not overwrite", File.ReadAllText(output.Path));
        Assert.Throws<DirectoryNotFoundException>(() => new PcapRecorder(System.IO.Path.Combine(output.Path, "x.pcap")));
        Assert.Throws<UnauthorizedAccessException>(() => new PcapRecorder(System.IO.Path.GetTempPath().TrimEnd(System.IO.Path.DirectorySeparatorChar), true));
    }

    private sealed class TestDisk(bool fail) : MemoryStream
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ManualResetEventSlim Release { get; } = new(false);
        public bool Closed { get; private set; }
        public override void Write(ReadOnlySpan<byte> bytes)
        {
            if (Position >= 24)
            {
                Entered.TrySetResult();
                if (fail) throw new IOException("Test: dysk pełny");
                if (!Release.Wait(TimeSpan.FromSeconds(10))) throw new IOException("Test: upłynął limit czasu");
            }
            var copy = bytes.ToArray();
            base.Write(copy, 0, copy.Length);
        }
        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
        protected override void Dispose(bool disposing) { Closed = true; base.Dispose(disposing); }
    }

    [Fact]
    public async Task DiskErrorStopsRecordingWithoutLeakingFileOrClaimingSuccess()
    {
        var disk = new TestDisk(true);
        await using var recorder = new PcapRecorder("test", disk);
        Assert.True(recorder.TryRecord(Packet(0), Source, Destination, Time));
        await disk.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await recorder.StopAsync();
        Assert.IsType<IOException>(recorder.Error);
        Assert.False(recorder.IsRecording);
        Assert.Equal(0, recorder.WrittenPackets);
        Assert.Equal(1, recorder.UnwrittenPackets);
        Assert.True(disk.Closed);
        Assert.Equal(24, disk.ToArray().Length);
    }

    [Fact]
    public async Task BoundedQueueReportsOverflowAndStopDrainsAcceptedPackets()
    {
        var disk = new TestDisk(false);
        await using var recorder = new PcapRecorder("test", disk);
        var packet = Packet(0);
        recorder.TryRecord(packet, Source, Destination, Time);
        await disk.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        int accepted = 1;
        try
        {
            for (int i = 0; i < 2050; i++)
                if (recorder.TryRecord(packet, Source, Destination, Time)) accepted++;
            Assert.True(recorder.DroppedPackets > 0);
            Assert.Equal(2051, accepted + recorder.DroppedPackets);
            var stopping = recorder.StopAsync();
            Assert.False(stopping.IsCompleted);
            Assert.False(recorder.TryRecord(packet, Source, Destination, Time));
            disk.Release.Set();
            await stopping;
            Assert.Equal(accepted, recorder.WrittenPackets);
            Assert.Equal(0, recorder.UnwrittenPackets);
            Assert.Null(recorder.Error);
        }
        finally { disk.Release.Set(); }
    }

    [Fact]
    public async Task LiveRecordsWithoutRenderingStopsAutomaticallyAndCanRecordAgain()
    {
        using var output = new Output();
        using var second = new Output();
        using var sender = new UdpClient();
        using var socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        int port = ((IPEndPoint)socket.Client.LocalEndPoint!).Port;
        socket.Dispose();
        var receiver = new Vlp16LiveReceiver();
        await Assert.ThrowsAsync<InvalidOperationException>(() => receiver.StartRecordingAsync(output.Path));
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var receiving = receiver.RunAsync(IPAddress.Any, port, stop.Token);
        try
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => receiver.StartRecordingAsync(System.IO.Path.GetTempPath().TrimEnd(System.IO.Path.DirectorySeparatorChar), true));
            Assert.Equal(LiveReceptionState.Listening, receiver.State);
            await receiver.StartRecordingAsync(output.Path);
            await Assert.ThrowsAsync<InvalidOperationException>(() => receiver.StartRecordingAsync(second.Path));
            await sender.SendAsync(new byte[512], new IPEndPoint(IPAddress.Loopback, port));
            for (int i = 0; i < 9; i++)
                await sender.SendAsync(Packet(i), new IPEndPoint(IPAddress.Loopback, port));
            while (receiver.ValidPackets < 9) await Task.Delay(5, stop.Token);
            await receiver.StopRecordingAsync();
            var recorder = receiver.Recording!;
            Assert.Equal(9, recorder.WrittenPackets);
            Assert.Equal(9, ReadRecords(output.Path).Count);
            Assert.Equal(IPAddress.Loopback.GetAddressBytes(), ReadRecords(output.Path)[0].Frame.AsSpan(30, 4).ToArray());
            Assert.Equal(port, BinaryPrimitives.ReadUInt16BigEndian(ReadRecords(output.Path)[0].Frame.AsSpan(36)));
            Assert.True(receiver.Frames.TryRead(out _)); // Renderowanie nie steruje zapisem.
            Assert.Equal(LiveReceptionState.Receiving, receiver.State);
            Assert.NotEmpty(Vlp16PcapReader.ReadFrames(output.Path));
            await receiver.StartRecordingAsync(second.Path);
            await sender.SendAsync(Packet(10), new IPEndPoint(IPAddress.Loopback, port));
            while (receiver.ValidPackets < 10) await Task.Delay(5, stop.Token);
        }
        finally { stop.Cancel(); await receiving; }
        Assert.False(receiver.Recording!.IsRecording);
        Assert.Equal(1, receiver.Recording.WrittenPackets);
        Assert.Single(ReadRecords(second.Path));
        using var exclusive = new FileStream(second.Path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }
}