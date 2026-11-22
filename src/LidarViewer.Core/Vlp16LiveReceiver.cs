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
            client.Client.Bind(new IPEndPoint(localAddress, port));
            Volatile.Write(ref listening, 1);
            var decoder = new Vlp16PacketDecoder();
            while (true)
            {
                token.ThrowIfCancellationRequested();
                var datagram = await client.ReceiveAsync(token).ConfigureAwait(false);
                if (!decoder.TryDecodePacket(datagram.Buffer, out var completed))
                {
                    Interlocked.Increment(ref rejectedPackets);
                    continue;
                }
                Interlocked.Increment(ref validPackets);
                Interlocked.Exchange(ref lastValidAt, Environment.TickCount64);
                Volatile.Write(ref lastSender, datagram.RemoteEndPoint.Address.ToString());
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
            frames.Writer.TryComplete(error);
            Volatile.Write(ref running, 0);
        }
    }
}
