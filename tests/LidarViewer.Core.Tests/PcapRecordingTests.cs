using LidarViewer.Core;
using Xunit;

namespace LidarViewer.Core.Tests;

public sealed class PcapRecordingTests
{
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void IndexedFramesMatchSequentialDecoderIncludingInternalPacketBoundary(bool little, bool dual)
    {
        using var file = new Vlp16PcapReaderTests.TestCapture(little);
        for (int i = 0; i < (dual ? 12 : 6); i++) file.WritePacket(i, dual, 10 + i * 0.25 + i * i * 0.1, dual ? 1400 : 1200);
        var recording = PcapRecording.Open(file.Path);
        var sequential = Vlp16PcapReader.ReadFrames(file.Path).ToArray();

        Assert.Equal(3, recording.Frames.Count);
        Assert.Equal(sequential.Length, recording.Frames.Count);
        Assert.True(recording.Frames[1].Start.Block > 0);
        foreach (int index in new[] { 0, 2, 1, 0, 2 })
            Assert.Equal(sequential[index].ToArray(), recording.ReadFrame(index).ToArray());
        Assert.Equal(sequential.Sum(frame => frame.Count),
            Enumerable.Range(0, recording.Frames.Count).Sum(i => recording.ReadFrame(i).Count));
        Assert.Throws<ArgumentOutOfRangeException>(() => recording.ReadFrame(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => recording.ReadFrame(3));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SupportsNanosecondTimestampPrecision(bool little)
    {
        using var file = new Vlp16PcapReaderTests.TestCapture(little, true);
        for (int i = 0; i < 6; i++) file.WritePacket(i, false, 10 + i * 0.25);
        var recording = PcapRecording.Open(file.Path);
        Assert.InRange(recording.Frames[1].Time.TotalSeconds, 0.5006, 0.5008);
        Assert.False(recording.HasTimestampWarnings);
    }

    [Fact]
    public void UsesIrregularPacketTimesAndNearestFrame()
    {
        using var file = new Vlp16PcapReaderTests.TestCapture(true);
        for (int i = 0; i < 6; i++) file.WritePacket(i, false, 10 + i * 0.25 + i * i * 0.1);
        var recording = PcapRecording.Open(file.Path);
        Assert.Equal(TimeSpan.Zero, recording.Frames[0].Time);
        Assert.InRange(recording.Frames[1].Time.TotalSeconds, 0.9006, 0.9008);
        Assert.InRange(recording.Frames[2].Time.TotalSeconds, 3.7499, 3.7501);
        Assert.True(recording.Duration > recording.Frames[^1].Time);
        Assert.False(recording.HasTimestampWarnings);
        Assert.Equal(0, recording.FindFrame(TimeSpan.FromSeconds(-1)));
        Assert.Equal(1, recording.FindFrame(TimeSpan.FromSeconds(1)));
        Assert.Equal(2, recording.FindFrame(recording.Duration));
        Assert.Equal(2, recording.FindFrame(TimeSpan.FromDays(1)));
    }

    [Fact]
    public void RegressingTimesProduceMonotonicTimelineAndWarning()
    {
        using var file = new Vlp16PcapReaderTests.TestCapture(true);
        for (int i = 0; i < 6; i++) file.WritePacket(i, false);
        var recording = PcapRecording.Open(file.Path);
        Assert.True(recording.HasTimestampWarnings);
        Assert.True(recording.Frames[1].Time > recording.Frames[0].Time);
        Assert.True(recording.Frames[2].Time > recording.Frames[1].Time);
    }

    [Fact]
    public void EmptyTruncatedAndMeasurementFreeCapturesAreRejected()
    {
        string empty = Path.GetTempFileName();
        try { Assert.Throws<EndOfStreamException>(() => PcapRecording.Open(empty)); }
        finally { File.Delete(empty); }

        using var file = new Vlp16PcapReaderTests.TestCapture(true);
        Assert.Throws<InvalidDataException>(() => PcapRecording.Open(file.Path));
        file.WritePacket(0, false);
        using (var stream = File.OpenWrite(file.Path)) stream.SetLength(stream.Length - 1);
        Assert.Throws<EndOfStreamException>(() => PcapRecording.Open(file.Path));
    }

    [Fact]
    public void IndexingAndFrameReadingCanBeCancelled()
    {
        using var file = new Vlp16PcapReaderTests.TestCapture(true);
        file.WritePacket(0, false);
        var recording = PcapRecording.Open(file.Path);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => PcapRecording.Open(file.Path, cancellation.Token));
        Assert.Throws<OperationCanceledException>(() => recording.ReadFrame(0, cancellation.Token));
    }

    [Fact]
    public void FilterAndExportWorkForIndependentlyReadFrame()
    {
        using var file = new Vlp16PcapReaderTests.TestCapture(true);
        for (int i = 0; i < 6; i++) file.WritePacket(i, false, i);
        var recording = PcapRecording.Open(file.Path);
        var points = recording.ReadFrame(1);
        var visible = PointCloudFilter.Apply(points, new()
        {
            DistanceEnabled = true, MaximumDistance = 3,
            HeightEnabled = true, MinimumHeight = -1, MaximumHeight = 0,
            IntensityEnabled = true, MinimumIntensity = 77, MaximumIntensity = 77
        });
        Assert.NotEmpty(visible);
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".ply");
        try
        {
            PointCloudFileWriter.Write(path, visible);
            Assert.Equal(visible.ToArray(), PointCloudFileReader.Read(path).ToArray());
        }
        finally { File.Delete(path); }
    }
}
