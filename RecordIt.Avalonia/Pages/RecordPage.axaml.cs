using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using RecordIt.Core.Services;

namespace RecordIt.Avalonia.Pages;

public class SceneItem
{
    public string Name { get; set; } = string.Empty;
    public override string ToString() => Name;
}

public class SourceItem
{
    public string Name     { get; set; } = string.Empty;
    public string IconText { get; set; } = "🖥";
}

public partial class RecordPage : UserControl
{
    private readonly ScreenRecordingService _recordingService = new();
    private readonly ObservableCollection<SceneItem>  _scenes  = new();
    private readonly ObservableCollection<SourceItem> _sources = new();

    private DispatcherTimer? _recordTimer;
    private DispatcherTimer? _meterTimer;
    private TimeSpan _elapsed;
    private bool _isRecording;

    // Simple channel view data
    private record AudioChannelData(string Name, Border VuContainer, TextBlock DbLabel, Slider VolumeSlider, ToggleButton MuteBtn, bool IsDesktop)
    {
        public float Peak { get; set; }
    }

    private readonly List<AudioChannelData> _channels = new();

    public RecordPage()
    {
        InitializeComponent();
        ScenesList.ItemsSource  = _scenes;
        SourcesList.ItemsSource = _sources;
        _ = InitAsync();
    }

    private async System.Threading.Tasks.Task InitAsync()
    {
        // Add default scene
        _scenes.Add(new SceneItem { Name = "Scene 1" });
        _scenes.Add(new SceneItem { Name = "Scene 2" });
        ScenesList.SelectedIndex = 0;
        RefreshSceneQuickBar();

        // Default sources for Scene 1
        _sources.Add(new SourceItem { Name = "Display 1",   IconText = "🖥" });
        _sources.Add(new SourceItem { Name = "Microphone",  IconText = "🎙" });

        // Build default mixer channels
        AddMixerChannel("Desktop Audio", isDesktop: true);
        AddMixerChannel("Mic/Aux",       isDesktop: false);

        // Probe devices (non-blocking)
        await ProbeAndPopulateDevicesAsync();

        // Start simulated meter timer
        StartMeterTimer();
    }

    // ─── Audio Mixer ─────────────────────────────────────────────────────────

    private void AddMixerChannel(string name, bool isDesktop)
    {
        // VU bar container
        var vuLeft  = new Border { Background = Brushes.Transparent, Width = 8, VerticalAlignment = VerticalAlignment.Bottom, Height = 0 };
        var vuRight = new Border { Background = Brushes.Transparent, Width = 8, VerticalAlignment = VerticalAlignment.Bottom, Height = 0 };

        var vuBrush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            EndPoint   = new RelativePoint(0, 0, RelativeUnit.Relative),
            GradientStops = new GradientStops
            {
                new GradientStop(Color.Parse("#22C55E"), 0.0),
                new GradientStop(Color.Parse("#22C55E"), 0.6),
                new GradientStop(Color.Parse("#FBBF24"), 0.75),
                new GradientStop(Color.Parse("#EF4444"), 1.0),
            }
        };

        vuLeft.Background  = vuBrush;
        vuRight.Background = vuBrush;

        var vuGrid = new Grid
        {
            Width  = 20,
            Height = 120,
            ColumnDefinitions = new ColumnDefinitions("*,2,*"),
            VerticalAlignment = VerticalAlignment.Stretch,
        };

        var vuBg1 = new Border { Background = new SolidColorBrush(Color.Parse("#1E1E1E")), CornerRadius = new CornerRadius(2) };
        var vuBg2 = new Border { Background = new SolidColorBrush(Color.Parse("#1E1E1E")), CornerRadius = new CornerRadius(2) };

        Grid.SetColumn(vuBg1,  0); vuGrid.Children.Add(vuBg1);
        Grid.SetColumn(vuBg2,  2); vuGrid.Children.Add(vuBg2);
        Grid.SetColumn(vuLeft, 0); vuGrid.Children.Add(vuLeft);
        Grid.SetColumn(vuRight,2); vuGrid.Children.Add(vuRight);

        var vuContainer = new Border
        {
            Child  = vuGrid,
            Height = 120,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        // dB label
        var dbLabel = new TextBlock
        {
            Text              = "−∞",
            FontSize          = 9,
            Foreground        = new SolidColorBrush(Color.Parse("#9E9E9E")),
            HorizontalAlignment = HorizontalAlignment.Center,
        };

        // Volume fader
        var fader = new Slider
        {
            Minimum = 0,
            Maximum = 1,
            Value   = 1,
            Width   = 90,
            VerticalAlignment = VerticalAlignment.Center,
        };

        // Mute button
        var muteBtn = new ToggleButton
        {
            Content  = "M",
            Width    = 28,
            Height   = 24,
            FontSize = 11,
            Background = Brushes.Transparent,
            BorderBrush = new SolidColorBrush(Color.Parse("#252525")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
        };

        // Type dot color
        var dotColor = isDesktop ? Color.Parse("#06B6D4") : Color.Parse("#6366F1");
        var typeDot  = new Ellipse
        {
            Width  = 6,
            Height = 6,
            Fill   = new SolidColorBrush(dotColor),
            HorizontalAlignment = HorizontalAlignment.Center,
        };

        // Channel strip
        var strip = new Border
        {
            Width  = 90,
            Padding = new Thickness(4),
            Background = new SolidColorBrush(Color.Parse("#161616")),
            BorderBrush = new SolidColorBrush(Color.Parse("#252525")),
            BorderThickness = new Thickness(0, 0, 1, 0),
            Child = new StackPanel
            {
                Spacing = 4,
                HorizontalAlignment = HorizontalAlignment.Center,
                Children =
                {
                    new TextBlock
                    {
                        Text = name,
                        FontSize = 10,
                        Foreground = new SolidColorBrush(Color.Parse("#9E9E9E")),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis,
                        MaxWidth = 80,
                    },
                    typeDot,
                    vuContainer,
                    dbLabel,
                    fader,
                    muteBtn,
                }
            }
        };

        AudioMixerChannels.Children.Add(strip);
        _channels.Add(new AudioChannelData(name, vuContainer, dbLabel, fader, muteBtn, isDesktop));
    }

    private void StartMeterTimer()
    {
        _meterTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _meterTimer.Tick += MeterTimer_Tick;
        _meterTimer.Start();
    }

    private readonly Random _rng = new();

    private void MeterTimer_Tick(object? sender, EventArgs e)
    {
        foreach (var ch in _channels)
        {
            if (ch.MuteBtn.IsChecked == true)
            {
                UpdateChannel(ch, 0f, 0f);
                continue;
            }

            // Simulate peak (replace with real Core Audio peak on Windows)
            float peak = (float)(_rng.NextDouble() * 0.5 + 0.05);
            float left  = peak * (float)(0.9 + _rng.NextDouble() * 0.1);
            float right = peak * (float)(0.8 + _rng.NextDouble() * 0.2);
            float vol   = (float)ch.VolumeSlider.Value;
            UpdateChannel(ch, left * vol, right * vol);
        }
    }

    private void UpdateChannel(AudioChannelData ch, float left, float right)
    {
        const double MaxH = 120.0;
        if (ch.VuContainer.Child is Grid vuGrid && vuGrid.Children.Count >= 4)
        {
            // Children 0,1 are backgrounds; children 2,3 are VU bars
            if (vuGrid.Children[2] is Border bl) bl.Height = left  * MaxH;
            if (vuGrid.Children[3] is Border br) br.Height = right * MaxH;
        }

        double db = left > 0 ? 20 * Math.Log10(left) : double.NegativeInfinity;
        ch.DbLabel.Text = double.IsNegativeInfinity(db) ? "−∞" : $"{db:0.0} dB";
    }

    // ─── Device probing ──────────────────────────────────────────────────────

    private async System.Threading.Tasks.Task ProbeAndPopulateDevicesAsync()
    {
        try
        {
            var devices = await _recordingService.ProbeDevicesAsync();
            WebcamDeviceCombo.Items.Clear();
            WebcamDeviceCombo.Items.Add(new ComboBoxItem { Content = "(disabled)", IsSelected = true });
            foreach (var v in devices.VideoDevices)
                WebcamDeviceCombo.Items.Add(new ComboBoxItem { Content = v });

            MicDeviceCombo.Items.Clear();
            MicDeviceCombo.Items.Add(new ComboBoxItem { Content = "Default Microphone", IsSelected = true });
            foreach (var a in devices.AudioDevices)
                MicDeviceCombo.Items.Add(new ComboBoxItem { Content = a });

            WebcamDeviceCombo.SelectedIndex = 0;
            MicDeviceCombo.SelectedIndex    = 0;
        }
        catch { /* ffmpeg not available on this platform */ }
    }

    // ─── Scene management ────────────────────────────────────────────────────

    private void RefreshSceneQuickBar()
    {
        SceneQuickBar.Children.Clear();
        int i = 0;
        foreach (var scene in _scenes)
        {
            int idx = i++;
            var btn = new Button
            {
                Content     = scene.Name,
                FontSize    = 11,
                Padding     = new Thickness(10, 4),
                CornerRadius = new CornerRadius(6),
                Background  = idx == ScenesList.SelectedIndex
                    ? new SolidColorBrush(Color.Parse("#292952"))
                    : new SolidColorBrush(Color.Parse("#1E1E1E")),
                Foreground  = idx == ScenesList.SelectedIndex
                    ? new SolidColorBrush(Color.Parse("#6366F1"))
                    : new SolidColorBrush(Color.Parse("#9E9E9E")),
                BorderBrush = new SolidColorBrush(idx == ScenesList.SelectedIndex
                    ? Color.Parse("#6366F1") : Color.Parse("#252525")),
                BorderThickness = new Thickness(1),
            };
            btn.Click += (_, _) => ScenesList.SelectedIndex = idx;
            SceneQuickBar.Children.Add(btn);
        }
    }

    private void ScenesList_SelectionChanged(object? sender, SelectionChangedEventArgs e) => RefreshSceneQuickBar();
    private void SourcesList_SelectionChanged(object? sender, SelectionChangedEventArgs e) { }

    // ─── Recording ───────────────────────────────────────────────────────────

    private async void StartRecordBtn_Click(object? sender, RoutedEventArgs e)
    {
        if (_isRecording) return;

        var outputPath  = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
            $"RecordIt_{DateTime.Now:yyyyMMdd_HHmmss}.mp4");

        var resolution  = (QualityCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "1080p (1920×1080)";
        var res         = resolution.StartsWith("4K") ? "3840x2160" :
                          resolution.StartsWith("1440") ? "2560x1440" :
                          resolution.StartsWith("720")  ? "1280x720"  :
                          resolution.StartsWith("480")  ? "854x480"   : "1920x1080";
        var fps         = int.TryParse(((FpsCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "30 fps").Split(' ')[0], out var f) ? f : 30;
        var micDevice   = (MicDeviceCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Default Microphone";
        var includeMic  = micDevice != "(disabled)";

        try
        {
            await _recordingService.StartRecording(
                "desktop", outputPath, res, fps,
                includeMic, false, null,
                includeMic ? micDevice : null);

            _isRecording = true;
            StartRecordBtn.IsVisible = false;
            StopRecordBtn.IsVisible  = true;
            RecordingBadge.IsVisible = true;
            StatusText.Text = "Recording…";
            MenuStartRecord.IsEnabled = false;
            MenuStopRecord.IsEnabled  = true;

            _elapsed = TimeSpan.Zero;
            _recordTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _recordTimer.Tick += (_, _) =>
            {
                _elapsed += TimeSpan.FromSeconds(1);
                RecordingTimerText.Text = _elapsed.ToString(@"mm\:ss");
            };
            _recordTimer.Start();

            // Notify main window
            if (VisualRoot is MainWindow mw)
                mw.StartRecordingIndicator(_elapsed, _recordTimer);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error: {ex.Message}";
        }
    }

    private void StopRecordBtn_Click(object? sender, RoutedEventArgs e)
    {
        if (!_isRecording) return;
        _recordingService.StopRecording();
        _recordTimer?.Stop();
        _isRecording = false;
        StartRecordBtn.IsVisible = true;
        StopRecordBtn.IsVisible  = false;
        RecordingBadge.IsVisible = false;
        StatusText.Text = "Ready";
        MenuStartRecord.IsEnabled = true;
        MenuStopRecord.IsEnabled  = false;

        if (VisualRoot is MainWindow mw) mw.StopRecordingIndicator();
    }

    // ─── Toolbar handlers ────────────────────────────────────────────────────

    private void AddSceneBtn_Click(object? sender, RoutedEventArgs e)
    {
        _scenes.Add(new SceneItem { Name = $"Scene {_scenes.Count + 1}" });
        RefreshSceneQuickBar();
    }

    private void RemoveSceneBtn_Click(object? sender, RoutedEventArgs e)
    {
        if (ScenesList.SelectedIndex >= 0 && _scenes.Count > 1)
        {
            _scenes.RemoveAt(ScenesList.SelectedIndex);
            RefreshSceneQuickBar();
        }
    }

    private void SceneUpBtn_Click(object? sender, RoutedEventArgs e)
    {
        int i = ScenesList.SelectedIndex;
        if (i <= 0) return;
        (_scenes[i], _scenes[i - 1]) = (_scenes[i - 1], _scenes[i]);
        ScenesList.SelectedIndex = i - 1;
        RefreshSceneQuickBar();
    }

    private void SceneDownBtn_Click(object? sender, RoutedEventArgs e)
    {
        int i = ScenesList.SelectedIndex;
        if (i < 0 || i >= _scenes.Count - 1) return;
        (_scenes[i], _scenes[i + 1]) = (_scenes[i + 1], _scenes[i]);
        ScenesList.SelectedIndex = i + 1;
        RefreshSceneQuickBar();
    }

    private void AddSourceBtn_Click(object? sender, RoutedEventArgs e)
    {
        _sources.Add(new SourceItem { Name = "Display Capture", IconText = "🖥" });
    }

    private void RemoveSourceBtn_Click(object? sender, RoutedEventArgs e)
    {
        if (SourcesList.SelectedIndex >= 0)
            _sources.RemoveAt(SourcesList.SelectedIndex);
    }

    private void SourceUpBtn_Click(object? sender, RoutedEventArgs e)
    {
        int i = SourcesList.SelectedIndex;
        if (i <= 0) return;
        (_sources[i], _sources[i - 1]) = (_sources[i - 1], _sources[i]);
        SourcesList.SelectedIndex = i - 1;
    }

    private void SourceDownBtn_Click(object? sender, RoutedEventArgs e)
    {
        int i = SourcesList.SelectedIndex;
        if (i < 0 || i >= _sources.Count - 1) return;
        (_sources[i], _sources[i + 1]) = (_sources[i + 1], _sources[i]);
        SourcesList.SelectedIndex = i + 1;
    }

    private void SourceSettingsBtn_Click(object? sender, RoutedEventArgs e) { }

    private void AddAudioTrackBtn_Click(object? sender, RoutedEventArgs e)
    {
        AddMixerChannel($"Audio {_channels.Count + 1}", isDesktop: false);
    }

    private void ClearAudioTracksBtn_Click(object? sender, RoutedEventArgs e)
    {
        _channels.Clear();
        AudioMixerChannels.Children.Clear();
        AddMixerChannel("Desktop Audio", isDesktop: true);
        AddMixerChannel("Mic/Aux",       isDesktop: false);
    }

    private void ProbeDevicesBtn_Click(object? sender, RoutedEventArgs e) => _ = ProbeAndPopulateDevicesAsync();

    private void RefreshSourcesBtn_Click(object? sender, RoutedEventArgs e) { }

    private void StartPreviewBtn_Click(object? sender, RoutedEventArgs e)
    {
        StartPreviewBtn.IsVisible = false;
        StopPreviewBtn.IsVisible  = true;
        PreviewHintText.Text = "Live Preview";
    }

    private void StopPreviewBtn_Click(object? sender, RoutedEventArgs e)
    {
        StartPreviewBtn.IsVisible = true;
        StopPreviewBtn.IsVisible  = false;
        PreviewHintText.Text = "No source selected";
    }

    private void ExportBtn_Click(object? sender, RoutedEventArgs e) => NavigateTo("library");

    // ─── Menu handlers ───────────────────────────────────────────────────────

    private void MenuNewScene_Click(object? sender, RoutedEventArgs e)   => AddSceneBtn_Click(sender, e);
    private void MenuOpenScene_Click(object? sender, RoutedEventArgs e)  { }
    private void MenuExport_Click(object? sender, RoutedEventArgs e)     => NavigateTo("library");
    private void MenuExit_Click(object? sender, RoutedEventArgs e)       => (VisualRoot as Window)?.Close();
    private void MenuSettings_Click(object? sender, RoutedEventArgs e)   => NavigateTo("settings");
    private void MenuFullscreen_Click(object? sender, RoutedEventArgs e) { }
    private void MenuResetLayout_Click(object? sender, RoutedEventArgs e) { }
    private void MenuWhiteboard_Click(object? sender, RoutedEventArgs e) => NavigateTo("whiteboard");
    private void MenuLibrary_Click(object? sender, RoutedEventArgs e)    => NavigateTo("library");
    private void MenuAbout_Click(object? sender, RoutedEventArgs e)      { }

    private void NavigateTo(string page)
    {
        if (VisualRoot is MainWindow mw) mw.NavigateTo(page);
    }
}
