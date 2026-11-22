namespace LidarViewer.Core;

// Strumień kompletnych chmur. Przyszłe źródła mogą dekodować UDP lub PCAP.
public interface IPointSource
{
    IAsyncEnumerable<IReadOnlyList<Point3D>> ReadFramesAsync(CancellationToken cancellationToken = default);
}
