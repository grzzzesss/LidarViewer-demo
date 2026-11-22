using System.Net;
using System.Net.Sockets;

namespace LidarViewer.Core;

// Liczy datagramy. Nie dekoduje pakietów VLP-16 i nie jest źródłem punktów.
public sealed class UdpPacketCounter
{
    private long count;
    private int running;
    public long DatagramCount => Interlocked.Read(ref count);

    public async Task ListenAsync(int port, CancellationToken cancellationToken)
    {
        if (port is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(port));
        if (Interlocked.CompareExchange(ref running, 1, 0) != 0)
            throw new InvalidOperationException("Nasłuch już działa.");
        try
        {
            Interlocked.Exchange(ref count, 0);
            using var client = new UdpClient(new IPEndPoint(IPAddress.Any, port));
            while (true)
            {
                await client.ReceiveAsync(cancellationToken).ConfigureAwait(false);
                Interlocked.Increment(ref count);
            }
        }
        finally { Volatile.Write(ref running, 0); }
    }
}
