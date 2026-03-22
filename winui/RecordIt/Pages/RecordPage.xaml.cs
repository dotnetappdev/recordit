using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using RecordIt.Core.Services;
using RecordIt.Services;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace RecordIt.Pages;

// ── Simple data models ────────────────────────────────────────────────────────

public sealed class SceneItem
{
    public string Name { get; set; } = "";
}

public sealed class SourceItem
{
    public string Name { get; set; } = "";
    public string Icon { get; set; } = "\uE7F8";  // default: screen
    public string SourceType { get; set; } = "screen";
    public bool IsVisible { get; set; } = true;
}

// ── AudioChannelView - represents one fader strip in the mixer ────────────────

internal sealed class AudioChannelView
{
    public string Name { get; }
    public bool IsDesktop { get; }
    public bool IsMic { get; }

    // UI elements owned by this channel
    public Microsoft.UI.Xaml.Shapes.Rectangle VuBarLeft  { get; }
    public Microsoft.UI.Xaml.Shapes.Rectangle VuBarRight { get; }
    public TextBlock    DbLabel    { get; }
    public Slider       VolSlider  { get; }
    public ToggleButton MuteBtn    { get; }

    public float Volume  => (float)VolSlider.Value;
    public bool  IsMuted => MuteBtn.IsChecked == true;

    public AudioChannelView(string name, bool isDesktop = false, bool isMic = false)
    {
        Name      = name;
        IsDesktop = isDesktop;
        IsMic     = isMic;

        // Create UI elements on the UI thread (where this ctor is called from)
        var vuBrush = MakeVuBrush();

        VuBarLeft  = new Microsoft.UI.Xaml.Shapes.Rectangle { Width = 4, RadiusX = 2, RadiusY = 2, Fill = vuBrush };
        VuBarRight = new Microsoft.UI.Xaml.Shapes.Rectangle { Width = 4, RadiusX = 2, RadiusY = 2, Fill = vuBrush };
        DbLabel    = new TextBlock { FontSize = 9, TextAlignment = TextAlignment.Center };
        VolSlider  = new Slider();
        MuteBtn    = new ToggleButton();

        VolSlider.Minimum = 0;
        VolSlider.Maximum = 1;
        VolSlider.Value   = 1;
        VolSlider.StepFrequency = 0.01;

        DbLabel.Text = "0.0 dB";
        DbLabel.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 153, 153, 153));

        MuteBtn.Content = "M";
        MuteBtn.FontSize = 10;
        MuteBtn.FontWeight = Microsoft.UI.Text.FontWeights.Bold;

        VolSlider.ValueChanged += (_, _) =>
        {
            var db = Volume > 0 ? 20.0 * Math.Log10(Volume) : -60.0;
            DbLabel.Text = db < -59.9 ? "-∞ dB" : $"{db:+0.0;-0.0} dB";
        };
    }

    /// <summary>Update VU bar heights (0.0 – 1.0). MaxHeight is the container height.</summary>
    public void UpdatePeak(float left, float right, double maxHeight)
    {
        if (IsMuted) { left = 0; right = 0; }
        VuBarLeft.Height  = Math.Clamp(left,  0f, 1f) * maxHeight;
        VuBarRight.Height = Math.Clamp(right, 0f, 1f) * maxHeight;
    }

    private static LinearGradientBrush MakeVuBrush()
    {
        var b = new LinearGradientBrush();
        b.StartPoint = new Point(0, 1);
        b.EndPoint   = new Point(0, 0);
        b.GradientStops.Add(new GradientStop { Color = Windows.UI.Color.FromArgb(255, 34, 197, 94),  Offset = 0.0 });
        b.GradientStops.Add(new GradientStop { Color = Windows.UI.Color.FromArgb(255, 234, 179, 8),  Offset = 0.70 });
        b.GradientStops.Add(new GradientStop { Color = Windows.UI.Color.FromArgb(255, 239, 68,  68), Offset = 1.0 });
        return b;
    }

    /// <summary>Build the full channel strip UI element.</summary>
    public UIElement BuildChannelStrip()
    {
        const double VuHeight   = 110;
        const double VuBarW     = 4;
        const double StripWidth = 72;

        // VU meter container (two bars side by side)
        var vuContainer = new Border
        {
            Width        = VuBarW * 2 + 6,
            Height       = VuHeight,
            Background   = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 18, 18, 18)),
            CornerRadius = new CornerRadius(3),
            BorderBrush  = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 40, 40, 40)),
            BorderThickness = new Thickness(1),
            Margin       = new Thickness(0, 0, 0, 4),
        };

        // Grid stretches to fill the full Border height; bars align to bottom and grow upward
        var vuGrid = new Grid
        {
            VerticalAlignment   = VerticalAlignment.Stretch,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        vuGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        vuGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2) });
        vuGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        VuBarLeft.VerticalAlignment  = VerticalAlignment.Bottom;
        VuBarRight.VerticalAlignment = VerticalAlignment.Bottom;
        VuBarLeft.Height = 0;
        VuBarRight.Height = 0;

        Grid.SetColumn(VuBarLeft, 0);
        Grid.SetColumn(VuBarRight, 2);
        vuGrid.Children.Add(VuBarLeft);
        vuGrid.Children.Add(VuBarRight);
        vuContainer.Child = vuGrid;

        // Volume slider (horizontal, narrow)
        VolSlider.HorizontalAlignment = HorizontalAlignment.Stretch;
        VolSlider.Margin = new Thickness(0, 2, 0, 2);

        // Mute button
        MuteBtn.HorizontalAlignment = HorizontalAlignment.Center;
        MuteBtn.Style = (Style)Application.Current.Resources["MuteButtonStyle"];

        // Name label
        var nameLabel = new TextBlock
        {
            Text              = Name,
            FontSize          = 10,
            TextAlignment     = TextAlignment.Center,
            TextTrimming      = TextTrimming.CharacterEllipsis,
            Foreground        = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 200, 200, 200)),
            Margin            = new Thickness(0, 0, 0, 2),
        };

        // Channel type indicator dot
        var accent = IsDesktop
            ? Windows.UI.Color.FromArgb(255, 6, 182, 212)
            : IsMic
                ? Windows.UI.Color.FromArgb(255, 99, 102, 241)
                : Windows.UI.Color.FromArgb(255, 139, 92, 246);

        var typeDot = new Microsoft.UI.Xaml.Shapes.Ellipse
        {
            Width  = 5,
            Height = 5,
            Fill   = new SolidColorBrush(accent),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 4),
        };

        var strip = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Width       = StripWidth,
            Padding     = new Thickness(6, 6, 6, 6),
            Spacing     = 0,
        };

        strip.Children.Add(nameLabel);
        strip.Children.Add(typeDot);
        strip.Children.Add(vuContainer);
        strip.Children.Add(DbLabel);
        strip.Children.Add(VolSlider);
        strip.Children.Add(MuteBtn);

        // Wrap in a border for channel separation
        var channelBorder = new Border
        {
            BorderBrush     = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 35, 35, 35)),
            BorderThickness = new Thickness(0, 0, 1, 0),
            Child           = strip,
        };

        return channelBorder;
    }
}

// ─────────────────────────────────────────────────────────────────────────────

public sealed partial class RecordPage : Page, IDisposable
{
    // Services
    private readonly ScreenRecordingService _recordingService = new();
    private readonly AudioMeterService      _audioMeter       = new();

    // State
    private string?  _selectedSourceId;
    private string?  _outputFilePath;
    private Process? _previewProcess;

    // Metering
    private DispatcherTimer? _meterTimer;
    private AudioChannelView? _desktopChannel;
    private AudioChannelView? _micChannel;

    // Collections
    public ObservableCollection<SceneItem>  Scenes  { get; } = new();
    public ObservableCollection<SourceItem> Sources { get; } = new();

    private const double VuMaxHeight = 110;

    public RecordPage()
    {
        InitializeComponent();
        ScenesList.ItemsSource  = Scenes;
        SourcesList.ItemsSource = Sources;

        // Seed with a default scene
        AddScene("Scene 1");
        SetStatus("Ready · select a capture source");

        // Auto-probe devices on load (async, non-blocking)
        _ = InitAsync();
    }

    private async Task InitAsync()
    {
        await LoadCaptureSources();
        await ProbeAndPopulateDevicesAsync();
        BuildDefaultMixerChannels();
        StartMeterTimer();
    }

    // ── Capture sources ───────────────────────────────────────────────────────

    private async Task LoadCaptureSources()
    {
        Sources.Clear();
        var raw = await _recordingService.GetCaptureSources();
        foreach (var s in raw)
        {
            Sources.Add(new SourceItem
            {
                Name       = s.Name,
                SourceType = s.Id,
                Icon       = s.Type == CaptureSourceType.Screen ? "\uE7F8" : "\uE737",
            });
        }
        if (Sources.Count > 0)
        {
            SourcesList.SelectedIndex = 0;
            _selectedSourceId = Sources[0].SourceType;
        }
    }

    // ── Device probing ────────────────────────────────────────────────────────

    private async Task ProbeAndPopulateDevicesAsync()
    {
        try
        {
            SetStatus("Probing audio/video devices…");
            var devices = await _recordingService.ProbeDevicesAsync();

            // Populate webcam combo
            WebcamDeviceCombo.Items.Clear();
            WebcamDeviceCombo.Items.Add(new ComboBoxItem { Content = "(disabled)" });
            foreach (var v in devices.VideoDevices)
                WebcamDeviceCombo.Items.Add(new ComboBoxItem { Content = v });
            WebcamDeviceCombo.SelectedIndex = 0;

            // Populate mic combo (audio devices)
            MicDeviceCombo.Items.Clear();
            MicDeviceCombo.Items.Add(new ComboBoxItem { Content = "Default Microphone" });
            foreach (var a in devices.AudioDevices)
                MicDeviceCombo.Items.Add(new ComboBoxItem { Content = a });
            MicDeviceCombo.SelectedIndex = 0;

            // Add audio sources to mixer that were detected
            RebuildMixerFromDevices(devices);

            SetStatus($"Found {devices.VideoDevices.Count} video · {devices.AudioDevices.Count} audio devices");
        }
        catch (Exception ex)
        {
            SetStatus($"Device probe failed: {ex.Message}");
        }
    }

    // ── Audio Mixer ───────────────────────────────────────────────────────────

    private void BuildDefaultMixerChannels()
    {
        AudioMixerChannels.Children.Clear();
        _desktopChannel = null;
        _micChannel     = null;

        _desktopChannel = new AudioChannelView("Desktop Audio", isDesktop: true);
        _micChannel     = new AudioChannelView("Mic/Aux",        isMic:     true);

        AudioMixerChannels.Children.Add(_desktopChannel.BuildChannelStrip());
        AudioMixerChannels.Children.Add(_micChannel.BuildChannelStrip());
    }

    private void RebuildMixerFromDevices(DshowDeviceList devices)
    {
        // Only add extra channels for additional discovered audio devices
        // (desktop + mic channels already exist)
        // We don't rebuild to avoid losing user adjustments
    }

    private void AddMixerChannel(string name, bool isDesktop = false, bool isMic = false)
    {
        var ch = new AudioChannelView(name, isDesktop, isMic);
        AudioMixerChannels.Children.Add(ch.BuildChannelStrip());
    }

    // ── VU Metering timer ─────────────────────────────────────────────────────

    private void StartMeterTimer()
    {
        _meterTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _meterTimer.Tick += MeterTimer_Tick;
        _meterTimer.Start();
    }

    private void MeterTimer_Tick(object? sender, object e)
    {
        var dPeak = _audioMeter.GetDesktopPeak();
        var mPeak = _audioMeter.GetMicPeak();

        // Add slight stereo simulation (±5%)
        var rand = (float)(Random.Shared.NextDouble() * 0.05);

        _desktopChannel?.UpdatePeak(dPeak, Math.Max(0, dPeak - rand), VuMaxHeight);
        _micChannel?.UpdatePeak(mPeak + rand, mPeak, VuMaxHeight);
    }

    // ── Scene management ──────────────────────────────────────────────────────

    private void AddScene(string name)
    {
        var scene = new SceneItem { Name = name };
        Scenes.Add(scene);
        RebuildSceneQuickBar();
        if (Scenes.Count == 1) ScenesList.SelectedIndex = 0;
    }

    private void RebuildSceneQuickBar()
    {
        SceneQuickBar.Children.Clear();
        foreach (var s in Scenes)
        {
            var btn = new Button
            {
                Content = s.Name,
                Style   = (Style)Application.Current.Resources["AlvoniaGhostButtonStyle"],
                FontSize = 11,
                Padding = new Thickness(10, 4, 10, 4),
                CornerRadius = new CornerRadius(6),
                BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(80, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(180, 255, 255, 255)),
            };
            btn.Tag = s;
            btn.Click += SceneQuickBtn_Click;
            SceneQuickBar.Children.Add(btn);
        }
    }

    private void SceneQuickBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is SceneItem scene)
        {
            var idx = Scenes.IndexOf(scene);
            if (idx >= 0) ScenesList.SelectedIndex = idx;
        }
    }

    private void ScenesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Scene switch - in a full impl, would swap source list
    }

    private void AddSceneBtn_Click(object sender, RoutedEventArgs e)
    {
        AddScene($"Scene {Scenes.Count + 1}");
    }

    private void RemoveSceneBtn_Click(object sender, RoutedEventArgs e)
    {
        if (ScenesList.SelectedItem is SceneItem s && Scenes.Count > 1)
        {
            Scenes.Remove(s);
            RebuildSceneQuickBar();
        }
    }

    private void SceneUpBtn_Click(object sender, RoutedEventArgs e)
    {
        var idx = ScenesList.SelectedIndex;
        if (idx > 0)
        {
            var item = Scenes[idx];
            Scenes.RemoveAt(idx);
            Scenes.Insert(idx - 1, item);
            ScenesList.SelectedIndex = idx - 1;
            RebuildSceneQuickBar();
        }
    }

    private void SceneDownBtn_Click(object sender, RoutedEventArgs e)
    {
        var idx = ScenesList.SelectedIndex;
        if (idx >= 0 && idx < Scenes.Count - 1)
        {
            var item = Scenes[idx];
            Scenes.RemoveAt(idx);
            Scenes.Insert(idx + 1, item);
            ScenesList.SelectedIndex = idx + 1;
            RebuildSceneQuickBar();
        }
    }

    // ── Source management ─────────────────────────────────────────────────────

    private void SourcesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SourcesList.SelectedItem is SourceItem src)
        {
            _selectedSourceId = src.SourceType;
            PreviewHintText.Text = $"Source: {src.Name}";
        }
    }

    private async void AddSourceBtn_Click(object sender, RoutedEventArgs e)
    {
        // Show source type picker dialog
        var dialog = new ContentDialog
        {
            Title          = "Add Source",
            CloseButtonText = "Cancel",
            XamlRoot       = XamlRoot,
        };

        // Build a list of all available source types including audio
        var panel = new StackPanel { Spacing = 6, Width = 300 };
        panel.Children.Add(new TextBlock
        {
            Text = "Choose a source type to add:",
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["TextSecondaryBrush"],
            Margin = new Thickness(0, 0, 0, 8),
        });

        // Probe devices for the dialog
        DshowDeviceList? probed = null;
        try { probed = await _recordingService.ProbeDevicesAsync(); } catch { }

        string? chosen = null;
        string? chosenName = null;

        void AddSourceOption(string label, string icon, string type, string id)
        {
            var btn = new Button
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Style = (Style)Application.Current.Resources["ControlsPanelBtnStyle"],
            };
            var inner = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            inner.Children.Add(new FontIcon
            {
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                Glyph = icon,
                FontSize = 14,
            });
            inner.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
            btn.Content = inner;
            btn.Click += (_, _) =>
            {
                chosen     = type;
                chosenName = label;
                dialog.Hide();
            };
            panel.Children.Add(btn);
        }

        AddSourceOption("Primary Display",    "\uE7F8", "screen",     "screen:primary");
        AddSourceOption("All Displays",       "\uE780", "screen",     "screen:all");
        AddSourceOption("Window Capture",     "\uE737", "window",     "screen:window");
        AddSourceOption("Whiteboard Window",  "\uE70A", "whiteboard", "title=RecordIt Whiteboard");

        if (probed != null)
        {
            foreach (var v in probed.VideoDevices)
                AddSourceOption(v, "\uE714", "video", $"video={v}");
        }

        // Audio sources section
        panel.Children.Add(new Border
        {
            Height = 1,
            Background = (Brush)Application.Current.Resources["BorderDefaultBrush"],
            Margin = new Thickness(0, 4, 0, 4),
        });
        panel.Children.Add(new TextBlock
        {
            Text = "Audio Sources",
            FontSize = 10, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.Resources["TextTertiaryBrush"],
        });

        AddSourceOption("Desktop Audio (Loopback)", "\uE767", "audiout", "Desktop Audio (Loopback)");
        AddSourceOption("Default Microphone",       "\uE720", "audiin",  "Default Microphone");

        if (probed != null)
        {
            foreach (var a in probed.AudioDevices.Skip(1)) // skip "Desktop Audio (Loopback)" already added
                AddSourceOption(a, "\uE720", "audiin", a);
        }

        var sv = new ScrollViewer { MaxHeight = 340, Content = panel };
        dialog.Content = sv;
        await dialog.ShowAsync();

        if (chosen != null && chosenName != null)
        {
            var icon = chosen switch
            {
                "screen"     => "\uE7F8",
                "window"     => "\uE737",
                "video"      => "\uE714",
                "audiout"    => "\uE767",
                "audiin"     => "\uE720",
                "whiteboard" => "\uE70A",
                _            => "\uE7F8",
            };
            Sources.Add(new SourceItem { Name = chosenName, Icon = icon, SourceType = chosen });
            SourcesList.SelectedIndex = Sources.Count - 1;

            // If it's an audio source, add a mixer channel
            if (chosen is "audiout" or "audiin")
                AddMixerChannel(chosenName, isDesktop: chosen == "audiout", isMic: chosen == "audiin");
        }
    }

    private void RemoveSourceBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SourcesList.SelectedItem is SourceItem src)
            Sources.Remove(src);
    }

    private void SourceUpBtn_Click(object sender, RoutedEventArgs e)
    {
        var idx = SourcesList.SelectedIndex;
        if (idx > 0)
        {
            var item = Sources[idx];
            Sources.RemoveAt(idx);
            Sources.Insert(idx - 1, item);
            SourcesList.SelectedIndex = idx - 1;
        }
    }

    private void SourceDownBtn_Click(object sender, RoutedEventArgs e)
    {
        var idx = SourcesList.SelectedIndex;
        if (idx >= 0 && idx < Sources.Count - 1)
        {
            var item = Sources[idx];
            Sources.RemoveAt(idx);
            Sources.Insert(idx + 1, item);
            SourcesList.SelectedIndex = idx + 1;
        }
    }

    private async void SourceSettingsBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SourcesList.SelectedItem is not SourceItem src) return;
        var dlg = new ContentDialog
        {
            Title           = $"Properties: {src.Name}",
            Content         = new TextBlock { Text = $"Type: {src.SourceType}\nVisibility: {(src.IsVisible ? "Visible" : "Hidden")}", FontSize = 13 },
            CloseButtonText = "Close",
            XamlRoot        = XamlRoot,
        };
        await dlg.ShowAsync();
    }

    // ── Recording ─────────────────────────────────────────────────────────────

    private async void StartRecordBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedSourceId == null)
        {
            SetStatus("Select a capture source first");
            return;
        }

        // Pick save location
        var picker = new FileSavePicker();
        var hwnd   = WindowNativeInterop.GetWindowHandle(App.MainWindow!);
        InitializeWithWindow.Initialize(picker, hwnd);
        picker.SuggestedStartLocation = PickerLocationId.VideosLibrary;
        picker.SuggestedFileName      = $"RecordIt-{DateTime.Now:yyyyMMdd-HHmmss}";
        picker.FileTypeChoices.Add("MP4 Video",  new[] { ".mp4" });
        picker.FileTypeChoices.Add("WebM Video", new[] { ".webm" });

        var file = await picker.PickSaveFileAsync();
        if (file == null) return;

        _outputFilePath = file.Path;

        // Gather options
        var quality = QualityCombo.SelectedIndex switch
        {
            0 => "1920x1080",
            1 => "2560x1440",
            2 => "3840x2160",
            3 => "1280x720",
            4 => "854x480",
            _ => "1920x1080",
        };
        var fps = FpsCombo.SelectedIndex switch
        {
            0 => 60, 1 => 30, 2 => 24, 3 => 15, _ => 30,
        };

        var webcamSel  = (WebcamDeviceCombo.SelectedItem as ComboBoxItem)?.Content?.ToString();
        var micSel     = (MicDeviceCombo.SelectedItem    as ComboBoxItem)?.Content?.ToString();
        var hasWebcam  = webcamSel is not null && webcamSel != "(disabled)";
        var hasMic     = micSel    is not null && micSel    != "Default Microphone";
        var micDevice  = hasMic   ? micSel    : null;
        var camDevice  = hasWebcam ? webcamSel : null;

        // Mic volume from mixer channel
        float micVol = _micChannel?.IsMuted == true ? 0f : (float)(_micChannel?.Volume ?? 1.0);

        try
        {
            SetStatus("Starting recording…");
            await _recordingService.StartRecording(
                _selectedSourceId,
                _outputFilePath,
                quality, fps,
                includeMic:    true,    // always try
                includeWebcam: hasWebcam,
                webcamDevice:  camDevice,
                audioDevice:   micDevice,
                micVolume:     micVol);

            StartRecordBtn.Visibility = Visibility.Collapsed;
            StopRecordBtn.Visibility  = Visibility.Visible;
            MenuStartRecord.IsEnabled = false;
            MenuStopRecord.IsEnabled  = true;
            RecordingBadge.Visibility = Visibility.Visible;
            SetStatus("Recording…");
            App.MainWindow?.StartRecordingIndicator();
        }
        catch (Exception ex)
        {
            SetStatus($"Failed to start: {ex.Message}");
            var dlg = new ContentDialog
            {
                Title           = "Recording Error",
                Content         = ex.Message,
                CloseButtonText = "OK",
                XamlRoot        = XamlRoot,
            };
            await dlg.ShowAsync();
        }
    }

    private async void StopRecordBtn_Click(object sender, RoutedEventArgs e)
    {
        await _recordingService.StopRecording();

        StartRecordBtn.Visibility = Visibility.Visible;
        StopRecordBtn.Visibility  = Visibility.Collapsed;
        MenuStartRecord.IsEnabled = true;
        MenuStopRecord.IsEnabled  = false;
        RecordingBadge.Visibility = Visibility.Collapsed;
        App.MainWindow?.StopRecordingIndicator();
        SetStatus("Recording stopped");

        if (_outputFilePath != null && File.Exists(_outputFilePath))
        {
            // ── Optional: extract separate MP3 audio track ────────────────
            if (SepAudioCheckBox.IsChecked == true)
                await ExtractSepAudioAsync(_outputFilePath);

            var dlg = new ContentDialog
            {
                Title               = "Recording Saved",
                Content             = $"Saved to:\n{_outputFilePath}",
                PrimaryButtonText   = "Open Folder",
                CloseButtonText     = "OK",
                XamlRoot            = XamlRoot,
            };
            var result = await dlg.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                var dir = Path.GetDirectoryName(_outputFilePath) ?? "";
                if (Directory.Exists(dir))
                    Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"")
                    { UseShellExecute = true });
            }
        }
    }

    /// <summary>
    /// Reads the Mp3BitrateCombo selection and runs FFmpeg to pull the audio
    /// out of <paramref name="videoPath"/> as one or more MP3 files alongside it.
    /// Shows a brief status message while working.
    /// </summary>
    private async Task ExtractSepAudioAsync(string videoPath)
    {
        SetStatus("Extracting audio…");

        var export  = new RecordIt.Core.Services.ExportService();
        var outDir  = Path.GetDirectoryName(videoPath) ?? "";
        var stem    = Path.GetFileNameWithoutExtension(videoPath);

        try
        {
            // Resolve selected bitrate(s)
            var tag = (Mp3BitrateCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "192";

            string[] mp3Paths;
            if (tag == "all")
            {
                mp3Paths = await export.ExtractAudioMultiBitrateAsync(
                    videoPath, outDir, new[] { 128, 192, 256, 320 });
            }
            else
            {
                int br = int.TryParse(tag, out var b) ? b : 192;
                var dest = Path.Combine(outDir, $"{stem}_{br}kbps.mp3");
                await export.ExtractAudioAsync(videoPath, dest, br);
                mp3Paths = new[] { dest };
            }

            var saved = string.Join("\n", mp3Paths.Select(p => Path.GetFileName(p)));
            var dlg = new ContentDialog
            {
                Title             = "Audio Extracted",
                Content           = $"MP3 file(s) saved:\n{saved}",
                PrimaryButtonText = "Open Folder",
                CloseButtonText   = "OK",
                XamlRoot          = XamlRoot,
            };
            var r = await dlg.ShowAsync();
            if (r == ContentDialogResult.Primary && Directory.Exists(outDir))
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{outDir}\"")
                    { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            SetStatus($"Audio extraction failed: {ex.Message}");
        }
        finally
        {
            SetStatus("Ready");
        }
    }

    // ── Preview ───────────────────────────────────────────────────────────────

    private void StartPreviewBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_previewProcess is { HasExited: false }) return;
            var psi = new ProcessStartInfo
            {
                FileName        = "ffplay",
                Arguments       = "-f gdigrab -framerate 30 -i desktop -window_title \"RecordIt Preview\"",
                UseShellExecute = false,
                CreateNoWindow  = false,
            };
            _previewProcess = Process.Start(psi);
            StartPreviewBtn.Visibility = Visibility.Collapsed;
            StopPreviewBtn.Visibility  = Visibility.Visible;
            SetStatus("Preview started");
        }
        catch (Exception ex)
        {
            SetStatus($"Preview error: {ex.Message}");
        }
    }

    private void StopPreviewBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_previewProcess is { HasExited: false })
                _previewProcess.Kill(entireProcessTree: true);
        }
        catch { }
        finally
        {
            _previewProcess = null;
            StartPreviewBtn.Visibility = Visibility.Visible;
            StopPreviewBtn.Visibility  = Visibility.Collapsed;
            SetStatus("Preview stopped");
        }
    }

    // ── Audio track management ────────────────────────────────────────────────

    private async void AddAudioTrackBtn_Click(object sender, RoutedEventArgs e)
    {
        // Re-probe audio devices and let user pick one for an extra mixer channel
        try
        {
            var devices = await _recordingService.ProbeDevicesAsync();
            var options = devices.AudioDevices;

            if (options.Count == 0)
            {
                SetStatus("No audio devices found");
                return;
            }

            var panel = new StackPanel { Spacing = 4 };
            string? picked = null;
            ContentDialog? dialog = null;

            dialog = new ContentDialog
            {
                Title           = "Add Audio Source",
                CloseButtonText = "Cancel",
                XamlRoot        = XamlRoot,
            };

            foreach (var opt in options)
            {
                var btn = new Button
                {
                    Content = opt,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Style   = (Style)Application.Current.Resources["ControlsPanelBtnStyle"],
                    FontSize = 12,
                };
                btn.Click += (_, _) => { picked = opt; dialog!.Hide(); };
                panel.Children.Add(btn);
            }
            dialog.Content = panel;
            await dialog.ShowAsync();

            if (picked != null)
            {
                bool isDesktop = picked.Contains("Loopback", StringComparison.OrdinalIgnoreCase)
                              || picked.Contains("virtual",  StringComparison.OrdinalIgnoreCase)
                              || picked.Contains("Desktop",  StringComparison.OrdinalIgnoreCase);
                AddMixerChannel(picked, isDesktop: isDesktop, isMic: !isDesktop);
                SetStatus($"Added: {picked}");
            }
        }
        catch (Exception ex)
        {
            SetStatus($"Error: {ex.Message}");
        }
    }

    private void ClearAudioTracksBtn_Click(object sender, RoutedEventArgs e)
    {
        // Keep first two (Desktop + Mic) and remove extra channels
        while (AudioMixerChannels.Children.Count > 2)
            AudioMixerChannels.Children.RemoveAt(AudioMixerChannels.Children.Count - 1);
    }

    // ── Menu handlers ─────────────────────────────────────────────────────────

    private void MenuNewScene_Click(object sender, RoutedEventArgs e)      => AddScene($"Scene {Scenes.Count + 1}");
    private void MenuOpenScene_Click(object sender, RoutedEventArgs e)     => SetStatus("Open scene collection not implemented");
    private void MenuExport_Click(object sender, RoutedEventArgs e)        => ExportBtn_Click(sender, e);
    private void MenuExit_Click(object sender, RoutedEventArgs e)          => App.MainWindow?.Close();
    private void MenuSettings_Click(object sender, RoutedEventArgs e)      => App.MainWindow?.NavigateTo("settings");
    private void MenuWhiteboard_Click(object sender, RoutedEventArgs e)    => App.MainWindow?.NavigateTo("whiteboard");
    private void MenuLibrary_Click(object sender, RoutedEventArgs e)       => App.MainWindow?.NavigateTo("library");
    private void MenuAbout_Click(object sender, RoutedEventArgs e)         => ShowAbout();
    private void MenuFullscreen_Click(object sender, RoutedEventArgs e)    => SetStatus("Fullscreen preview not yet implemented");
    private void MenuLockDocks_Click(object sender, RoutedEventArgs e)     => SetStatus("Dock locking not yet implemented");
    private void MenuResetLayout_Click(object sender, RoutedEventArgs e)   => ResetPanelWidths();

    private void MenuTogglePanel_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleMenuFlyoutItem item) return;
        var tag = item.Tag?.ToString() ?? "";
        var visible = item.IsChecked ? Visibility.Visible : Visibility.Collapsed;

        switch (tag)
        {
            case "scenes":      ScenesPanel.Visibility      = visible; break;
            case "sources":     SourcesPanel.Visibility     = visible; break;
            case "mixer":       MixerPanel.Visibility       = visible; break;
            case "transitions": TransitionsPanel.Visibility = visible; break;
            case "controls":    ControlsPanel.Visibility    = visible; break;
        }
    }

    private void ResetPanelWidths()
    {
        ScenesColumn.Width   = new GridLength(175);
        SourcesColumn.Width  = new GridLength(200);
        MixerColumn.Width    = new GridLength(1, GridUnitType.Star);
        TransColumn.Width    = new GridLength(155);
        ControlsColumn.Width = new GridLength(155);
    }

    // ── Probe / Refresh ───────────────────────────────────────────────────────

    private void RefreshSourcesBtn_Click(object sender, RoutedEventArgs e) => _ = LoadCaptureSources();

    private void ProbeDevicesBtn_Click(object sender, RoutedEventArgs e)   => _ = ProbeAndPopulateDevicesAsync();

    // ── Export / Library ──────────────────────────────────────────────────────

    private void ExportBtn_Click(object sender, RoutedEventArgs e)
        => App.MainWindow?.NavigateTo("library");

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void SetStatus(string text) => StatusText.Text = text;

    private async void ShowAbout()
    {
        var dlg = new ContentDialog
        {
            Title           = "RecordIt",
            Content         = "OBS-style screen recorder built with WinUI 3 / .NET 10.\n\nVersion 1.0 · Alvonia",
            CloseButtonText = "OK",
            XamlRoot        = XamlRoot,
        };
        await dlg.ShowAsync();
    }

    // ── IDisposable ───────────────────────────────────────────────────────────

    public void Dispose()
    {
        _meterTimer?.Stop();
        _audioMeter.Dispose();
        if (_previewProcess is { HasExited: false })
            try { _previewProcess.Kill(true); } catch { }
    }
}
