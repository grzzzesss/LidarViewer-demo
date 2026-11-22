using System.Net;
using System.Net.Sockets;
using LidarViewer.Core;
using Xunit;

namespace LidarViewer.Core.Tests;

public class CoreTests
{
    [Fact]
    public void GeneratorProducesRepeatableHouseWithGroundAndRoof()
    {
        var source = new SyntheticPointSource();
        var first = source.Generate();
        Assert.Equal(first.ToArray(), source.Generate().ToArray());
        Assert.Equal(13162, first.Count);
        Assert.Contains(first, p => p.Z == 0 && p.X == -8);
        Assert.Contains(first, p => p.Z == 5 && p.X == 0);
        Assert.All(first, p => Assert.True(float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z)));
        Assert.All(first, p => Assert.InRange(p.Z, 0, 5));
    }

    [Fact]
    public async Task SimulationHonorsCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        await using var frames = new SyntheticPointSource().ReadFramesAsync(cancellation.Token).GetAsyncEnumerator();
        Assert.True(await frames.MoveNextAsync());
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await frames.MoveNextAsync());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65536)]
    public async Task InvalidPortsAreRejected(int port)
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => new UdpPacketCounter().ListenAsync(port, default));
    }

    [Fact]
    public async Task UdpCountsDatagramsStopsAndRestarts()
    {
        using var sender = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        using var reservation = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        int port = ((IPEndPoint)reservation.Client.LocalEndPoint!).Port;
        reservation.Dispose();
        var counter = new UdpPacketCounter();
        for (int run = 0; run < 2; run++)
        {
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var listening = counter.ListenAsync(port, cancellation.Token);
            try
            {
                Assert.Equal(0, counter.DatagramCount);
                await Assert.ThrowsAsync<InvalidOperationException>(() => counter.ListenAsync(port, default));
                for (int i = 0; i < 3; i++)
                    await sender.SendAsync(new byte[] { 1, 2, 3 }, new IPEndPoint(IPAddress.Loopback, port));
                while (counter.DatagramCount < 3) await Task.Delay(10, cancellation.Token);
                Assert.Equal(3, counter.DatagramCount);
            }
            finally
            {
                cancellation.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => listening);
            }
        }
    }

    [Fact]
    public async Task OccupiedPortReportsSocketError()
    {
        using var occupied = new UdpClient();
        occupied.Client.ExclusiveAddressUse = true;
        occupied.Client.Bind(new IPEndPoint(IPAddress.Any, 0));
        int port = ((IPEndPoint)occupied.Client.LocalEndPoint!).Port;
        await Assert.ThrowsAsync<SocketException>(() => new UdpPacketCounter().ListenAsync(port, default));
    }
}
