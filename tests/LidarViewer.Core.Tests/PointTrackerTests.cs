using LidarViewer.Core;
using Xunit;

namespace LidarViewer.Core.Tests;

public sealed class PointTrackerTests
{
    private static Point3D[] Object(float x) => Enumerable.Range(0, 24)
        .Select(i => new Point3D(x + (i % 4) * .08f, (i / 4) * .06f, (i * i % 7) * .035f, 120)).ToArray();

    [Fact]
    public void FollowsMovingSurfaceInsteadOfStationaryPointAtOldPosition()
    {
        var cloud = Object(3);
        var tracker = new PointTracker();
        tracker.Select(cloud[0], cloud);
        for (int frame = 1; frame <= 12; frame++)
        {
            float x = 3 + frame * .12f;
            var moving = Object(x);
            var current = moving.Append(cloud[0]).ToArray();
            tracker.Update(current);
            Assert.False(tracker.IsLost);
            Assert.NotNull(tracker.Position);
            Assert.Contains(tracker.Position!.Value, moving);
            Assert.InRange(tracker.Position.Value.X, x - .03f, x + .09f);
        }
    }

    [Fact]
    public void NeverHoldsOldCoordinatesWhenThereIsNoCurrentMeasurement()
    {
        var tracker = new PointTracker();
        var cloud = Object(3);
        tracker.Select(cloud[0], cloud);
        tracker.Update(Array.Empty<Point3D>());
        Assert.True(tracker.IsLost);
        Assert.Null(tracker.Position);
        tracker.Update(cloud);
        Assert.Null(tracker.Position);
        tracker.Select(cloud[0], cloud);
        Assert.False(tracker.IsLost);
    }

    [Fact]
    public void RejectsUnrelatedSinglePointInsteadOfPretendingSurfaceWasFound()
    {
        var tracker = new PointTracker();
        var cloud = Object(3);
        tracker.Select(cloud[0], cloud);
        tracker.Update(new[] { new Point3D(3, 0, 0, 50) });
        Assert.True(tracker.IsLost);
    }

    [Fact]
    public void StaticSurfaceDoesNotMoveAndClearReleasesSelection()
    {
        var tracker = new PointTracker();
        var cloud = Object(3);
        tracker.Select(cloud[0], cloud);
        for (int i = 0; i < 5; i++) tracker.Update(cloud);
        Assert.Equal(cloud[0], tracker.Position);
        tracker.Clear();
        Assert.Null(tracker.Position);
        Assert.False(tracker.IsLost);
    }

    [Fact]
    public void SparseCloudUsesBoundedFallbackAndRejectsDistantJump()
    {
        var tracker = new PointTracker();
        tracker.Select(new(2, 0, 0, 0));
        tracker.Update(new[] { new Point3D(2.1f, 0, 0, 0) });
        Assert.Equal(2.1f, tracker.Position!.Value.X);
        tracker.Update(new[] { new Point3D(4, 0, 0, 0) });
        Assert.True(tracker.IsLost);
    }
    [Fact]
    public void RecordedMovingSurfaceFromFrame72FollowsThroughEndOfDemo()
    {
        var recording = PcapRecording.Open(Path.Combine(AppContext.BaseDirectory, "samples", "demo.pcap"));
        var cloud = recording.ReadFrame(71); // GUI numeruje klatki od 1.
        var selected = cloud.OrderBy(p => Math.Pow(p.X - 3.5, 2) +
            Math.Pow(p.Y + .4, 2) + Math.Pow(p.Z + .7, 2)).First();
        var tracker = new PointTracker();
        tracker.Select(selected, cloud);
        for (int frame = 72; frame < recording.Frames.Count; frame++)
        {
            var current = recording.ReadFrame(frame);
            var previous = tracker.Position!.Value;
            tracker.Update(current);
            Assert.False(tracker.IsLost, $"Utrata w klatce {frame + 1}");
            Assert.NotNull(tracker.Position);
            Assert.Contains(tracker.Position.Value, current);
            Assert.True(tracker.Position.Value.Y < previous.Y);
            Assert.InRange(tracker.Position.Value.X, 2, 5);
        }
        Assert.True(tracker.Position!.Value.Y < -20);
    }}