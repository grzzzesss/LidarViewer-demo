using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Threading.Channels;

namespace LidarViewer.Core;

public sealed class PcapRecorder : IAsyncDisposable
{
    private sealed record Packet(byte[] Payload, IPEndPoint Source, IPEndPoint Destination, DateTimeOffset Time);
    private readonly object gate = new();
    private readonly Channel<Packet> packets = Channel.CreateBounded<Packet>(new BoundedChannelOptions(2048)
    {
        FullMode = BoundedChannelFullMode.Wait, SingleReader = true, SingleWriter = false
    });
    private readonly Task writing;
    private readonly Stopwatch duration = Stopwatch.StartNew();
    private bool accepting = true;
    private long writtenPackets, writtenBytes = 24, droppedPackets, acceptedPackets;
    private Exception? error;

    public string Path { get; }
    public bool IsRecording { get { lock (gate) return accepting; } }
    public bool IsCompleted => writing.IsCompleted;
    public long WrittenPackets => Interlocked.Read(ref writtenPackets);
    public long WrittenBytes => Interlocked.Read(ref writtenBytes);
    public long DroppedPackets => Interlocked.Read(ref droppedPackets);
    public long UnwrittenPackets => Interlocked.Read(ref acceptedPackets) - WrittenPackets;
    public Exception? Error => Volatile.Read(ref error);
    public TimeSpan Duration { get { lock (gate) return duration.Elapsed; } }

    public PcapRecorder(string path, bool overwrite = false)
        : this(path, new FileStream(path, overwrite ? FileMode.Create : FileMode.CreateNew,
            FileAccess.Write, FileShare.Read, 65536, FileOptions.SequentialScan)) { }

    // Wewnętrzny konstruktor pozwala testować awarię i przeciążenie dysku.
    internal PcapRecorder(string path, Stream stream)
    {
        Path = path;
        try
        {
            var header = new byte[24];
            BinaryPrimitives.WriteUInt32LittleEndian(header, 0xa1b2c3d4);
            BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(4), 2);
            BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(6), 4);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(16), 65535);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(20), 1); // Ethernet, bez FCS.
            stream.Write(header);
            stream.Flush();
        }
        catch { stream.Dispose(); throw; }
        writing = Task.Run(() => WritePacketsAsync(stream));
    }

    // Kopia należy do kolejki: odbiornik może ponownie wykorzystać swój bufor.
    public bool TryRecord(ReadOnlySpan<byte> payload, IPEndPoint source, IPEndPoint destination, DateTimeOffset time)
    {
        if (payload.Length != 1206) throw new ArgumentException("Pakiet VLP-16 musi mieć 1206 bajtów.", nameof(payload));
        if (source.Address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork ||
            destination.Address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork ||
            destination.Address.Equals(IPAddress.Any))
            throw new ArgumentException("Do zapisu wymagane są rzeczywiste adresy IPv4 nadawcy i celu.");
        lock (gate)
        {
            if (!accepting) return false;
            if (!packets.Writer.TryWrite(new(payload.ToArray(),
                new(source.Address, source.Port), new(destination.Address, destination.Port), time)))
            {
                Interlocked.Increment(ref droppedPackets);
                return false;
            }
            Interlocked.Increment(ref acceptedPackets);
            return true;
        }
    }

    internal void Fail(Exception exception)
    {
        lock (gate)
        {
            Volatile.Write(ref error, exception);
            accepting = false;
            packets.Writer.TryComplete();
        }
    }

    public Task StopAsync()
    {
        lock (gate)
        {
            accepting = false;
            packets.Writer.TryComplete();
        }
        return writing;
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);

    private async Task WritePacketsAsync(Stream stream)
    {
        long lastMicroseconds = 0;
        try
        {
            await foreach (var packet in packets.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                // Czas odbioru UTC; cofnięcie zegara systemowego nie cofa osi nagrania.
                long microseconds = Math.Max(lastMicroseconds,
                    (packet.Time.UtcTicks - DateTimeOffset.UnixEpoch.UtcTicks) / 10);
                if (microseconds / 1_000_000 > uint.MaxValue)
                    throw new IOException("Czas pakietu wykracza poza zakres klasycznego PCAP.");
                lastMicroseconds = microseconds;
                var frame = BuildFrame(packet);
                var record = new byte[16];
                BinaryPrimitives.WriteUInt32LittleEndian(record, (uint)(microseconds / 1_000_000));
                BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(4), (uint)(microseconds % 1_000_000));
                BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(8), (uint)frame.Length);
                BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(12), (uint)frame.Length);
                long start = stream.Position;
                try
                {
                    stream.Write(record);
                    stream.Write(frame);
                }
                catch
                {
                    // W miarę możliwości usuń niedokończony rekord po błędzie dysku.
                    try { stream.SetLength(start); } catch (IOException) { }
                    throw;
                }
                Interlocked.Increment(ref writtenPackets);
                Interlocked.Add(ref writtenBytes, record.Length + frame.Length);
            }
            stream.Flush();
        }
        catch (Exception ex) { Volatile.Write(ref error, ex); }
        finally
        {
            lock (gate)
            {
                accepting = false;
                packets.Writer.TryComplete();
                duration.Stop();
            }
            while (packets.Reader.TryRead(out _)) { }
            try { stream.Dispose(); }
            catch (Exception ex) { Volatile.Write(ref error, ex); }
        }
    }

    private static byte[] BuildFrame(Packet packet)
    {
        var frame = new byte[1248];
        // Syntetyczne lokalnie administrowane MAC, nie adresy przechwycone z kabla.
        frame[0] = frame[6] = 0x02;
        frame[5] = 2;
        frame[11] = 1;
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(12), 0x0800);
        var ip = frame.AsSpan(14, 20);
        ip[0] = 0x45;
        BinaryPrimitives.WriteUInt16BigEndian(ip[2..], 1234);
        ip[8] = 64;
        ip[9] = 17;
        packet.Source.Address.GetAddressBytes().CopyTo(ip[12..]);
        packet.Destination.Address.GetAddressBytes().CopyTo(ip[16..]);
        BinaryPrimitives.WriteUInt16BigEndian(ip[10..], Checksum(ip));
        var udp = frame.AsSpan(34);
        BinaryPrimitives.WriteUInt16BigEndian(udp, (ushort)packet.Source.Port);
        BinaryPrimitives.WriteUInt16BigEndian(udp[2..], (ushort)packet.Destination.Port);
        BinaryPrimitives.WriteUInt16BigEndian(udp[4..], 1214);
        packet.Payload.CopyTo(udp[8..]);
        uint pseudoHeader = 17 + 1214u;
        for (int i = 12; i < 20; i += 2) pseudoHeader += BinaryPrimitives.ReadUInt16BigEndian(ip[i..]);
        ushort checksum = Checksum(udp, pseudoHeader);
        BinaryPrimitives.WriteUInt16BigEndian(udp[6..], checksum == 0 ? ushort.MaxValue : checksum);
        return frame;
    }

    private static ushort Checksum(ReadOnlySpan<byte> data, uint sum = 0)
    {
        for (int i = 0; i < data.Length; i += 2)
            sum += i + 1 < data.Length ? BinaryPrimitives.ReadUInt16BigEndian(data[i..]) : (uint)data[i] << 8;
        while (sum >> 16 != 0) sum = (sum & 65535) + (sum >> 16);
        return (ushort)~sum;
    }
}