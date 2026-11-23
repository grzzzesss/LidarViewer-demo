using System.ComponentModel;
using System.Windows;
using System.IO;
using Microsoft.Win32;
using LidarViewer.Core;

namespace LidarViewer.Desktop;

public partial class MainWindow : Window
{
    private CancellationTokenSource? simulationCancellation;
    private Task simulationTask = Task.CompletedTask;
    private CancellationTokenSource? captureCancellation;
    private Task captureTask = Task.CompletedTask;
    private CancellationTokenSource? fileCancellation;
    private Task fileTask = Task.CompletedTask;
    private bool closing, allowClose;

    public MainWindow()
    {
        InitializeComponent();
        Scene.MeasurementChanged += ShowMeasurement;
        liveStatusTimer.Tick += (_, _) => RefreshLiveStatus();
        LiveAddress.Items.Add("0.0.0.0");
        try
        {
            var addresses = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
                .SelectMany(network => network.GetIPProperties().UnicastAddresses)
                .Select(entry => entry.Address)
                .Where(address => address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                .Distinct();
            foreach (var address in addresses) LiveAddress.Items.Add(address.ToString());
        }
        catch (System.Net.NetworkInformation.NetworkInformationException ex)
        {
            LiveError.Text = $"Nie odczytano listy interfejsów: {ex.Message}. Wpisz lokalny IPv4 ręcznie.";
        }
        ContentRendered += OpenStartupCapture;
    }

    private CancellationTokenSource? liveCancellation;
    private Task liveTask = Task.CompletedTask;
    private Vlp16LiveReceiver? liveReceiver;
    private readonly System.Windows.Threading.DispatcherTimer liveStatusTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(250)
    };

    private bool recordingBusy;
    private string? recordingStartError;
    private Task recordingTask = Task.CompletedTask;
    private PcapRecorder? lastRecording;

    private void StartRecording_Click(object sender, RoutedEventArgs e)
    {
        var receiver = liveReceiver;
        if (recordingBusy || closing || receiver == null || receiver.Recording is { IsRecording: true }) return;
        var dialog = new SaveFileDialog
        {
            Title = "Nagraj dane Live do PCAP", Filter = "Nagranie PCAP (*.pcap)|*.pcap",
            DefaultExt = ".pcap", AddExtension = true, OverwritePrompt = true,
            FileName = $"VLP16_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.pcap"
        };
        if (dialog.ShowDialog(this) != true || closing || receiver != liveReceiver) return;
        recordingBusy = true;
        StartRecording.IsEnabled = StopRecording.IsEnabled = false;
        recordingStartError = null;
        recordingTask = StartRecordingAsync(receiver, dialog.FileName);
    }

    private async Task StartRecordingAsync(Vlp16LiveReceiver receiver, string path)
    {
        try
        {
            // SaveFileDialog potwierdził nadpisanie, otwarcie pliku odbywa się poza UI.
            await receiver.StartRecordingAsync(path, overwrite: true);
            lastRecording = receiver.Recording;
        }
        catch (Exception ex)
        {
            recordingStartError = $"Nie rozpoczęto nagrywania: {ex.Message}";
        }
        finally
        {
            recordingBusy = false;
            RefreshRecordingStatus();
        }
    }

    private void StopRecording_Click(object sender, RoutedEventArgs e)
    {
        if (recordingBusy || liveReceiver == null) return;
        recordingBusy = true;
        StartRecording.IsEnabled = StopRecording.IsEnabled = false;
        RecordingStatus.Text = "Kończenie zapisu…";
        recordingTask = StopRecordingAsync(liveReceiver);
    }

    private async Task StopRecordingAsync(Vlp16LiveReceiver receiver)
    {
        try { await receiver.StopRecordingAsync(); }
        catch (Exception ex) { RecordingWarning.Text = $"Błąd kończenia zapisu: {ex.Message}"; }
        finally
        {
            recordingBusy = false;
            RefreshRecordingStatus();
        }
    }

    private void RefreshRecordingStatus()
    {
        if (liveReceiver?.Recording is { } current) lastRecording = current;
        bool listening = !closing && liveReceiver?.State is
            LiveReceptionState.Listening or LiveReceptionState.Receiving or LiveReceptionState.NoData;
        bool active = lastRecording?.IsRecording == true;
        StartRecording.IsEnabled = listening && !active && !recordingBusy && (lastRecording?.IsCompleted ?? true);
        StopRecording.IsEnabled = listening && active && !recordingBusy;
        if (recordingStartError != null)
        {
            RecordingStatus.Text = recordingStartError;
            return;
        }
        if (lastRecording is not { } recording || recordingBusy) return;
        RecordingStatus.Foreground = recording.Error != null || active
            ? System.Windows.Media.Brushes.DarkRed : System.Windows.Media.Brushes.DarkGreen;
        RecordingStatus.Text = recording.Error != null
            ? $"Błąd zapisu: {recording.Error.Message}. Plik może być niekompletny; spróbuj go otworzyć."
            : active ? "● REC — nagrywanie" : !recording.IsCompleted ? "Kończenie zapisu…" : $"Zapisano: {Path.GetFileName(recording.Path)}";
        RecordingStatistics.Text = $"Czas: {recording.Duration:hh\\:mm\\:ss} | Pakiety: {recording.WrittenPackets:N0} | {recording.WrittenBytes / 1048576.0:F2} MB";
        RecordingWarning.Text = recording.DroppedPackets > 0 || (!active && recording.UnwrittenPackets > 0)
            ? $"Niepełne nagranie! Przepełnienie kolejki: {recording.DroppedPackets:N0}; niezapisane przyjęte pakiety: {recording.UnwrittenPackets:N0}."
            : "";
    }

    private void ConnectLive_Click(object sender, RoutedEventArgs e)
    {
        if (liveCancellation != null || closing) return;
        if (!System.Net.IPAddress.TryParse(LiveAddress.Text, out var address) ||
            address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
        {
            LiveStatus.Text = "Błąd: wpisz lokalny adres IPv4 lub 0.0.0.0.";
            return;
        }
        if (!int.TryParse(LivePort.Text, out int port) || port is < 1 or > 65535)
        {
            LiveStatus.Text = "Błąd: port musi należeć do zakresu 1–65535.";
            return;
        }

        liveCancellation = new();
        ConnectLive.IsEnabled = LiveAddress.IsEnabled = LivePort.IsEnabled = false;
        DisconnectLive.IsEnabled = true;
        OpenFile.IsEnabled = StartSimulation.IsEnabled = false;
        LiveStatus.Text = "Uruchamianie nasłuchu…";
        LiveError.Text = "";
        liveTask = RunLiveAsync(address, port, liveCancellation.Token);
    }

    private async Task RunLiveAsync(System.Net.IPAddress address, int port, CancellationToken token)
    {
        using var receiverCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        Task receiving = Task.CompletedTask;
        try
        {
            simulationCancellation?.Cancel();
            captureCancellation?.Cancel();
            fileCancellation?.Cancel();
            displayCancellation?.Cancel();
            await Task.WhenAll(simulationTask, captureTask, fileTask, displayTask);
            token.ThrowIfCancellationRequested();

            Scene.ResetColorRange();
            liveReceiver = new Vlp16LiveReceiver();
            receiving = liveReceiver.RunAsync(address, port, receiverCancellation.Token);
            OpenFile.IsEnabled = StartSimulation.IsEnabled = !closing;
            FileStatus.Text = $"LIVE {address}:{port}";
            liveStatusTimer.Start();
            RefreshLiveStatus();
            bool firstFrame = true;
            while (await liveReceiver.Frames.WaitToReadAsync(token))
            {
                IReadOnlyList<Point3D>? latest = null;
                while (liveReceiver.Frames.TryRead(out var frame)) latest = frame;
                token.ThrowIfCancellationRequested();
                if (latest == null) continue;

                bool shown = await DisplayFrameAsync(latest, _ => Task.FromResult(!token.IsCancellationRequested));
                token.ThrowIfCancellationRequested();
                if (firstFrame && shown)
                {
                    Scene.ShowLidarView();
                    firstFrame = false;
                }
            }
            await receiving;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            LiveStatus.Text = "Błąd";
            LiveError.Text = $"Nie można odbierać LIVE: {ex.Message}";
        }
        finally
        {
            liveStatusTimer.Stop();
            receiverCancellation.Cancel();
            try { await receiving; }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                LiveStatus.Text = "Błąd";
                LiveError.Text = $"Błąd nasłuchu: {ex.Message}";
            }

            if (token.IsCancellationRequested) LiveStatus.Text = "Rozłączono";
            RefreshRecordingStatus();
            liveReceiver = null;
            StartRecording.IsEnabled = StopRecording.IsEnabled = false;
            liveCancellation?.Dispose();
            liveCancellation = null;
            ConnectLive.IsEnabled = LiveAddress.IsEnabled = LivePort.IsEnabled = !closing;
            DisconnectLive.IsEnabled = false;
            OpenFile.IsEnabled = StartSimulation.IsEnabled = !closing && fileCancellation == null;
        }
    }

    private void RefreshLiveStatus()
    {
        RefreshRecordingStatus();
        if (liveReceiver == null || closing) return;
        LiveStatus.Text = liveReceiver.State switch
        {
            LiveReceptionState.Listening => "Nasłuchiwanie",
            LiveReceptionState.Receiving => "Odbieranie danych",
            LiveReceptionState.NoData => "Brak danych od co najmniej 3 s",
            LiveReceptionState.Error => "Błąd",
            _ => "Rozłączono"
        };
        LivePackets.Text = $"Poprawne: {liveReceiver.ValidPackets:N0}, odrzucone: {liveReceiver.RejectedPackets:N0}";
        LivePoints.Text = $"Ostatnia odebrana chmura: {liveReceiver.LastPointCount:N0} pkt";
        LiveSender.Text = $"Nadawca: {liveReceiver.LastSender ?? "—"}";
    }

    private async void DisconnectLive_Click(object sender, RoutedEventArgs e)
    {
        DisconnectLive.IsEnabled = false;
        liveCancellation?.Cancel();
        displayCancellation?.Cancel();
        await liveTask;
    }


    private void OpenStartupCapture(object? sender, EventArgs e)
    {
        ContentRendered -= OpenStartupCapture;
        if (closing || fileCancellation != null || captureCancellation != null ||
            simulationCancellation != null || liveCancellation != null) return;

        string path = Path.Combine(AppContext.BaseDirectory, "samples", "demo.pcap");
        if (!File.Exists(path))
        {
            Status.Text = "Nie znaleziono nagrania startowego. Wybierz plik przyciskiem Otwórz plik.";
            return;
        }

        BeginOpeningFile(path, true);
    }

    private void RingSettings_Changed(object sender, RoutedEventArgs e)
    {
        // Zdarzenia kontrolek występują również podczas InitializeComponent.
        if (Scene == null || RingSpacing?.SelectedItem is not System.Windows.Controls.ComboBoxItem item)
            return;

        int spacing = int.Parse((string)item.Tag, System.Globalization.CultureInfo.InvariantCulture);
        Scene.SetDistanceRings(ShowRings.IsChecked == true, spacing);
    }

    private void View_Click(object sender, RoutedEventArgs e)
    {
        var selected = (System.Windows.Controls.Primitives.ToggleButton)sender;
        string view = (string)selected.Tag;
        Scene.SetView(view);
        foreach (var button in new[] { View3D, ViewXY, ViewXZ, ViewYZ })
            button.IsChecked = ReferenceEquals(button, selected);
    }

    private IReadOnlyList<Point3D> originalPoints = Array.Empty<Point3D>();
    private PointCloudFilterSettings filterSettings = new();
    private bool resettingFilter;
    private CancellationTokenSource? displayCancellation;
    private Task displayTask = Task.CompletedTask;

    private void FilterSettings_Changed(object sender, RoutedEventArgs e)
    {
        if (Scene == null || FilterMessage == null || resettingFilter || closing) return;
        if (HeightMinimum == null || HeightMaximum == null || IntensityMinimum == null ||
            IntensityMaximum == null) return;

        var candidate = filterSettings with
        {
            DistanceEnabled = EnableDistanceFilter.IsChecked == true,
            HeightEnabled = EnableHeightFilter.IsChecked == true,
            IntensityEnabled = EnableIntensityFilter.IsChecked == true
        };

        if (candidate.DistanceEnabled)
        {
            if (!TryDistance(FilterMinimum.Text, out double min) || !TryDistance(FilterMaximum.Text, out double max))
            {
                FilterMessage.Text = "Odległość: wpisz pełne liczby. Pozostaje ostatni poprawny zestaw ustawień.";
                return;
            }
            candidate = candidate with { MinimumDistance = min, MaximumDistance = max };
        }
        if (candidate.HeightEnabled)
        {
            if (!TryDistance(HeightMinimum.Text, out double min) || !TryDistance(HeightMaximum.Text, out double max))
            {
                FilterMessage.Text = "Wysokość: wpisz pełne liczby. Pozostaje ostatni poprawny zestaw ustawień.";
                return;
            }
            candidate = candidate with { MinimumHeight = min, MaximumHeight = max };
        }
        if (candidate.IntensityEnabled)
        {
            if (!TryDistance(IntensityMinimum.Text, out double min) || !TryDistance(IntensityMaximum.Text, out double max))
            {
                FilterMessage.Text = "Intensywność: wpisz pełne liczby. Pozostaje ostatni poprawny zestaw ustawień.";
                return;
            }
            candidate = candidate with { MinimumIntensity = min, MaximumIntensity = max };
        }
        try
        {
            PointCloudFilter.Validate(candidate);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            FilterMessage.Text = ex.Message;
            return;
        }

        filterSettings = candidate;
        FilterMessage.Text = "";
        displayTask = UpdateDisplayAsync();
    }


    private static bool TryDistance(string text, out double value) =>
        double.TryParse(text.Trim().Replace(',', '.'), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out value) && double.IsFinite(value);

    private void ResetFilter_Click(object sender, RoutedEventArgs e)
    {
        resettingFilter = true;
        EnableDistanceFilter.IsChecked = false;
        EnableHeightFilter.IsChecked = false;
        EnableIntensityFilter.IsChecked = false;
        FilterMinimum.Text = "0";
        FilterMaximum.Text = "20";
        HeightMinimum.Text = "-1";
        HeightMaximum.Text = "3";
        IntensityMinimum.Text = "0";
        IntensityMaximum.Text = "255";
        filterSettings = new();
        resettingFilter = false;
        FilterSettings_Changed(sender, e);
    }

    private Task<bool> DisplayFrameAsync(IReadOnlyList<Point3D> points,
        Func<CancellationToken, Task<bool>>? beforeDisplay = null)
    {
        originalPoints = points;
        var task = UpdateDisplayAsync(beforeDisplay);
        displayTask = task;
        return task;
    }

    private async Task<bool> UpdateDisplayAsync(Func<CancellationToken, Task<bool>>? beforeDisplay = null)
    {
        displayCancellation?.Cancel();
        using var cancellation = new CancellationTokenSource();
        displayCancellation = cancellation;
        var source = originalPoints;
        var settings = filterSettings;
        try
        {
            bool hasIntensity = true;
            var visible = await Task.Run(() =>
            {
                hasIntensity = source.Any(point => point.HasIntensity);
                return PointCloudFilter.Apply(source, settings, cancellation.Token);
            }, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (closing) return false;

            Scene.SetRingSource(source);
            Scene.ValidateMeasurementFilters(settings);
            IntensityInfo.Text = source.Count > 0 && !hasIntensity
                ? "Ta chmura nie zawiera intensywności. Aktywny filtr intensywności ukryje wszystkie punkty."
                : "";
            if (!await Scene.ShowPointsAsync(visible, cancellation.Token, beforeDisplay)) return false;
            cancellation.Token.ThrowIfCancellationRequested();
            PointCount.Text = $"Punkty widoczne: {visible.Count:N0} / {source.Count:N0}";
            return true;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!closing) FilterMessage.Text = $"Błąd aktualizacji chmury: {ex.Message}";
        }
        finally
        {
            if (ReferenceEquals(displayCancellation, cancellation)) displayCancellation = null;
        }
        return false;
    }


    private void ColorMode_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (Scene == null || ColorLegend == null ||
            ColorMode.SelectedItem is not System.Windows.Controls.ComboBoxItem item) return;

        var mode = Enum.Parse<LidarViewer.Visualization.PointColorMode>((string)item.Tag);
        Scene.SetColorMode(mode);
        ColorLegend.Text = mode switch
        {
            LidarViewer.Visualization.PointColorMode.Height => "Nisko: niebieski, środek: zielony, wysoko: czerwony.",
            LidarViewer.Visualization.PointColorMode.Intensity => "Niska: ciemny, wysoka: jasny. Brak intensywności: szary.",
            LidarViewer.Visualization.PointColorMode.Rgb => "Kolory zapisane w danych. Brak RGB: szary.",
            LidarViewer.Visualization.PointColorMode.Solid => "Wszystkie punkty niebieskie.",
            _ => "Niebieski: 0 m, zielony: 15 m, czerwony: 30 m i dalej."
        };
        displayTask = UpdateDisplayAsync();
    }

    private bool measurementEnabled;

    private void ToggleMeasurement_Click(object sender, RoutedEventArgs e)
    {
        measurementEnabled = !measurementEnabled;
        ToggleMeasurement.Content = measurementEnabled ? "Wyłącz pomiar" : "Włącz pomiar";
        Scene.SetMeasurementEnabled(measurementEnabled);
    }

    private void ShowMeasurement(Point3D? selected)
    {
        if (selected is not { } point)
        {
            MeasurementResult.Text = measurementEnabled
                ? Scene.MeasurementLost ? "Utracono punkt. Wskaż punkt ponownie." : "Kliknij punkt lewym przyciskiem myszy."
                : "Pomiar wyłączony.";
            return;
        }

        double distance = PointMeasurement.DistanceFromOrigin(point);
        MeasurementResult.Text = $"X: {point.X:F2} m\nY: {point.Y:F2} m\nZ: {point.Z:F2} m\nOdległość: {distance:F2} m";
    }


    private async void StartSimulation_Click(object sender, RoutedEventArgs e)
    {
        StartSimulation.IsEnabled = false;
        liveCancellation?.Cancel();
        displayCancellation?.Cancel();
        captureCancellation?.Cancel();
        await Task.WhenAll(captureTask, liveTask);
        if (closing) return;
        Scene.ResetColorRange();
        simulationCancellation = new();
        StartSimulation.IsEnabled = false;
        FileStatus.Text = "Nie otwarto pliku.";
        StopSimulation.IsEnabled = true;
        SimulationStatus.Text = "Symulacja: działa";
        Status.Text = "Wyświetlane dane syntetyczne — domek i podłoże.";
        simulationTask = RunSimulationAsync(simulationCancellation.Token);
    }

    private async Task RunSimulationAsync(CancellationToken token)
    {
        try
        {
            bool firstFrame = true;
            await foreach (var frame in new SyntheticPointSource().ReadFramesAsync(token))
            {
                await DisplayFrameAsync(frame);
                if (firstFrame)
                {
                    Scene.FitToCloud();
                    firstFrame = false;
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex) { Status.Text = $"Błąd symulacji: {ex.Message}"; }
        finally
        {
            StartSimulation.IsEnabled = !closing && fileCancellation == null;
            StopSimulation.IsEnabled = false;
            SimulationStatus.Text = "Symulacja: zatrzymana (ostatnia chmura pozostaje w widoku)";
            simulationCancellation?.Dispose();
            simulationCancellation = null;
        }
    }

    private async void StopSimulation_Click(object sender, RoutedEventArgs e)
    {
        StopSimulation.IsEnabled = false;
        simulationCancellation?.Cancel();
        await simulationTask;
    }

    private PcapRecording? recording;
    private bool capturePaused;
    private bool pendingSeek;
    private int requestedFrame;
    private int currentFrame = -1;
    private int seekVersion;
    private bool updatingTimeline;
    private bool draggingTimeline;
    private readonly SemaphoreSlim captureSignal = new(0, 1);

    private readonly PlaybackClock playbackClock = new();
    private readonly System.Diagnostics.Stopwatch playbackStopwatch = System.Diagnostics.Stopwatch.StartNew();

    private void PlaybackSpeed_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        // InitializeComponent zgłasza zdarzenie przed utworzeniem całego okna.
        if (PlaybackSpeed?.SelectedItem is not System.Windows.Controls.ComboBoxItem item ||
            playbackClock == null) return;

        double speed = double.Parse((string)item.Tag, System.Globalization.CultureInfo.InvariantCulture);
        playbackClock.SetSpeed(speed, playbackStopwatch.Elapsed.TotalSeconds);
        WakeCapture();
    }

    private async Task<bool> WaitForCaptureTimeAsync(double mediaSeconds, CancellationToken token)
    {
        while (!pendingSeek)
        {
            token.ThrowIfCancellationRequested();
            if (capturePaused)
            {
                await captureSignal.WaitAsync(token);
                continue;
            }
            double remaining = playbackClock.SecondsUntil(mediaSeconds, playbackStopwatch.Elapsed.TotalSeconds);
            if (remaining <= 0) return true;
            int milliseconds = (int)Math.Clamp(Math.Ceiling(remaining * 1000), 1, int.MaxValue - 1);
            await captureSignal.WaitAsync(milliseconds, token);
        }
        return false;
    }

    private void WakeCapture()
    {
        if (captureSignal.CurrentCount == 0) captureSignal.Release();
    }

    private async Task PlayCaptureAsync(string path, CancellationToken token)
    {
        string name = Path.GetFileName(path);
        FileStatus.Text = $"Indeksowanie nagrania: {name}";
        try
        {
            recording = await Task.Run(() => PcapRecording.Open(path, token), token);
            token.ThrowIfCancellationRequested();
            currentFrame = -1;
            requestedFrame = 0;
            pendingSeek = true;
            capturePaused = false;
            seekVersion++;
            Timeline.IsEnabled = true;
            PauseCapture.IsEnabled = true;
            PlaybackSpeed.IsEnabled = true;
            UpdateTimeline();
            bool firstFrame = true;

            while (true)
            {
                token.ThrowIfCancellationRequested();
                if (!pendingSeek && capturePaused)
                {
                    await captureSignal.WaitAsync(token);
                    continue;
                }

                bool isSeek = pendingSeek;
                int target = isSeek ? requestedFrame : currentFrame + 1;
                pendingSeek = false;
                if (target >= recording.Frames.Count)
                {
                    await WaitForCaptureTimeAsync(recording.Duration.TotalSeconds, token);
                    if (pendingSeek) continue;
                    capturePaused = true;
                    playbackClock.SetPaused(true, playbackStopwatch.Elapsed.TotalSeconds);
                    UpdateTimeline();
                    Status.Text = $"Koniec nagrania: {name}";
                    continue;
                }

                int version = seekVersion;
                // Odczyt wykonujemy podczas czasu pozostałego do wyświetlenia klatki.
                var points = await Task.Run(() => recording.ReadFrame(target, token), token);
                token.ThrowIfCancellationRequested();
                if (version != seekVersion) continue;
                bool shown = await DisplayFrameAsync(points, async displayToken =>
                {
                    using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, displayToken);
                    if (!isSeek && !await WaitForCaptureTimeAsync(recording.Frames[target].Time.TotalSeconds, linked.Token))
                        return false;
                    return version == seekVersion;
                });
                if (!shown) continue;
                token.ThrowIfCancellationRequested();
                if (version != seekVersion) continue;

                currentFrame = requestedFrame = target;
                if (isSeek)
                    playbackClock.Reset(recording.Frames[target].Time.TotalSeconds,
                        playbackStopwatch.Elapsed.TotalSeconds, capturePaused);
                if (firstFrame)
                {
                    Scene.ShowLidarView();
                    firstFrame = false;
                }
                UpdateTimeline();
                FileStatus.Text = name;
                Status.Text = capturePaused ? "Odtwarzanie wstrzymane." : $"Odtwarzanie: {name}";


            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (EndOfStreamException)
        {
            Status.Text = "Plik PCAP jest ucięty lub niekompletny.";
        }
        catch (Exception ex)
        {
            Status.Text = $"Błąd odczytu PCAP: {ex.Message}";
        }
        finally
        {
            recording = null;
            captureCancellation?.Dispose();
            captureCancellation = null;
            PauseCapture.IsEnabled = false;
            PlaybackSpeed.IsEnabled = false;
            Timeline.IsEnabled = false;
            PreviousFrame.IsEnabled = NextFrame.IsEnabled = false;
            StartSimulation.IsEnabled = !closing && fileCancellation == null;
        }
    }

    private void PauseCapture_Click(object sender, RoutedEventArgs e)
    {
        if (recording == null) return;
        if (capturePaused && currentFrame == recording.Frames.Count - 1 && !pendingSeek)
        {
            requestedFrame = 0;
            pendingSeek = true;
            seekVersion++;
            Scene.ClearSelection();
        }
        capturePaused = !capturePaused;
        playbackClock.SetPaused(capturePaused, playbackStopwatch.Elapsed.TotalSeconds);
        Status.Text = capturePaused ? "Odtwarzanie wstrzymane." : "Wznowiono odtwarzanie.";
        WakeCapture();
    }

    private void Timeline_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (recording == null) return;
        draggingTimeline = true;
        capturePaused = true;
        playbackClock.SetPaused(true, playbackStopwatch.Elapsed.TotalSeconds);
        WakeCapture();
    }

    private void Timeline_PreviewMouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        FinishTimelineDrag();
    }

    private void Timeline_LostMouseCapture(object sender, System.Windows.Input.MouseEventArgs e)
    {
        FinishTimelineDrag();
    }

    private void FinishTimelineDrag()
    {
        if (!draggingTimeline) return;
        draggingTimeline = false;
        if (recording != null)
            SeekToFrame(recording.FindFrame(TimeSpan.FromSeconds(Timeline.Value)));
    }

    private void Timeline_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (updatingTimeline || recording == null || !Timeline.IsEnabled) return;
        SeekToFrame(recording.FindFrame(TimeSpan.FromSeconds(e.NewValue)));
    }

    private void PreviousFrame_Click(object sender, RoutedEventArgs e) =>
        SeekToFrame(requestedFrame - 1);

    private void NextFrame_Click(object sender, RoutedEventArgs e) =>
        SeekToFrame(requestedFrame + 1);

    private void SeekToFrame(int frame)
    {
        if (recording == null) return;
        capturePaused = true;
        playbackClock.SetPaused(true, playbackStopwatch.Elapsed.TotalSeconds);
        requestedFrame = Math.Clamp(frame, 0, recording.Frames.Count - 1);
        pendingSeek = true;
        seekVersion++;
        // Numer żądania unieważnia starszą klatkę bez zgłaszania wyjątku anulowania.
        Scene.ClearSelection();
        PreviousFrame.IsEnabled = requestedFrame > 0;
        NextFrame.IsEnabled = requestedFrame < recording.Frames.Count - 1;
        WakeCapture();
    }

    private void UpdateTimeline()
    {
        if (recording == null) return;
        updatingTimeline = true;
        try
        {
            Timeline.Maximum = recording.Duration.TotalSeconds;
            double seconds = currentFrame < 0 ? 0 :
                currentFrame == recording.Frames.Count - 1
                    ? recording.Duration.TotalSeconds
                    : recording.Frames[currentFrame].Time.TotalSeconds;
            if (!draggingTimeline) Timeline.Value = seconds;
            PlaybackTime.Text = $"{FormatCaptureTime(TimeSpan.FromSeconds(seconds))} / {FormatCaptureTime(recording.Duration)}";
            FrameCounter.Text = $"Klatka: {Math.Max(0, currentFrame + 1)} / {recording.Frames.Count}";
            PreviousFrame.IsEnabled = currentFrame > 0;
            NextFrame.IsEnabled = currentFrame >= 0 && currentFrame < recording.Frames.Count - 1;
        }
        finally { updatingTimeline = false; }
    }

    private static string FormatCaptureTime(TimeSpan time) =>
        time.TotalHours >= 1 ? $"{(int)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00}"
            : $"{(int)time.TotalMinutes:00}:{time.Seconds:00}";


    private void OpenFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Otwórz plik",
            Filter = "Obsługiwane pliki (*.pcap;*.xyz;*.ply;*.pcd)|*.pcap;*.xyz;*.ply;*.pcd|PCAP (*.pcap)|*.pcap|XYZ (*.xyz)|*.xyz|PLY (*.ply)|*.ply|PCD (*.pcd)|*.pcd",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true || closing) return;

        string extension = Path.GetExtension(dialog.FileName).ToLowerInvariant();
        if (extension is not (".pcap" or ".xyz" or ".ply" or ".pcd"))
        {
            Status.Text = "Wybierz plik PCAP, XYZ, PLY lub PCD.";
            return;
        }

        BeginOpeningFile(dialog.FileName, extension == ".pcap");
    }

    private void BeginOpeningFile(string path, bool isCapture)
    {
        fileCancellation = new();
        OpenFile.IsEnabled = false;
        StartSimulation.IsEnabled = false;
        StopSimulation.IsEnabled = false;
        PauseCapture.IsEnabled = false;
        fileTask = OpenFileAsync(path, isCapture, fileCancellation.Token);
    }

    private async Task OpenFileAsync(string path, bool isCapture, CancellationToken token)
    {
        string name = Path.GetFileName(path);
        try
        {
            simulationCancellation?.Cancel();
            captureCancellation?.Cancel();
            liveCancellation?.Cancel();
            displayCancellation?.Cancel();
            await Task.WhenAll(simulationTask, captureTask, liveTask, displayTask);
            token.ThrowIfCancellationRequested();

            Scene.ResetColorRange();
            if (isCapture)
            {
                captureCancellation = new();
                FrameCounter.Text = "Indeksowanie nagrania…";
                captureTask = PlayCaptureAsync(path, captureCancellation.Token);
                return;
            }

            Status.Text = $"Wczytywanie chmury: {name}";
            var points = await Task.Run(() => PointCloudFileReader.Read(path, token), token);
            token.ThrowIfCancellationRequested();

            if (!await DisplayFrameAsync(points))
            {
                if (!closing) Status.Text = "Nie zakończono wyświetlania chmury. Sprawdź komunikat przy filtrach lub otwórz plik ponownie.";
                return;
            }
            Scene.FitToCloud();
            FileStatus.Text = name;
            Status.Text = $"Otwarta chmura: {name}";
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            Status.Text = $"Błąd otwierania {name}: {ex.Message}";
        }
        finally
        {
            fileCancellation?.Dispose();
            fileCancellation = null;
            OpenFile.IsEnabled = !closing;
            StartSimulation.IsEnabled = !closing;
        }
    }


    private Task saveTask = Task.CompletedTask;
    private readonly CancellationTokenSource saveCancellation = new();

    private void SaveCloud_Click(object sender, RoutedEventArgs e)
    {
        var snapshot = Scene.GetCloudSnapshot();
        if (snapshot.Length == 0)
        {
            SaveStatus.Text = "Brak aktualnej chmury do zapisania.";
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Zapisz chmurę",
            Filter = "PLY ASCII (*.ply)|*.ply|PCD ASCII (*.pcd)|*.pcd|XYZ (*.xyz)|*.xyz",
            DefaultExt = ".ply",
            AddExtension = true,
            FileName = "chmura",
            OverwritePrompt = true
        };
        if (dialog.ShowDialog(this) != true || closing) return;

        SaveCloud.IsEnabled = false;
        saveTask = SaveCloudAsync(dialog.FileName, snapshot);
    }

    private async Task SaveCloudAsync(string path, Point3D[] snapshot)
    {
        try
        {
            SaveStatus.Text = $"Zapisywanie chmury: {Path.GetFileName(path)}";
            await Task.Run(() => PointCloudFileWriter.Write(path, snapshot, saveCancellation.Token));
            if (!closing)
                SaveStatus.Text = $"Zapisano {snapshot.Length:N0} punktów: {Path.GetFileName(path)}";
        }
        catch (OperationCanceledException) when (saveCancellation.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (!closing) SaveStatus.Text = $"Błąd zapisu chmury: {ex.Message}";
        }
        finally
        {
            SaveCloud.IsEnabled = !closing;
        }
    }


    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (allowClose) return;
        e.Cancel = true;
        if (closing) return;
        closing = true;
        IsEnabled = false;
        simulationCancellation?.Cancel();
        captureCancellation?.Cancel();
        fileCancellation?.Cancel();
        liveCancellation?.Cancel();
        liveStatusTimer.Stop();
        saveCancellation.Cancel();
        displayCancellation?.Cancel();
        await Task.Yield();
        await Task.WhenAll(simulationTask, captureTask, fileTask, saveTask, displayTask, liveTask);
        await recordingTask;
        saveCancellation.Dispose();
        Scene.Dispose();
        allowClose = true;
        Close();
    }
}
