using System.Runtime.CompilerServices;

namespace LidarViewer.Core;

public sealed class SyntheticPointSource : IPointSource
{
    // Deterministyczny domek: podłoże, cztery ściany i dwuspadowy dach. Z jest osią pionową.
    public IReadOnlyList<Point3D> Generate()
    {
        var points = new List<Point3D>();
        for (int x = -40; x <= 40; x++)
        for (int y = -40; y <= 40; y++)
            points.Add(new(x / 5f, y / 5f, 0, 60));

        for (int u = -20; u <= 20; u++)
        for (int h = 1; h <= 30; h++)
        {
            float a = u / 10f, z = h / 10f;
            points.Add(new(a, -2, z, 180));
            points.Add(new(a, 2, z, 180));
            points.Add(new(-2, a, z, 180));
            points.Add(new(2, a, z, 180));
        }
        for (int x = -20; x <= 20; x++)
        for (int y = -20; y <= 20; y++)
            points.Add(new(x / 10f, y / 10f, 5 - Math.Abs(x / 10f), 220));

        return points.AsReadOnly();
    }

    public async IAsyncEnumerable<IReadOnlyList<Point3D>> ReadFramesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Statyczna scena jest powtarzana co 200 ms, by sprawdzić cykl Start/Stop.
        var frame = await Task.Run(Generate, cancellationToken).ConfigureAwait(false);
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(200));
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return frame;
        } while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false));
    }
}
