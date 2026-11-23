using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;

namespace LidarViewer.Core;

public enum LiveReceptionState { Disconnected, Listening, Receiving, NoData, Error }

public sealed class Vlp16LiveReceiver
{
    private int running;
    private int listening;
    private long validPackets, rejectedPackets, startedAt, lastValidAt;
    private int lastPointCount;
    private string? lastSender;
    private Exception? error;
    private Channel<IReadOnlyList<Point3D>> frames = CreateChannel();

    private readonly SemaphoreSlim recordingGate = new(1, 1);
    private PcapRecorder? recording;
    public PcapRecorder? Recording => Volatile.Read(ref recording);

    public async Task StartRecordingAsync(string path, bool overwrite = false)
    {
        await recordingGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref listening) == 0)
                throw new InvalidOperationException("Najpierw uruchom odbiór Live.");
            if (Recording is { IsRecording: true })
                throw new InvalidOperationException("Nagrywanie już trwa.");
            if (Recording is { } previous) await previous.StopAsync().ConfigureAwait(false);
            var next = await Task.Run(() => new PcapRecorder(path, overwrite)).ConfigureAwait(false);
            if (Volatile.Read(ref listening) == 0)
            {
                await next.StopAsync().ConfigureAwait(false);
                throw new InvalidOperationException("Live został rozłączony podczas otwierania pliku.");
            }
            Volatile.Write(ref recording, next);
        }
        finally { recordingGate.Release(); }
    }

    public async Task StopRecordingAsync()
    {
        await recordingGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (Recording is { } current) await current.StopAsync().ConfigureAwait(false);
        }
        finally { recordingGate.Release(); }
    }

    public ChannelReader<IReadOnlyList<Point3D>> Frames => frames.Reader;
    public long ValidPackets => Interlocked.Read(ref validPackets);
    public long RejectedPackets => Interlocked.Read(ref rejectedPackets);
    public int LastPointCount => Volatile.Read(ref lastPointCount);
    public string? LastSender => Volatile.Read(ref lastSender);
    public LiveReceptionState State
    {
        get
        {
            if (Volatile.Read(ref listening) == 0)
                return error == null ? LiveReceptionState.Disconnected : LiveReceptionState.Error;
            long last = Interlocked.Read(ref lastValidAt);
            if (Environment.TickCount64 - (last == 0 ? Interlocked.Read(ref startedAt) : last) >= 3000)
                return LiveReceptionState.NoData;
            return last == 0 ? LiveReceptionState.Listening : LiveReceptionState.Receiving;
        }
    }

    private static Channel<IReadOnlyList<Point3D>> CreateChannel() =>
        Channel.CreateBounded<IReadOnlyList<Point3D>>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleWriter = true,
            SingleReader = true
        });

    public async Task RunAsync(IPAddress localAddress, int port, CancellationToken token)
    {
        if (port is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(port));
        if (localAddress.AddressFamily != AddressFamily.InterNetwork)
            throw new ArgumentException("Wybierz lokalny adres IPv4.", nameof(localAddress));
        if (Interlocked.CompareExchange(ref running, 1, 0) != 0)
            throw new InvalidOperationException("Odbiornik LIVE już działa.");

        frames = CreateChannel();
        Interlocked.Exchange(ref validPackets, 0);
        Interlocked.Exchange(ref rejectedPackets, 0);
        Interlocked.Exchange(ref lastValidAt, 0);
        Interlocked.Exchange(ref startedAt, Environment.TickCount64);
        Volatile.Write(ref lastPointCount, 0);
        Volatile.Write(ref lastSender, null);
        error = null;
        try
        {
            using var client = new UdpClient(AddressFamily.InterNetwork);
            client.Client.ExclusiveAddressUse = true;
            client.Client.ReceiveBufferSize = 4 * 1024 * 1024;
            client.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.PacketInformation, true);
            client.Client.Bind(new IPEndPoint(localAddress, port));
            Volatile.Write(ref listening, 1);
            var decoder = new Vlp16PacketDecoder();
            var buffer = new byte[65535];
            EndPoint sender = new IPEndPoint(IPAddress.Any, 0);
            var headerAngles = new int[12];
            while (true)
            {
                token.ThrowIfCancellationRequested();
                var datagram = await client.Client.ReceiveMessageFromAsync(buffer.AsMemory(),
                    SocketFlags.None, sender, token).ConfigureAwait(false);
                var receivedAt = DateTimeOffset.UtcNow;
                var payload = buffer.AsSpan(0, datagram.ReceivedBytes);
                // Surowy pakiet trafia do nagrania przed dekodowaniem i filtrowaniem.
                if (Recording is { IsRecording: true } recorder && payload.Length == 1206 &&
                    Vlp16PacketDecoder.ReadPacketHeader(payload, headerAngles, out _))
                {
                    try
                    {
                        recorder.TryRecord(payload, (IPEndPoint)datagram.RemoteEndPoint,
                            new IPEndPoint(datagram.PacketInformation.Address, port), receivedAt);
                    }
                    catch (ArgumentException ex)
                    {
                        recorder.Fail(new IOException("Nie odczytano adresów pakietu do nagrania.", ex));
                    }
                }
                if (!decoder.TryDecodePacket(payload, out var completed))
                {
                    Interlocked.Increment(ref rejectedPackets);
                    continue;
                }
                Interlocked.Increment(ref validPackets);
                Interlocked.Exchange(ref lastValidAt, Environment.TickCount64);
                Volatile.Write(ref lastSender, ((IPEndPoint)datagram.RemoteEndPoint).Address.ToString());
                foreach (var frame in completed)
                {
                    Volatile.Write(ref lastPointCount, frame.Count);
                    frames.Writer.TryWrite(frame);
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            error = ex;
            throw;
        }
        finally
        {
            Volatile.Write(ref listening, 0);
            await StopRecordingAsync().ConfigureAwait(false);
            frames.Writer.TryComplete(error);
            Volatile.Write(ref running, 0);
        }
    }
}
