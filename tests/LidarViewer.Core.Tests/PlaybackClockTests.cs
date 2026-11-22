using LidarViewer.Desktop;
using Xunit;

namespace LidarViewer.Core.Tests;

public sealed class PlaybackClockTests
{
    [Theory]
    [InlineData(0.5, 0.2)]
    [InlineData(1, 0.1)]
    [InlineData(2, 0.05)]
    [InlineData(4, 0.025)]
    public void ScalesFrameInterval(double speed, double expected)
    {
        var clock = new PlaybackClock();
        clock.SetSpeed(speed, 0);
        clock.Reset(0, 0, false);
        Assert.Equal(expected, clock.SecondsUntil(0.1, 0), 8);
    }

    [Fact]
    public void SpeedChangePreservesCurrentMediaPosition()
    {
        var clock = new PlaybackClock();
        clock.Reset(0, 0, false);
        Assert.Equal(2, clock.Position(2));
        clock.SetSpeed(2, 2);
        Assert.Equal(2, clock.Position(2));
        Assert.Equal(4, clock.Position(3));
        clock.SetSpeed(1, 3);
        Assert.Equal(5, clock.Position(4));
    }

    [Fact]
    public void SlowdownPreservesPositionAndPauseDoesNotAdvanceTime()
    {
        var clock = new PlaybackClock();
        clock.SetSpeed(4, 0);
        clock.Reset(0, 0, false);
        clock.SetSpeed(0.5, 1);
        Assert.Equal(4, clock.Position(1));
        Assert.Equal(5, clock.Position(3));
        clock.SetPaused(true, 3);
        clock.SetSpeed(2, 30);
        Assert.Equal(5, clock.Position(100));
        clock.SetPaused(false, 100);
        Assert.Equal(7, clock.Position(101));
    }

    [Fact]
    public void IrregularTimesAndDecodeCostUseAbsoluteSchedule()
    {
        var clock = new PlaybackClock();
        clock.Reset(0, 0, false);
        Assert.Equal(0.07, clock.SecondsUntil(0.1, 0.03), 8);
        Assert.Equal(0.24, clock.SecondsUntil(0.35, 0.11), 8);
        Assert.Equal(0, clock.SecondsUntil(0.35, 0.5));
        Assert.Equal(0.000001, clock.SecondsUntil(0.000001, 0), 8);
    }

    [Fact]
    public void SeekingReanchorsClockAndRetainsSpeed()
    {
        var clock = new PlaybackClock();
        clock.SetSpeed(4, 0);
        clock.Reset(20, 10, true);
        Assert.Equal(20, clock.Position(100));
        Assert.Equal(4, clock.Speed);
        clock.SetPaused(false, 100);
        Assert.Equal(24, clock.Position(101));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void RejectsUnsupportedSpeed(double speed) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new PlaybackClock().SetSpeed(speed, 0));
}
