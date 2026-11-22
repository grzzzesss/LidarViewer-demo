using System.Net;
using System.Net.Sockets;
using LidarViewer.Core;
using Xunit;

namespace LidarViewer.Core.Tests;

public sealed class Vlp16LiveReceiverTests
{
    private static byte[] Packet(int index, bool dual = false)
    {
        using var file = new Vlp16PcapReaderTests.TestCapture(true);
        file.WritePacket(index, dual, index);
        return File.ReadAllBytes(file.Path).AsSpan(24 + 16 + 42, 1206).ToArray();
    }

    private static int FreePort()
    {
        using var socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)socket.Client.LocalEndPoint!).Port;
    }

    [Theory]
    [InlineData(false, 6)]
    [InlineData(true, 12)]
    public void LiveDecoderMatchesCompletePcapFrames(bool dual, int packetCount)
    {
        using var file = new Vlp16PcapReaderTests.TestCapture(true);
        var decoder = new Vlp16PacketDecoder();
        var completed = new List<IReadOnlyList<Point3D>>();
        for (int i = 0; i < packetCount; i++)
        {
            file.WritePacket(i, dual, i);
            Assert.True(decoder.TryDecodePacket(Packet(i, dual), out var frames));
            completed.AddRange(frames);
        }
        var expected = Vlp16PcapReader.ReadFrames(file.Path).ToArray();
        Assert.Equal(expected.Length - 1, completed.Count);
        for (int i = 0; i < completed.Count; i++)
            Assert.Equal(expected[i].ToArray(), completed[i].ToArray());
        Assert.Equal(77, completed[0][0].Intensity);
        Assert.InRange(completed[0][0].X, -0.403f, -0.401f);
        Assert.InRange(completed[0][0].Z, -0.519f, -0.517f);
    }

    [Fact]
    public void RejectsMalformedPacketsWithoutChangingCurrentFrame()
    {
        var decoder = new Vlp16PacketDecoder();
        Assert.True(decoder.TryDecodePacket(Packet(0), out var first));
        Assert.Empty(first);
        Assert.False(decoder.TryDecodePacket(new byte[10], out _));
        var malformed = Packet(1);
        malformed[100] = 0;
        Assert.False(decoder.TryDecodePacket(malformed, out _));
        Assert.True(decoder.TryDecodePacket(Packet(1), out _));
        Assert.True(decoder.TryDecodePacket(Packet(2), out var completed));
        Assert.Equal(30, Assert.Single(completed).Count);
    }

    [Fact]
    public async Task ReceivesCountsAndRestartsOnSamePort()
    {
        int port = FreePort();
        var receiver = new Vlp16LiveReceiver();
        using var sender = new UdpClient();
        for (int run = 0; run < 2; run++)
        {
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var receiving = receiver.RunAsync(IPAddress.Loopback, port, cancellation.Token);
            try
            {
                Assert.Equal(LiveReceptionState.Listening, receiver.State);
                await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    receiver.RunAsync(IPAddress.Loopback, port, cancellation.Token));
                await sender.SendAsync(new byte[3], new IPEndPoint(IPAddress.Loopback, port));
                await sender.SendAsync(new byte[1206], new IPEndPoint(IPAddress.Loopback, port));
                for (int i = 0; i < 3; i++)
                {
                    await sender.SendAsync(Packet(i), new IPEndPoint(IPAddress.Loopback, port));
                    await Task.Delay(5, cancellation.Token);
                }
                var frame = await receiver.Frames.ReadAsync(cancellation.Token);
                Assert.Equal(30, frame.Count);
                Assert.Equal(3, receiver.ValidPackets);
                Assert.Equal(2, receiver.RejectedPackets);
                Assert.Equal(30, receiver.LastPointCount);
                Assert.Equal("127.0.0.1", receiver.LastSender);
                Assert.Equal(LiveReceptionState.Receiving, receiver.State);
            }
            finally
            {
                cancellation.Cancel();
                await receiving.WaitAsync(TimeSpan.FromSeconds(2));
            }
            Assert.Equal(LiveReceptionState.Disconnected, receiver.State);
        }
    }

    [Fact]
    public async Task CancelWithoutIncomingPacketsReleasesPort()
    {
        int port = FreePort();
        var receiver = new Vlp16LiveReceiver();
        using var cancellation = new CancellationTokenSource();
        var receiving = receiver.RunAsync(IPAddress.Loopback, port, cancellation.Token);
        cancellation.Cancel();
        await receiving.WaitAsync(TimeSpan.FromSeconds(2));
        using var rebound = new UdpClient(new IPEndPoint(IPAddress.Loopback, port));
        Assert.Equal(LiveReceptionState.Disconnected, receiver.State);
    }

    [Fact]
    public async Task OccupiedPortProducesError()
    {
        using var occupied = new UdpClient(AddressFamily.InterNetwork);
        occupied.Client.ExclusiveAddressUse = true;
        occupied.Client.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        int port = ((IPEndPoint)occupied.Client.LocalEndPoint!).Port;
        var receiver = new Vlp16LiveReceiver();
        await Assert.ThrowsAsync<SocketException>(() =>
            receiver.RunAsync(IPAddress.Loopback, port, CancellationToken.None));
        Assert.Equal(LiveReceptionState.Error, receiver.State);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65536)]
    public async Task InvalidPortsAreRejected(int port) =>
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            new Vlp16LiveReceiver().RunAsync(IPAddress.Loopback, port, CancellationToken.None));

    [Fact]
    public async Task RenderingQueueKeepsOnlyNewestCompleteFrame()
    {
        int port = FreePort();
        var receiver = new Vlp16LiveReceiver();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var sender = new UdpClient();
        var receiving = receiver.RunAsync(IPAddress.Loopback, port, cancellation.Token);
        try
        {
            for (int i = 0; i < 9; i++)
            {
                await sender.SendAsync(Packet(i), new IPEndPoint(IPAddress.Loopback, port));
                await Task.Delay(5, cancellation.Token);
            }
            while (receiver.ValidPackets < 9) await Task.Delay(5, cancellation.Token);
            Assert.True(receiver.Frames.TryRead(out var latest));
            Assert.Equal(30, latest.Count);
            Assert.False(receiver.Frames.TryRead(out _));
            var decoder = new Vlp16PacketDecoder();
            IReadOnlyList<Point3D>? expected = null;
            for (int i = 0; i < 9; i++)
            {
                decoder.TryDecodePacket(Packet(i), out var frames);
                if (frames.Count > 0) expected = frames[^1];
            }
            Assert.Equal(expected!.ToArray(), latest.ToArray());
        }
        finally
        {
            cancellation.Cancel();
            await receiving;
        }
    }
}
