namespace LidarViewer.Desktop;

public sealed class PlaybackClock
{
    private double mediaAnchor;
    private double realAnchor;
    private bool paused = true;
    public double Speed { get; private set; } = 1;

    public double Position(double realSeconds) =>
        mediaAnchor + (paused ? 0 : Math.Max(0, realSeconds - realAnchor) * Speed);

    public void Reset(double mediaSeconds, double realSeconds, bool isPaused)
    {
        mediaAnchor = mediaSeconds;
        realAnchor = realSeconds;
        paused = isPaused;
    }

    public void SetPaused(bool value, double realSeconds)
    {
        mediaAnchor = Position(realSeconds);
        realAnchor = realSeconds;
        paused = value;
    }

    public void SetSpeed(double speed, double realSeconds)
    {
        if (speed is not (0.5 or 1 or 2 or 4))
            throw new ArgumentOutOfRangeException(nameof(speed));
        mediaAnchor = Position(realSeconds);
        realAnchor = realSeconds;
        Speed = speed;
    }

    public double SecondsUntil(double mediaSeconds, double realSeconds) =>
        Math.Max(0, (mediaSeconds - Position(realSeconds)) / Speed);
}
