using System.Windows.Controls;
using HelixToolkit.SharpDX;
using HelixToolkit.Wpf.SharpDX;
using HelixToolkit;
using System.Numerics;
using LidarPoint = LidarViewer.Core.Point3D;

namespace LidarViewer.Visualization;

public partial class PointCloudView : UserControl, IDisposable
{
    private readonly DefaultEffectsManager effects = new();
    private IReadOnlyList<LidarPoint>? displayed;
    public PointCloudView()
    {
        InitializeComponent();
        Viewport.EffectsManager = effects;
        BuildDistanceRings();
    }

    private IReadOnlyList<LidarPoint>? ringSource;
    private PointColorMode colorMode = PointColorMode.Distance;
    private int colorVersion;
    private int renderedColorVersion = -1;
    private bool heightRangeInitialized;
    private double minimumZ, maximumZ;

    public void ResetColorRange() => heightRangeInitialized = false;

    public void SetColorMode(PointColorMode mode)
    {
        Dispatcher.VerifyAccess();
        if (colorMode == mode) return;
        colorMode = mode;
        colorVersion++;
    }

    public void SetRingSource(IReadOnlyList<LidarPoint> points)
    {
        ringSource = points;
        if (heightRangeInitialized || points.Count == 0) return;
        minimumZ = points.Min(point => (double)point.Z);
        maximumZ = points.Max(point => (double)point.Z);
        heightRangeInitialized = true;
        colorVersion++;
    }

    public void ValidateMeasurementFilters(LidarViewer.Core.PointCloudFilterSettings settings)
    {
        if (trackedPoint is { } point && !LidarViewer.Core.PointCloudFilter.Matches(point, settings))
            ClearMeasurement();
    }

    public LidarPoint[] GetCloudSnapshot()
    {
        Dispatcher.VerifyAccess();
        return displayed?.ToArray() ?? Array.Empty<LidarPoint>();
    }

    public void ShowPoints(IReadOnlyList<LidarPoint> points)
    {
        Dispatcher.VerifyAccess();
        if (ReferenceEquals(displayed, points) && renderedColorVersion == colorVersion) return;
        ApplyPoints(points, PrepareGeometry(points, CancellationToken.None, colorMode, minimumZ, maximumZ));
    }

    public async Task<bool> ShowPointsAsync(IReadOnlyList<LidarPoint> points, CancellationToken token,
        Func<CancellationToken, Task<bool>>? beforeDisplay = null)
    {
        Dispatcher.VerifyAccess();
        if (ReferenceEquals(displayed, points) && renderedColorVersion == colorVersion) return true;

        int version = colorVersion;
        var mode = colorMode;
        double lowZ = minimumZ, highZ = maximumZ;
        var geometry = await Task.Run(() => PrepareGeometry(points, token, mode, lowZ, highZ), token);
        token.ThrowIfCancellationRequested();
        if (beforeDisplay != null && !await beforeDisplay(token)) return false;
        token.ThrowIfCancellationRequested();
        if (version != colorVersion) return false;
        ApplyPoints(points, geometry);
        return true;
    }

    private static PointGeometry3D PrepareGeometry(IReadOnlyList<LidarPoint> points, CancellationToken token,
        PointColorMode mode, double lowZ, double highZ)
    {
        var positions = new Vector3Collection(points.Count);
        var colors = new Color4Collection(points.Count);
        foreach (var point in points)
        {
            token.ThrowIfCancellationRequested();
            positions.Add(new Vector3(point.X, point.Y, point.Z));
            var color = PointColoring.GetColor(point, mode, lowZ, highZ);
            colors.Add(new HelixToolkit.Maths.Color4(color.X, color.Y, color.Z, color.W));
        }
        return new PointGeometry3D { Positions = positions, Colors = colors };
    }

    private void ApplyPoints(IReadOnlyList<LidarPoint> points, PointGeometry3D geometry)
    {
        Cloud.Color = System.Windows.Media.Colors.White;
        Cloud.Geometry = geometry;
        displayed = points;
        renderedColorVersion = colorVersion;
        UpdateTracking(points);
    }


    public event Action<LidarPoint?>? MeasurementChanged;
    private bool measurementEnabled;
    private LidarPoint? trackedPoint;

    public void SetMeasurementEnabled(bool enabled)
    {
        measurementEnabled = enabled;
        Viewport.Cursor = enabled ? System.Windows.Input.Cursors.Cross : System.Windows.Input.Cursors.Arrow;
        ClearMeasurement();
    }

    public void ClearSelection() => ClearMeasurement();

    private void ClearMeasurement()
    {
        trackedPoint = null;
        SelectedPoint.Geometry = null;
        MeasurementLine.Geometry = null;
        MeasurementChanged?.Invoke(null);
    }

    private void Viewport_PreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        // PPM, kółko i kombinacje z klawiszami pozostają dostępne dla kamery.
        if (!measurementEnabled || e.ChangedButton != System.Windows.Input.MouseButton.Left ||
            System.Windows.Input.Keyboard.Modifiers != System.Windows.Input.ModifierKeys.None ||
            e.RightButton == System.Windows.Input.MouseButtonState.Pressed ||
            e.MiddleButton == System.Windows.Input.MouseButtonState.Pressed)
            return;

        e.Handled = true;
        if (displayed == null) return;

        var hit = Viewport.FindHits(e.GetPosition(Viewport))
            .FirstOrDefault(result => result.IsValid && ReferenceEquals(result.Geometry, Cloud.Geometry));
        if (hit?.Tag is not int index || index < 0 || index >= displayed.Count)
            return;

        ShowMeasurement(displayed[index]);
    }

    private void ShowMeasurement(LidarPoint point)
    {
        trackedPoint = point;
        SelectedPoint.Geometry = new PointGeometry3D
        {
            Positions = new Vector3Collection { new Vector3(point.X, point.Y, point.Z) }
        };
        MeasurementLine.Geometry = new LineGeometry3D
        {
            Positions = new Vector3Collection
            {
                Vector3.Zero,
                new Vector3(point.X, point.Y, point.Z)
            },
            Indices = new IntCollection { 0, 1 }
        };
        MeasurementChanged?.Invoke(point);
    }


    private void UpdateTracking(IReadOnlyList<LidarPoint> points)
    {
        if (!measurementEnabled || trackedPoint is not { } previous) return;

        // Dopasowanie przestrzenne między kolejnymi klatkami, maksymalnie 2 m.
        double nearestSquared = 4;
        LidarPoint? nearest = null;
        foreach (var point in points)
        {
            double dx = (double)point.X - previous.X;
            double dy = (double)point.Y - previous.Y;
            double dz = (double)point.Z - previous.Z;
            double squared = dx * dx + dy * dy + dz * dz;
            if (squared > nearestSquared) continue;

            nearestSquared = squared;
            nearest = point;
        }

        if (nearest is { } matched)
            ShowMeasurement(matched);
        else
            ClearMeasurement();
    }

    private double ringSceneRadius = 10;
    private int ringSpacing = 1;

    public void SetDistanceRings(bool visible, int spacing)
    {
        Dispatcher.VerifyAccess();
        DistanceRingModel.Visibility = visible ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        if (spacing == ringSpacing) return;
        ringSpacing = spacing;
        BuildDistanceRings();
    }

    private void UpdateRingScene()
    {
        double radius = DistanceRings.SceneRadius(ringSource ?? displayed ?? Array.Empty<LidarPoint>());
        if (DistanceRings.Radii(radius, ringSpacing).Length ==
            DistanceRings.Radii(ringSceneRadius, ringSpacing).Length)
        {
            ringSceneRadius = radius;
            return;
        }
        ringSceneRadius = radius;
        BuildDistanceRings();
    }

    private void BuildDistanceRings()
    {
        var positions = new Vector3Collection();
        var indices = new IntCollection();
        foreach (int radius in DistanceRings.Radii(ringSceneRadius, ringSpacing))
        {
            int start = positions.Count;
            foreach (var point in DistanceRings.Circle(radius)) positions.Add(point);
            for (int segment = 0; segment < DistanceRings.SegmentsPerCircle; segment++)
            {
                indices.Add(start + segment);
                indices.Add(start + segment + 1);
            }
        }
        DistanceRingModel.Geometry = new LineGeometry3D { Positions = positions, Indices = indices };
    }


    private string currentView = "3D";

    public void SetView(string view)
    {
        Dispatcher.VerifyAccess();
        if (view is not ("3D" or "XY" or "XZ" or "YZ"))
            throw new ArgumentOutOfRangeException(nameof(view));

        currentView = view;
        ApplyViewCamera();
        FitCameraToCloud();
    }

    private void ApplyViewCamera()
    {
        bool perspective = currentView == "3D";
        Viewport.IsRotationEnabled = perspective;
        Viewport.IsChangeFieldOfViewEnabled = perspective;
        Viewport.ShowViewCube = perspective;

        if (perspective)
        {
            Viewport.Camera = new PerspectiveCamera
            {
                Position = new System.Windows.Media.Media3D.Point3D(12, -16, 12),
                LookDirection = new System.Windows.Media.Media3D.Vector3D(-12, 16, -12),
                UpDirection = new System.Windows.Media.Media3D.Vector3D(0, 0, 1),
                NearPlaneDistance = 0.1,
                FarPlaneDistance = 1000
            };
            return;
        }

        var direction = currentView switch
        {
            "XY" => new System.Windows.Media.Media3D.Vector3D(0, 0, -1),
            "XZ" => new System.Windows.Media.Media3D.Vector3D(0, 1, 0),
            _ => new System.Windows.Media.Media3D.Vector3D(-1, 0, 0)
        };
        var up = currentView == "XY"
            ? new System.Windows.Media.Media3D.Vector3D(0, 1, 0)
            : new System.Windows.Media.Media3D.Vector3D(0, 0, 1);

        Viewport.Camera = new OrthographicCamera
        {
            Position = new System.Windows.Media.Media3D.Point3D(-direction.X * 30, -direction.Y * 30, -direction.Z * 30),
            LookDirection = direction * 30,
            UpDirection = up,
            Width = 20,
            NearPlaneDistance = 0.01,
            FarPlaneDistance = 1000
        };
    }

    private void FitCameraToCloud()
    {
        // Dopasowanie obejmuje tylko punkty, bez okręgów i znacznika pomiaru.
        if (displayed is not { Count: > 0 }) return;
        var bounds = System.Windows.Media.Media3D.Rect3D.Empty;
        foreach (var point in displayed)
            bounds.Union(new System.Windows.Media.Media3D.Point3D(point.X, point.Y, point.Z));
        double size = Math.Max(1, Math.Max(bounds.SizeX, Math.Max(bounds.SizeY, bounds.SizeZ)));
        if (Viewport.Camera is ProjectionCamera camera)
            camera.FarPlaneDistance = Math.Max(1000, size * 20);
        Viewport.ZoomExtents(bounds, 0);
    }


    public void ShowLidarView()
    {
        UpdateRingScene();
        ClearMeasurement();
        ApplyViewCamera();
        if (currentView != "3D") FitCameraToCloud();
    }

    public void FitToCloud()
    {
        ClearMeasurement();
        UpdateRingScene();
        ApplyViewCamera();
        FitCameraToCloud();
    }

    public void Dispose()
    {
        Viewport.Dispose();
        effects.Dispose();
    }
}
