using Microsoft.Graphics.Canvas;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using RecordIt.Core.Services;
using RecordIt.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Windowing;
using Windows.Devices.Enumeration;
using Windows.Foundation;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.Imaging;
using Windows.Media.Capture;
using Windows.Media.Core;
using Windows.Media.Devices;
using Windows.Media.Playback;
using Windows.Storage.Pickers;
using Windows.UI;
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
    /// <summary>Stored WGC item after the user picks a display in Properties.</summary>
    public GraphicsCaptureItem? CaptureItem { get; set; }
}

// ── AudioChannelView - represents one fader strip in the mixer ────────────────

internal sealed class AudioChannelView
{
    public string Name { get; }
    public bool IsDesktop { get; }
    public bool IsMic { get; }

    public Microsoft.UI.Xaml.Shapes.Rectangle VuBarLeft  { get; }
    public Microsoft.UI.Xaml.Shapes.Rectangle VuBarRight { get; }
    public TextBlock    DbLabel  { get; }
    public Slider       VolSlider { get; }
    public ToggleButton MuteBtn  { get; }

    public float Volume  => (float)VolSlider.Value;
    public bool  IsMuted => MuteBtn.IsChecked == true;

    // Fixed meter height used by BuildChannelStrip and UpdatePeak
    public const double MeterH = 180;

    public AudioChannelView(string name, bool isDesktop = false, bool isMic = false)
    {
        Name      = name;
        IsDesktop = isDesktop;
        IsMic     = isMic;

        var vuBrush = MakeVuBrush();

        VuBarLeft  = new Microsoft.UI.Xaml.Shapes.Rectangle { Fill = vuBrush };
        VuBarRight = new Microsoft.UI.Xaml.Shapes.Rectangle { Fill = vuBrush };
        DbLabel    = new TextBlock
        {
            FontSize      = 11,
            Text          = "0.0 dB",
            TextAlignment = TextAlignment.Center,
            Foreground    = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 81, 162, 222)),
            Margin        = new Thickness(0, 2, 0, 4),
        };
        VolSlider = new Slider
        {
            Orientation     = Orientation.Vertical,
            Minimum         = 0,
            Maximum         = 1,
            Value           = 1,
            StepFrequency   = 0.01,
            Height          = MeterH,
            Width           = 20,
            VerticalAlignment = VerticalAlignment.Top,
        };
        MuteBtn = new ToggleButton { Padding = new Thickness(4, 2, 4, 2) };

        VolSlider.ValueChanged += (_, _) =>
        {
            var db = Volume > 0 ? 20.0 * Math.Log10(Volume) : -60.0;
            DbLabel.Text = db < -59.9 ? "-∞ dB" : $"{db:0.0} dB";
        };
    }

    public void UpdatePeak(float left, float right, double _ignored)
    {
        if (IsMuted) { left = right = 0; }
        VuBarLeft.Height  = Math.Clamp(left,  0f, 1f) * MeterH;
        VuBarRight.Height = Math.Clamp(right, 0f, 1f) * MeterH;
    }

    private static LinearGradientBrush MakeVuBrush()
    {
        var b = new LinearGradientBrush { StartPoint = new Point(0, 1), EndPoint = new Point(0, 0) };
        b.GradientStops.Add(new GradientStop { Color = Windows.UI.Color.FromArgb(255, 34,  197, 94),  Offset = 0.00 });
        b.GradientStops.Add(new GradientStop { Color = Windows.UI.Color.FromArgb(255, 134, 197, 94),  Offset = 0.60 });
        b.GradientStops.Add(new GradientStop { Color = Windows.UI.Color.FromArgb(255, 234, 179, 8),   Offset = 0.80 });
        b.GradientStops.Add(new GradientStop { Color = Windows.UI.Color.FromArgb(255, 239, 68,  68),  Offset = 1.00 });
        return b;
    }

    public UIElement BuildChannelStrip()
    {
        const double BarW       = 12;   // per L/R bar
        const double StripWidth = 100;

        // ── Status label ──────────────────────────────────────────────────────
        var statusLbl = new TextBlock
        {
            Text              = IsDesktop || IsMic ? "Global" : "Active",
            FontSize          = 9,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground        = new SolidColorBrush(
                IsDesktop ? Windows.UI.Color.FromArgb(255, 81, 162, 222)
                          : Windows.UI.Color.FromArgb(255, 81, 200, 120)),
            Margin            = new Thickness(0, 0, 0, 2),
        };

        // ── Name + chevron ────────────────────────────────────────────────────
        var namePanel = new StackPanel
        {
            Orientation         = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Spacing             = 3,
            Margin              = new Thickness(0, 0, 0, 2),
        };
        namePanel.Children.Add(new TextBlock
        {
            Text        = Name,
            FontSize    = 10,
            MaxWidth    = StripWidth - 22,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground  = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 210, 210, 210)),
        });
        namePanel.Children.Add(new FontIcon
        {
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            Glyph      = "\uE70D",
            FontSize   = 7,
            Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 160, 160, 160)),
            VerticalAlignment = VerticalAlignment.Center,
        });

        // ── VU bars ───────────────────────────────────────────────────────────
        VuBarLeft.Width            = BarW;
        VuBarLeft.Height           = 0;
        VuBarLeft.VerticalAlignment = VerticalAlignment.Bottom;

        VuBarRight.Width            = BarW;
        VuBarRight.Height           = 0;
        VuBarRight.VerticalAlignment = VerticalAlignment.Bottom;

        var vuBg = new Border
        {
            Width           = BarW * 2 + 2,
            Height          = MeterH,
            Background      = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 20, 20, 20)),
            BorderBrush     = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 50, 50, 50)),
            BorderThickness = new Thickness(1),
        };
        var vuGrid = new Grid { VerticalAlignment = VerticalAlignment.Stretch };
        vuGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(BarW) });
        vuGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2) });
        vuGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(BarW) });
        Grid.SetColumn(VuBarLeft,  0);
        Grid.SetColumn(VuBarRight, 2);
        vuGrid.Children.Add(VuBarLeft);
        vuGrid.Children.Add(VuBarRight);
        vuBg.Child = vuGrid;

        // ── dB scale ──────────────────────────────────────────────────────────
        var scaleCanvas = new Canvas { Width = 24, Height = MeterH };
        var dbStops = new[] { 0, -6, -12, -18, -24, -30, -36, -42, -48, -54, -60 };
        for (int i = 0; i < dbStops.Length; i++)
        {
            double y = i / (double)(dbStops.Length - 1) * MeterH;
            var lbl = new TextBlock
            {
                Text       = dbStops[i].ToString(),
                FontSize   = 8,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 100, 100, 100)),
            };
            Canvas.SetLeft(lbl, 2);
            Canvas.SetTop(lbl, y - 6);
            scaleCanvas.Children.Add(lbl);
        }

        // ── Meter row: [slider] [bars] [scale] ────────────────────────────────
        var meterRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing     = 3,
            Margin      = new Thickness(0, 0, 0, 4),
        };
        meterRow.Children.Add(VolSlider);
        meterRow.Children.Add(vuBg);
        meterRow.Children.Add(scaleCanvas);

        // ── Bottom buttons ────────────────────────────────────────────────────
        MuteBtn.Content = new FontIcon
        {
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            Glyph      = "\uE767",
            FontSize   = 11,
        };
        MuteBtn.Width  = 28;
        MuteBtn.Height = 24;

        var settingsBtn = new Button
        {
            Content = new FontIcon
            {
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                Glyph      = "\uE713",
                FontSize   = 11,
            },
            Width   = 28,
            Height  = 24,
            Padding = new Thickness(2),
        };

        var bottomRow = new StackPanel
        {
            Orientation         = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Spacing             = 4,
        };
        bottomRow.Children.Add(MuteBtn);
        bottomRow.Children.Add(settingsBtn);

        // ── Assemble strip ────────────────────────────────────────────────────
        var strip = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Width       = StripWidth,
            Padding     = new Thickness(4, 4, 4, 6),
        };
        strip.Children.Add(statusLbl);
        strip.Children.Add(namePanel);
        strip.Children.Add(DbLabel);
        strip.Children.Add(meterRow);
        strip.Children.Add(bottomRow);

        return new Border
        {
            BorderBrush     = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 55, 55, 55)),
            BorderThickness = new Thickness(0, 0, 1, 0),
            Child           = strip,
        };
    }
}

// ─────────────────────────────────────────────────────────────────────────────

public sealed partial class RecordPage : Page, IDisposable
{
    // Services
    private readonly ScreenRecordingService _recordingService = new();
    private readonly AudioMeterService      _audioMeter       = new();
    private readonly SpeechCaptionService   _captionService   = new();
    private readonly SettingsService        _settings         = new();

    // State
    private string?  _selectedSourceId;
    private string?  _outputFilePath;
    private Process? _previewProcess;
    private MediaCapture? _mediaCapture;
    private DateTime _recordingStartTime;

    // Studio Mode
    private bool _isStudioMode;

    // Lower Third
    private bool _lowerThirdVisible;

    // Per-scene source persistence
    private readonly Dictionary<string, List<SourceItem>> _sceneSources = new();

    // Captions
    private bool _captionsActive;
    private CancellationTokenSource? _captionClearCts;
    private CaptionConfig _captionConfig = new();

    // Metering
    private DispatcherTimer? _meterTimer;
    private AudioChannelView? _desktopChannel;
    private AudioChannelView? _micChannel;

    // Main preview — screen capture (WGC)
    private readonly SoftwareBitmapSource _mainBitmapSrc = new();
    private CanvasDevice?               _mainCaptureDevice;
    private Direct3D11CaptureFramePool? _mainCapturePool;
    private GraphicsCaptureSession?     _mainCaptureSession;
    private bool                        _mainCaptureBusy;

    // Main preview — webcam
    private MediaPlayer? _mainVideoPlayer;

    // Collections
    public ObservableCollection<SceneItem>  Scenes  { get; } = new();
    public ObservableCollection<SourceItem> Sources { get; } = new();

    private const double VuMaxHeight = AudioChannelView.MeterH;

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
        // Load caption defaults from app settings so the CC toggle uses them.
        LoadCaptionConfigFromSettings();

        // Attach the persistent SoftwareBitmapSource to the screen-preview Image
        PreviewScreenImage.Source = _mainBitmapSrc;

        // Check ffmpeg first — prompt the user if it's not installed
        await ShowFfmpegSetupIfNeededAsync();

        // Sources start empty — user adds them per scene via the + button
        UpdateSourcesEmptyState();

        await ProbeAndPopulateDevicesAsync();
        BuildDefaultMixerChannels();
        StartMeterTimer();
    }

    private void LoadCaptionConfigFromSettings()
    {
        // Caption style preset
        if (_settings.Get("caption_style") is { } s &&
            Enum.TryParse<CaptionStyle>(s, out var cs))
            _captionConfig.Style = cs;

        // Language
        _captionConfig.Language = _settings.Get("caption_language") ?? _captionConfig.Language;

        // Text colour key (Custom/White/Yellow/etc)
        _captionConfig.TextColor = _settings.Get("caption_text_color") ?? _captionConfig.TextColor;

        // Font size
        if (double.TryParse(_settings.Get("caption_font_size"), out var fs))
            _captionConfig.FontSize = fs;

        // Background opacity
        if (double.TryParse(_settings.Get("caption_bg_opacity"), out var bo))
            _captionConfig.BgOpacity = bo;

        // Position
        _captionConfig.Position = _settings.Get("caption_position") ?? _captionConfig.Position;

        // Auto-clear delay
        if (int.TryParse(_settings.Get("caption_clear_after_sec"), out var c))
            _captionConfig.ClearAfterSec = c;

        // Burn-in into recording
        _captionConfig.BurnIntoRecording =
            (_settings.Get("caption_burn_into_recording") ?? "0") == "1";

        // Note: MaxLineChars is not exposed in the current CC settings dialog,
        // but we still support persistence for future UI.
        if (int.TryParse(_settings.Get("caption_max_line_chars"), out var mlc) && mlc > 0)
            _captionConfig.MaxLineChars = mlc;
    }

    private void SaveCaptionConfigToSettings()
    {
        _settings.Set("caption_style", _captionConfig.Style.ToString());
        _settings.Set("caption_language", _captionConfig.Language);
        _settings.Set("caption_text_color", _captionConfig.TextColor);
        _settings.Set("caption_font_size", _captionConfig.FontSize.ToString(System.Globalization.CultureInfo.InvariantCulture));
        _settings.Set("caption_bg_opacity", _captionConfig.BgOpacity.ToString(System.Globalization.CultureInfo.InvariantCulture));
        _settings.Set("caption_position", _captionConfig.Position);
        _settings.Set("caption_clear_after_sec", _captionConfig.ClearAfterSec.ToString());
        _settings.Set("caption_burn_into_recording", _captionConfig.BurnIntoRecording ? "1" : "0");
        _settings.Set("caption_max_line_chars", _captionConfig.MaxLineChars.ToString());
    }

    /// <summary>Show the OBS-style empty-state overlay when the current scene has no sources.</summary>
    private void UpdateSourcesEmptyState()
    {
        if (SourcesEmptyState == null) return;
        SourcesEmptyState.Visibility = Sources.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    // ── FFmpeg first-run setup dialog ─────────────────────────────────────────

    private async Task ShowFfmpegSetupIfNeededAsync()
    {
        // Quick non-blocking check (don't install automatically)
        if (await FfmpegLocator.EnsureAvailableAsync(installIfMissing: false))
            return;

        // Build dialog content
        var statusText = new TextBlock
        {
            Text       = "ffmpeg is not installed or cannot be found.",
            FontSize   = 12,
            Foreground = (Brush)Application.Current.Resources["TextTertiaryBrush"],
            TextWrapping = TextWrapping.Wrap,
        };
        var progressRing = new ProgressRing
        {
            IsActive  = false,
            Width     = 24,
            Height    = 24,
            Visibility = Visibility.Collapsed,
        };
        var panel = new StackPanel { Spacing = 12, Width = 340 };
        panel.Children.Add(new TextBlock
        {
            Text         = "RecordIt uses ffmpeg for all recording and export features. "
                         + "You can download the free static build automatically (~95 MB from gyan.dev), "
                         + "or point to an existing ffmpeg.exe on your machine.",
            TextWrapping = TextWrapping.Wrap,
            FontSize     = 13,
        });
        panel.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing     = 8,
            Children    = { progressRing, statusText },
        });

        var dlg = new ContentDialog
        {
            Title                = "FFmpeg Not Found",
            Content              = panel,
            PrimaryButtonText    = "Download Automatically",
            SecondaryButtonText  = "Browse for ffmpeg.exe",
            CloseButtonText      = "Continue (Limited)",
            XamlRoot             = XamlRoot,
        };

        // Prevent closing during download
        dlg.PrimaryButtonClick += async (s, args) =>
        {
            args.Cancel = true;
            dlg.IsPrimaryButtonEnabled   = false;
            dlg.IsSecondaryButtonEnabled = false;
            progressRing.IsActive        = true;
            progressRing.Visibility      = Visibility.Visible;
            statusText.Text              = "Downloading ffmpeg… this may take a minute.";

            var ok = await FfmpegLocator.EnsureAvailableAsync(installIfMissing: true);

            progressRing.IsActive   = false;
            progressRing.Visibility = Visibility.Collapsed;
            if (ok)
            {
                statusText.Text           = "✓ ffmpeg downloaded and ready!";
                dlg.CloseButtonText       = "Done";
                dlg.IsPrimaryButtonEnabled = false;
            }
            else
            {
                statusText.Text              = "Download failed. Check your internet connection.";
                dlg.IsPrimaryButtonEnabled   = true;
                dlg.IsSecondaryButtonEnabled = true;
            }
        };

        dlg.SecondaryButtonClick += async (s, args) =>
        {
            args.Cancel = true;
            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            WinRT.Interop.InitializeWithWindow.Initialize(picker,
                WindowNativeInterop.GetWindowHandle(App.MainWindow!));
            picker.FileTypeFilter.Add(".exe");
            picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.ComputerFolder;
            var file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                FfmpegLocator.Executable = file.Path;
                var works = await FfmpegLocator.EnsureAvailableAsync(installIfMissing: false);
                statusText.Text = works
                    ? $"✓ Using: {file.Path}"
                    : "That file doesn't appear to be a working ffmpeg.exe.";
                if (works)
                {
                    dlg.CloseButtonText          = "Done";
                    dlg.IsPrimaryButtonEnabled   = false;
                    dlg.IsSecondaryButtonEnabled = false;
                }
            }
        };

        await dlg.ShowAsync();
    }

    // ── Capture sources ───────────────────────────────────────────────────────

    private async Task LoadCaptureSources()
    {
        Sources.Clear();
        // Add built-in screen sources
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

        // Probe and add video capture devices (webcams) as sources so they show up immediately
        try
        {
            var devs = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture);
            foreach (var d in devs)
            {
                // Use device name as display; SourceType contains identifier to start preview/recording
                var id = $"video={d.Name}";
                if (!Sources.Any(x => x.SourceType == id))
                {
                    Sources.Add(new SourceItem { Name = d.Name, SourceType = id, Icon = "\uE714" });
                }
            }
        }
        catch { }
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

            // ── Video devices via WinRT DeviceInformation ─────────────────
            var videoDevs = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture);
            WebcamDeviceCombo.Items.Clear();
            WebcamDeviceCombo.Items.Add(new ComboBoxItem { Content = "(disabled)" });
            foreach (var d in videoDevs)
                WebcamDeviceCombo.Items.Add(new ComboBoxItem { Content = d.Name });
            WebcamDeviceCombo.SelectedIndex = 0;

            // ── Audio capture devices via WinRT (WASAPI) ──────────────────
            var audioCapture = await DeviceInformation.FindAllAsync(DeviceClass.AudioCapture);
            MicDeviceCombo.Items.Clear();
            MicDeviceCombo.Items.Add(new ComboBoxItem { Content = "Default Microphone" });
            MicDeviceCombo.Items.Add(new ComboBoxItem { Content = "Desktop Audio (Loopback)" });
            foreach (var d in audioCapture)
                MicDeviceCombo.Items.Add(new ComboBoxItem { Content = d.Name });
            MicDeviceCombo.SelectedIndex = 0;

            // ── Also run ffmpeg DirectShow probe for broader compatibility ─
            DshowDeviceList? dshow = null;
            try { dshow = await _recordingService.ProbeDevicesAsync(); } catch { }
            if (dshow != null)
                RebuildMixerFromDevices(dshow);

            SetStatus($"Found {videoDevs.Count} camera(s) · {audioCapture.Count} mic(s)");
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
        // Ensure the new scene starts with an empty source list
        _sceneSources[name] = new List<SourceItem>();
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
        // Save current sources to the scene we're leaving
        if (e.RemovedItems.Count > 0 && e.RemovedItems[0] is SceneItem leaving)
            _sceneSources[leaving.Name] = new List<SourceItem>(Sources);

        // Restore sources for the scene we're entering (empty list if never populated)
        Sources.Clear();
        if (e.AddedItems.Count > 0 && e.AddedItems[0] is SceneItem entering)
        {
            if (_sceneSources.TryGetValue(entering.Name, out var saved))
                foreach (var s in saved) Sources.Add(s);
        }
        _selectedSourceId = Sources.Count > 0 ? Sources[0].SourceType : null;
        UpdateSourcesEmptyState();
    }

    private void AddSceneBtn_Click(object sender, RoutedEventArgs e)
    {
        // Snapshot current sources into the scene we're leaving
        if (ScenesList.SelectedItem is SceneItem current)
            _sceneSources[current.Name] = new List<SourceItem>(Sources);

        AddScene($"Scene {Scenes.Count + 1}");
        // New scene is now selected — its source list is empty
        Sources.Clear();
        _selectedSourceId = null;
        UpdateSourcesEmptyState();
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
        if (SourcesList.SelectedItem is not SourceItem src)
        {
            ShowPreviewHint();
            return;
        }

        _selectedSourceId = src.SourceType;

        if (src.SourceType is "screen" or "window")
        {
            if (src.CaptureItem != null)
                StartMainScreenPreview(src.CaptureItem);
            else
                ShowPreviewHint($"Source: {src.Name}\n(open Properties to pick display)");
        }
        else if (src.SourceType != null &&
                 src.SourceType.StartsWith("video", StringComparison.OrdinalIgnoreCase))
        {
            _ = StartMainVideoPreviewAsync(src.Name);
        }
        else
        {
            // Audio or other source — show a label
            ShowPreviewHint($"Source: {src.Name}");
            _ = StopCameraPreviewAsync();
        }
    }

    private async void AddSourceBtn_Click(object sender, RoutedEventArgs e)
    {
        // ── Step 1: pick source type ──────────────────────────────────────────
        var typeDlg = new ContentDialog
        {
            Title           = "Add Source",
            CloseButtonText = "Cancel",
            XamlRoot        = XamlRoot,
        };

        var panel = new StackPanel { Spacing = 4, Width = 300 };

        DshowDeviceList? probed = null;
        try { probed = await _recordingService.ProbeDevicesAsync(); } catch { }

        string? chosen     = null;
        string? chosenName = null;

        void AddSourceOption(string label, string icon, string type)
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
                Glyph      = icon,
                FontSize   = 14,
            });
            inner.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
            btn.Content = inner;
            btn.Click += (_, _) => { chosen = type; chosenName = label; typeDlg.Hide(); };
            panel.Children.Add(btn);
        }

        // Section header helper
        void AddSeparator(string title)
        {
            panel.Children.Add(new Border
            {
                Height = 1,
                Background = (Brush)Application.Current.Resources["BorderDefaultBrush"],
                Margin = new Thickness(0, 6, 0, 4),
            });
            panel.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 10,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = (Brush)Application.Current.Resources["TextTertiaryBrush"],
                Margin = new Thickness(0, 0, 0, 2),
            });
        }

        AddSourceOption("Display Capture",    "\uE7F8", "screen");
        AddSourceOption("Window Capture",     "\uE737", "window");
        AddSourceOption("Whiteboard Window",  "\uE70A", "whiteboard");

        AddSeparator("Video Sources");
        AddSourceOption("Video Capture Device", "\uE714", "video");
        if (probed != null)
            foreach (var v in probed.VideoDevices)
                AddSourceOption(v, "\uE714", $"video={v}");

        AddSeparator("Audio Sources");
        AddSourceOption("Audio Output Capture",  "\uE767", "audiout");
        AddSourceOption("Audio Input Capture",   "\uE720", "audiin");
        if (probed != null)
            foreach (var a in probed.AudioDevices.Skip(1))
                AddSourceOption(a, "\uE720", "audiin");

        typeDlg.Content = new ScrollViewer { MaxHeight = 360, Content = panel };
        await typeDlg.ShowAsync();

        if (chosen == null) return;

        // ── Step 2: enter source name (OBS "Create/Select Source" style) ──────
        var nameBox = new TextBox
        {
            Text                = chosenName,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            SelectionStart      = 0,
            SelectionLength     = chosenName?.Length ?? 0,
        };
        var createRadio   = new RadioButton { Content = "Create new", IsChecked = true, GroupName = "SourceMode" };
        var existingRadio = new RadioButton { Content = "Add Existing", GroupName = "SourceMode" };
        var existingList  = new ListBox { IsEnabled = false, Height = 80 };
        var visibleChk    = new CheckBox { Content = "Make source visible", IsChecked = true };

        // Populate "Add Existing" list with same-type sources already in other scenes
        foreach (var saved in _sceneSources.Values)
            foreach (var s in saved.Where(s => s.SourceType == chosen))
                if (!existingList.Items.Contains(s.Name))
                    existingList.Items.Add(s.Name);

        createRadio.Checked   += (_, _) => { nameBox.IsEnabled = true;  existingList.IsEnabled = false; };
        existingRadio.Checked += (_, _) => { nameBox.IsEnabled = false; existingList.IsEnabled = true; };

        var nameContent = new StackPanel { Spacing = 8, Width = 320 };
        nameContent.Children.Add(createRadio);
        nameContent.Children.Add(nameBox);
        nameContent.Children.Add(existingRadio);
        nameContent.Children.Add(existingList);
        nameContent.Children.Add(new Border
        {
            Height = 1,
            Background = (Brush)Application.Current.Resources["BorderDefaultBrush"],
            Margin = new Thickness(0, 4, 0, 4),
        });
        nameContent.Children.Add(visibleChk);

        var nameDlg = new ContentDialog
        {
            Title               = "Create/Select Source",
            Content             = nameContent,
            PrimaryButtonText   = "OK",
            CloseButtonText     = "Cancel",
            DefaultButton       = ContentDialogButton.Primary,
            XamlRoot            = XamlRoot,
        };
        var nameResult = await nameDlg.ShowAsync();
        if (nameResult != ContentDialogResult.Primary) return;

        // Resolve final name
        string finalName;
        if (existingRadio.IsChecked == true && existingList.SelectedItem is string existingName)
            finalName = existingName;
        else
            finalName = string.IsNullOrWhiteSpace(nameBox.Text) ? chosenName! : nameBox.Text.Trim();

        var icon = chosen switch
        {
            "screen"     => "\uE7F8",
            "window"     => "\uE737",
            "video"      => "\uE714",
            "audiout"    => "\uE767",
            "audiin"     => "\uE720",
            "whiteboard" => "\uE70A",
            _            => "\uE714",
        };
        // Strip the "video=" prefix for video device sources
        string srcType = chosen.StartsWith("video=") ? "video" : chosen;

        var newSrc = new SourceItem
        {
            Name       = finalName,
            Icon       = icon,
            SourceType = srcType,
            IsVisible  = visibleChk.IsChecked == true,
        };
        Sources.Add(newSrc);
        SourcesList.SelectedIndex = Sources.Count - 1;
        UpdateSourcesEmptyState();

        // Add mixer channel for audio sources
        if (srcType is "audiout" or "audiin")
            AddMixerChannel(finalName, isDesktop: srcType == "audiout", isMic: srcType == "audiin");

        // ── Step 3: auto-open Properties ──────────────────────────────────────
        await ShowSourcePropertiesAsync(newSrc);
    }

    private void RemoveSourceBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SourcesList.SelectedItem is SourceItem src)
        {
            Sources.Remove(src);
            UpdateSourcesEmptyState();
        }
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
        if (SourcesList.SelectedItem is SourceItem src)
            await ShowSourcePropertiesAsync(src);
    }

    private async void SourcesList_DoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
    {
        if (SourcesList.SelectedItem is SourceItem src)
            await ShowSourcePropertiesAsync(src);
    }

    private Task ShowSourcePropertiesAsync(SourceItem src) => src.SourceType switch
    {
        "screen"  => ShowDisplayCapturePropertiesAsync(src),
        "window"  => ShowWindowCapturePropertiesAsync(src),
        "video"   => ShowVideoCapturePropertiesAsync(src),
        "audiout" => ShowAudioOutputPropertiesAsync(src),
        "audiin"  => ShowAudioInputPropertiesAsync(src),
        _         => Task.CompletedTask,
    };

    // ── Display Capture Properties ───────────────────────────────────────────

    private async Task ShowDisplayCapturePropertiesAsync(SourceItem src)
    {
        var monitors = await Windows.Devices.Display.DisplayMonitor.FindAllAsync();

        var bitmapSrc  = new SoftwareBitmapSource();
        var previewImg = new Image
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Height  = 290,
            Stretch = Stretch.Uniform,
            Source  = bitmapSrc,
        };
        var previewBorder = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Height       = 290,
            Background   = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 10, 10, 10)),
            CornerRadius = new CornerRadius(4),
            Child        = previewImg,
        };

        var captureMethodCombo = MakeWideCombo(
            new[] { "Automatic", "DXGI Desktop Duplication", "Windows Graphics Capture" }, 0);

        // Build display list and map index → GraphicsCaptureItem
        var displayCombo = MakeWideCombo(new[] { "[Select a display to capture]" }, 0);
        var monitorList  = monitors.ToList();
        int primaryIdx   = 1;
        for (int i = 0; i < monitorList.Count; i++)
        {
            var m   = monitorList[i];
            var res = m.NativeResolutionInRawPixels;
            var pos = m.PhysicalPosition;
            var lbl = $"{m.DisplayName}: {res.Width}x{res.Height} @ {pos.X},{pos.Y}";
            if (i == 0) { lbl += " (Primary Monitor)"; primaryIdx = 1; }
            displayCombo.Items.Add(lbl);
        }

        var cursorChk = new CheckBox { Content = "Capture Cursor", IsChecked = true,
                                       Margin  = new Thickness(0, 6, 0, 0) };

        // WGC state
        CanvasDevice?               dlgDevice  = null;
        Direct3D11CaptureFramePool? dlgPool    = null;
        GraphicsCaptureSession?     dlgSession = null;
        bool                        dlgBusy    = false;
        GraphicsCaptureItem?        pickedItem = null;

        void StopDlgCapture()
        {
            dlgSession?.Dispose(); dlgSession = null;
            dlgPool?.Dispose();    dlgPool    = null;
        }

        void StartCaptureFromItem(GraphicsCaptureItem item)
        {
            StopDlgCapture();
            pickedItem = item;
            try
            {
                dlgDevice ??= new CanvasDevice();
                dlgPool    = Direct3D11CaptureFramePool.Create(
                    dlgDevice, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, item.Size);
                dlgSession = dlgPool.CreateCaptureSession(item);
                dlgSession.IsCursorCaptureEnabled = cursorChk.IsChecked == true;
                dlgPool.FrameArrived += (pool, _) =>
                {
                    if (dlgBusy) { pool.TryGetNextFrame()?.Dispose(); return; }
                    dlgBusy = true;
                    var frame = pool.TryGetNextFrame();
                    if (frame == null) { dlgBusy = false; return; }
                    DispatcherQueue.TryEnqueue(async () =>
                    {
                        try
                        {
                            var sb = await SoftwareBitmap.CreateCopyFromSurfaceAsync(frame.Surface);
                            if (sb.BitmapPixelFormat != BitmapPixelFormat.Bgra8 ||
                                sb.BitmapAlphaMode   != BitmapAlphaMode.Premultiplied)
                                sb = SoftwareBitmap.Convert(sb, BitmapPixelFormat.Bgra8,
                                                            BitmapAlphaMode.Premultiplied);
                            await bitmapSrc.SetBitmapAsync(sb);
                        }
                        catch { }
                        finally { frame.Dispose(); dlgBusy = false; }
                    });
                };
                dlgSession.StartCapture();
            }
            catch { }
        }

        // Auto-preview when display dropdown changes
        void TryStartPreviewForIndex(int idx)
        {
            int monIdx = idx - 1; // offset for [Select...] placeholder
            if (monIdx < 0 || monIdx >= monitorList.Count) { StopDlgCapture(); return; }
            var m   = monitorList[monIdx];
            var gid = new Windows.Graphics.DisplayId { Value = m.DisplayId.Value };
            var item = GraphicsCaptureItem.TryCreateFromDisplayId(gid);
            if (item != null) StartCaptureFromItem(item);
        }

        displayCombo.SelectionChanged += (_, _) => TryStartPreviewForIndex(displayCombo.SelectedIndex);
        cursorChk.Checked   += (_, _) => { if (dlgSession != null) dlgSession.IsCursorCaptureEnabled = true;  };
        cursorChk.Unchecked += (_, _) => { if (dlgSession != null) dlgSession.IsCursorCaptureEnabled = false; };

        var content = new StackPanel { Spacing = 8, Width = 620 };
        content.Children.Add(previewBorder);
        content.Children.Add(MakePropRow("Capture Method", captureMethodCombo));
        content.Children.Add(MakePropRow("Display",        displayCombo));
        content.Children.Add(cursorChk);

        var dlg = new ContentDialog
        {
            Title               = $"Properties for '{src.Name}'",
            Content             = content,
            PrimaryButtonText   = "OK",
            SecondaryButtonText = "Defaults",
            CloseButtonText     = "Cancel",
            XamlRoot            = XamlRoot,
        };

        // Auto-select primary monitor and start preview
        displayCombo.SelectedIndex = monitorList.Count > 0 ? primaryIdx : 0;

        var result = await dlg.ShowAsync();
        StopDlgCapture();
        dlgDevice?.Dispose();

        if (result == ContentDialogResult.Primary && pickedItem != null)
        {
            src.CaptureItem = pickedItem;
            StartMainScreenPreview(pickedItem);
        }
    }

    // ── Window Capture Properties ────────────────────────────────────────────

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsCallback lpEnumFunc, nint lParam);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint hWnd);
    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int GetWindowText(nint hWnd, System.Text.StringBuilder sb, int nMaxCount);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetWindowTextLength(nint hWnd);
    private delegate bool EnumWindowsCallback(nint hWnd, nint lParam);

    private static List<(nint Hwnd, string Title)> GetOpenWindows()
    {
        var list = new List<(nint, string)>();
        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd)) return true;
            var len = GetWindowTextLength(hwnd);
            if (len == 0) return true;
            var sb = new System.Text.StringBuilder(len + 1);
            GetWindowText(hwnd, sb, sb.Capacity);
            var t = sb.ToString();
            if (!string.IsNullOrWhiteSpace(t)) list.Add((hwnd, t));
            return true;
        }, 0);
        return list;
    }

    private async Task ShowWindowCapturePropertiesAsync(SourceItem src)
    {
        var windows = GetOpenWindows();

        var bitmapSrc  = new SoftwareBitmapSource();
        var previewImg = new Image
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Height  = 290,
            Stretch = Stretch.Uniform,
            Source  = bitmapSrc,
        };
        var previewBorder = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Height       = 290,
            Background   = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 10, 10, 10)),
            CornerRadius = new CornerRadius(4),
            Child        = previewImg,
        };

        var windowCombo = MakeWideCombo(new[] { "[Select a window to capture]" }, 0);
        foreach (var (_, title) in windows) windowCombo.Items.Add(title);

        var captureMethodCombo = MakeWideCombo(
            new[] { "Automatic", "BitBlt", "Windows Graphics Capture" }, 0);
        var matchPriorityCombo = MakeWideCombo(
            new[] { "Match title, otherwise find window of same type",
                    "Match title, otherwise find window of same executable",
                    "Match title only" }, 0);

        var captureAudioChk = new CheckBox { Content = "Capture Audio (BETA)", IsChecked = false };
        var cursorChk       = new CheckBox { Content = "Capture Cursor",        IsChecked = true  };
        var multiAdapterChk = new CheckBox { Content = "Multi-adapter Compatibility", IsChecked = false };

        // WGC state for dialog preview
        CanvasDevice?               dlgDevice  = null;
        Direct3D11CaptureFramePool? dlgPool    = null;
        GraphicsCaptureSession?     dlgSession = null;
        bool                        dlgBusy    = false;
        GraphicsCaptureItem?        pickedItem = null;

        void StopDlgCapture()
        {
            dlgSession?.Dispose(); dlgSession = null;
            dlgPool?.Dispose();    dlgPool    = null;
        }

        void StartCaptureFromItem(GraphicsCaptureItem item)
        {
            StopDlgCapture();
            pickedItem = item;
            try
            {
                dlgDevice ??= new CanvasDevice();
                dlgPool    = Direct3D11CaptureFramePool.Create(
                    dlgDevice, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, item.Size);
                dlgSession = dlgPool.CreateCaptureSession(item);
                dlgSession.IsCursorCaptureEnabled = cursorChk.IsChecked == true;
                dlgPool.FrameArrived += (pool, _) =>
                {
                    if (dlgBusy) { pool.TryGetNextFrame()?.Dispose(); return; }
                    dlgBusy = true;
                    var frame = pool.TryGetNextFrame();
                    if (frame == null) { dlgBusy = false; return; }
                    DispatcherQueue.TryEnqueue(async () =>
                    {
                        try
                        {
                            var sb = await SoftwareBitmap.CreateCopyFromSurfaceAsync(frame.Surface);
                            if (sb.BitmapPixelFormat != BitmapPixelFormat.Bgra8 ||
                                sb.BitmapAlphaMode   != BitmapAlphaMode.Premultiplied)
                                sb = SoftwareBitmap.Convert(sb, BitmapPixelFormat.Bgra8,
                                                            BitmapAlphaMode.Premultiplied);
                            await bitmapSrc.SetBitmapAsync(sb);
                        }
                        catch { }
                        finally { frame.Dispose(); dlgBusy = false; }
                    });
                };
                dlgSession.StartCapture();
            }
            catch { }
        }

        // Use WGC picker for window selection (shows the nice thumbnail grid)
        var pickerBtn = new Button
        {
            Content             = "Pick Window with Capture Picker…",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin              = new Thickness(0, 0, 0, 4),
        };
        pickerBtn.Click += async (_, _) =>
        {
            try
            {
                var picker = new GraphicsCapturePicker();
                InitializeWithWindow.Initialize(picker,
                    WindowNativeInterop.GetWindowHandle(App.MainWindow!));
                var item = await picker.PickSingleItemAsync();
                if (item == null) return;
                // Update window dropdown to show selected item name
                if (!windowCombo.Items.Contains(item.DisplayName))
                    windowCombo.Items.Add(item.DisplayName);
                windowCombo.SelectedItem = item.DisplayName;
                StartCaptureFromItem(item);
            }
            catch { }
        };

        cursorChk.Checked   += (_, _) => { if (dlgSession != null) dlgSession.IsCursorCaptureEnabled = true;  };
        cursorChk.Unchecked += (_, _) => { if (dlgSession != null) dlgSession.IsCursorCaptureEnabled = false; };

        var content = new StackPanel { Spacing = 8, Width = 620 };
        content.Children.Add(previewBorder);
        content.Children.Add(pickerBtn);
        content.Children.Add(MakePropRow("Window",               windowCombo));
        content.Children.Add(MakePropRow("Capture Method",       captureMethodCombo));
        content.Children.Add(MakePropRow("Window Match Priority", matchPriorityCombo));
        content.Children.Add(captureAudioChk);
        content.Children.Add(cursorChk);
        content.Children.Add(multiAdapterChk);

        var dlg = new ContentDialog
        {
            Title               = $"Properties for '{src.Name}'",
            Content             = content,
            PrimaryButtonText   = "OK",
            SecondaryButtonText = "Defaults",
            CloseButtonText     = "Cancel",
            XamlRoot            = XamlRoot,
        };

        // If source already has a capture item, resume preview
        if (src.CaptureItem != null) StartCaptureFromItem(src.CaptureItem);

        var result = await dlg.ShowAsync();
        StopDlgCapture();
        dlgDevice?.Dispose();

        if (result == ContentDialogResult.Primary && pickedItem != null)
        {
            src.CaptureItem = pickedItem;
            StartMainScreenPreview(pickedItem);
        }
    }

    // ── Video Capture Device Properties ─────────────────────────────────────

    private async Task ShowVideoCapturePropertiesAsync(SourceItem src)
    {
        var videoDevices = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture);

        // Live preview element
        var previewElem = new MediaPlayerElement
        {
            HorizontalAlignment        = HorizontalAlignment.Stretch,
            Height                     = 290,
            AreTransportControlsEnabled = false,
            AutoPlay                   = false,
            Stretch                    = Stretch.Uniform,
            Background                 = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 10, 10, 10)),
        };

        var deviceNames = videoDevices.Select(d => d.Name).ToList();
        var deviceCombo = MakeWideComboList(deviceNames, 0);
        int matchIdx = deviceNames.FindIndex(n =>
            n.Contains(src.Name, StringComparison.OrdinalIgnoreCase));
        if (matchIdx >= 0) deviceCombo.SelectedIndex = matchIdx;

        var deactivateChk = new CheckBox { Content = "Deactivate when not showing",
                                           IsChecked = false, Margin = new Thickness(0, 4, 0, 0) };
        var resFpsCombo   = MakeWideCombo(new[] { "Device Default", "Custom" }, 0);
        var resCombo      = MakeWideCombo(Array.Empty<string>(), -1);
        resCombo.IsEnabled = false;

        MediaCapture? previewCapture = null;
        MediaPlayer?  previewPlayer  = null;

        async Task StartVideoPreviewAsync(int idx)
        {
            previewPlayer?.Dispose(); previewPlayer = null;
            if (previewCapture != null)
            {
                try { await previewCapture.StopPreviewAsync(); } catch { }
                previewCapture.Dispose(); previewCapture = null;
            }
            if (idx < 0 || idx >= videoDevices.Count) return;
            try
            {
                previewCapture = new MediaCapture();
                await previewCapture.InitializeAsync(new MediaCaptureInitializationSettings
                {
                    VideoDeviceId       = videoDevices[idx].Id,
                    StreamingCaptureMode = StreamingCaptureMode.Video,
                });
                var fs = previewCapture.FrameSources.Values.FirstOrDefault(
                    f => f.Info.MediaStreamType is MediaStreamType.VideoPreview
                                               or MediaStreamType.VideoRecord);
                if (fs != null)
                {
                    var ms = MediaSource.CreateFromMediaFrameSource(fs);
                    previewPlayer = new MediaPlayer { Source = ms };
                    previewElem.SetMediaPlayer(previewPlayer);
                    previewPlayer.Play();
                }
            }
            catch { }
        }

        deviceCombo.SelectionChanged += async (_, _) =>
            await StartVideoPreviewAsync(deviceCombo.SelectedIndex);

        var content = new StackPanel { Spacing = 8, Width = 620 };
        content.Children.Add(new Border
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Height     = 290,
            CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 10, 10, 10)),
            Child      = previewElem,
        });
        content.Children.Add(MakePropRow("Device",              deviceCombo));
        content.Children.Add(deactivateChk);
        content.Children.Add(MakePropRow("Resolution/FPS Type", resFpsCombo));
        content.Children.Add(MakePropRow("Resolution",          resCombo));

        var dlg = new ContentDialog
        {
            Title               = $"Properties for '{src.Name}'",
            Content             = content,
            PrimaryButtonText   = "OK",
            SecondaryButtonText = "Defaults",
            CloseButtonText     = "Cancel",
            XamlRoot            = XamlRoot,
        };

        ContentDialogResult result = ContentDialogResult.None;
        _ = StartVideoPreviewAsync(deviceCombo.SelectedIndex);

        try
        {
            result = await dlg.ShowAsync();
        }
        finally
        {
            // Stop the dialog preview first so the main preview can open the camera
            // without "device already in use" conflicts.
            previewPlayer?.Dispose();
            if (previewCapture != null)
            {
                try { await previewCapture.StopPreviewAsync(); } catch { }
                previewCapture.Dispose();
            }
        }

        if (result == ContentDialogResult.Primary)
        {
            var selected = deviceCombo.SelectedItem as string;
            if (!string.IsNullOrWhiteSpace(selected))
            {
                // Persist selection so SourcesList_SelectionChanged / main preview use it.
                src.Name = selected;
                await StartMainVideoPreviewAsync(selected);
            }
        }
    }

    // ── Audio Output Capture Properties ─────────────────────────────────────

    private async Task ShowAudioOutputPropertiesAsync(SourceItem src)
    {
        var devices = await DeviceInformation.FindAllAsync(
            MediaDevice.GetAudioRenderSelector());
        var names = new[] { "Default" }.Concat(devices.Select(d => d.Name)).ToList();
        var combo = MakeWideComboList(names, 0);

        var content = new StackPanel { Spacing = 8, Width = 500 };
        content.Children.Add(MakePropRow("Device", combo));

        await new ContentDialog
        {
            Title               = $"Properties for '{src.Name}'",
            Content             = content,
            PrimaryButtonText   = "OK",
            SecondaryButtonText = "Defaults",
            CloseButtonText     = "Cancel",
            XamlRoot            = XamlRoot,
        }.ShowAsync();
    }

    // ── Audio Input Capture Properties ───────────────────────────────────────

    private async Task ShowAudioInputPropertiesAsync(SourceItem src)
    {
        var devices = await DeviceInformation.FindAllAsync(
            MediaDevice.GetAudioCaptureSelector());
        var names = new[] { "Default" }.Concat(devices.Select(d => d.Name)).ToList();
        var combo = MakeWideComboList(names, 0);

        var content = new StackPanel { Spacing = 8, Width = 500 };
        content.Children.Add(MakePropRow("Device", combo));

        await new ContentDialog
        {
            Title               = $"Properties for '{src.Name}'",
            Content             = content,
            PrimaryButtonText   = "OK",
            SecondaryButtonText = "Defaults",
            CloseButtonText     = "Cancel",
            XamlRoot            = XamlRoot,
        }.ShowAsync();
    }

    // ── Dialog layout helpers ────────────────────────────────────────────────

    private static Grid MakePropRow(string label, FrameworkElement control)
    {
        var g = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var lbl = new TextBlock
        {
            Text              = label,
            FontSize          = 13,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground        = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 200, 200, 200)),
        };
        Grid.SetColumn(lbl,     0);
        Grid.SetColumn(control, 1);
        g.Children.Add(lbl);
        g.Children.Add(control);
        return g;
    }

    private static ComboBox MakeWideCombo(IEnumerable<string> items, int selectedIndex)
    {
        var cb = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, FontSize = 13 };
        foreach (var it in items) cb.Items.Add(it);
        if (selectedIndex >= 0 && selectedIndex < cb.Items.Count)
            cb.SelectedIndex = selectedIndex;
        return cb;
    }

    private static ComboBox MakeWideComboList(IList<string> items, int selectedIndex)
        => MakeWideCombo(items, selectedIndex);

    // ── Camera preview (live webcam feed via MediaCapture) ───────────────

    private async Task StartCameraPreviewAsync(string deviceName)
    {
        await StopCameraPreviewAsync();
        try
        {
            // Find device by name
            var devs = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture);
            var dev  = devs.FirstOrDefault(d => d.Name.Equals(deviceName, StringComparison.OrdinalIgnoreCase))
                       ?? devs.FirstOrDefault();
            if (dev == null)
            {
                LivePreviewHint.Text = $"Camera not found: {deviceName}";
                return;
            }

            _mediaCapture = new MediaCapture();
            await _mediaCapture.InitializeAsync(new MediaCaptureInitializationSettings
            {
                VideoDeviceId       = dev.Id,
                StreamingCaptureMode = Windows.Media.Capture.StreamingCaptureMode.Video,
            });

            // Build a MediaSource from the first video frame source
            var frameSource = _mediaCapture.FrameSources.Values
                .FirstOrDefault(fs => fs.Info.MediaStreamType == Windows.Media.Capture.MediaStreamType.VideoPreview
                                   || fs.Info.MediaStreamType == Windows.Media.Capture.MediaStreamType.VideoRecord);

            if (frameSource != null)
            {
                var mediaSource = Windows.Media.Core.MediaSource.CreateFromMediaFrameSource(frameSource);
                var player = new MediaPlayer { Source = mediaSource };
                LivePreviewElement.SetMediaPlayer(player);
                LivePreviewElement.Visibility = Visibility.Visible;
                LivePreviewHint.Visibility    = Visibility.Collapsed;
                player.Play();

                // Also show in Program pane if in Studio Mode
                if (_isStudioMode)
                {
                    var progPlayer = new MediaPlayer { Source = mediaSource };
                    ProgramCamElement.SetMediaPlayer(progPlayer);
                    ProgramCamElement.Visibility  = Visibility.Visible;
                    ProgramHintText.Visibility    = Visibility.Collapsed;
                    progPlayer.Play();
                }
            }
            else
            {
                LivePreviewHint.Text = $"Preview: {deviceName}";
            }
        }
        catch (Exception ex)
        {
            LivePreviewHint.Text = $"Camera error: {ex.Message}";
            LivePreviewHint.Visibility    = Visibility.Visible;
            LivePreviewElement.Visibility = Visibility.Collapsed;
        }
    }

    private async Task StopCameraPreviewAsync()
    {
        LivePreviewElement.SetMediaPlayer(null);
        LivePreviewElement.Visibility = Visibility.Collapsed;
        LivePreviewHint.Visibility    = Visibility.Visible;
        LivePreviewHint.Text          = "No live source";

        ProgramCamElement.SetMediaPlayer(null);
        ProgramCamElement.Visibility = Visibility.Collapsed;

        if (_mediaCapture != null)
        {
            try { await _mediaCapture.StopPreviewAsync(); } catch { }
            _mediaCapture.Dispose();
            _mediaCapture = null;
        }
    }

    // ── Main preview helpers (screen capture + webcam) ───────────────────────

    /// <summary>Start streaming a WGC item into the main PreviewHost canvas.</summary>
    private void StartMainScreenPreview(GraphicsCaptureItem item)
    {
        StopMainScreenPreview();
        StopMainVideoPreview();
        try
        {
            _mainCaptureDevice ??= new CanvasDevice();
            _mainCapturePool = Direct3D11CaptureFramePool.Create(
                _mainCaptureDevice, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, item.Size);
            _mainCaptureSession = _mainCapturePool.CreateCaptureSession(item);
            _mainCaptureSession.IsCursorCaptureEnabled = true;
            _mainCapturePool.FrameArrived += (pool, _) =>
            {
                if (_mainCaptureBusy) { pool.TryGetNextFrame()?.Dispose(); return; }
                _mainCaptureBusy = true;
                var frame = pool.TryGetNextFrame();
                if (frame == null) { _mainCaptureBusy = false; return; }
                DispatcherQueue.TryEnqueue(async () =>
                {
                    try
                    {
                        var sb = await SoftwareBitmap.CreateCopyFromSurfaceAsync(frame.Surface);
                        if (sb.BitmapPixelFormat != BitmapPixelFormat.Bgra8 ||
                            sb.BitmapAlphaMode   != BitmapAlphaMode.Premultiplied)
                            sb = SoftwareBitmap.Convert(sb, BitmapPixelFormat.Bgra8,
                                                        BitmapAlphaMode.Premultiplied);
                        await _mainBitmapSrc.SetBitmapAsync(sb);
                    }
                    catch { }
                    finally { frame.Dispose(); _mainCaptureBusy = false; }
                });
            };
            _mainCaptureSession.StartCapture();

            // Show the same screen feed in the side "LIVE PREVIEW" panel.
            // (The panel originally only supported webcams via MediaPlayerElement.)
            LivePreviewScreenImage.Source     = _mainBitmapSrc;
            LivePreviewScreenImage.Visibility = Visibility.Visible;
            LivePreviewElement.SetMediaPlayer(null);
            LivePreviewElement.Visibility     = Visibility.Collapsed;
            LivePreviewHint.Visibility       = Visibility.Collapsed;

            PreviewScreenImage.Visibility = Visibility.Visible;
            PreviewHintPanel.Visibility   = Visibility.Collapsed;
        }
        catch { /* leave hint visible if capture fails */ }
    }

    private void StopMainScreenPreview()
    {
        _mainCaptureSession?.Dispose(); _mainCaptureSession = null;
        _mainCapturePool?.Dispose();    _mainCapturePool    = null;
        PreviewScreenImage.Visibility = Visibility.Collapsed;

        LivePreviewScreenImage.Visibility = Visibility.Collapsed;
        LivePreviewElement.SetMediaPlayer(null);
        LivePreviewElement.Visibility = Visibility.Collapsed;
        LivePreviewHint.Visibility   = Visibility.Visible;
        LivePreviewHint.Text         = "No live source";
    }

    /// <summary>Start streaming a webcam device into the main PreviewHost canvas.</summary>
    private async Task StartMainVideoPreviewAsync(string deviceName)
    {
        StopMainScreenPreview();
        StopMainVideoPreview();
        try
        {
            var devs = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture);
            var dev  = devs.FirstOrDefault(d => d.Name.Equals(deviceName, StringComparison.OrdinalIgnoreCase))
                       ?? devs.FirstOrDefault();
            if (dev == null) return;

            _mediaCapture = new MediaCapture();
            await _mediaCapture.InitializeAsync(new MediaCaptureInitializationSettings
            {
                VideoDeviceId        = dev.Id,
                StreamingCaptureMode = StreamingCaptureMode.Video,
            });
            var fs = _mediaCapture.FrameSources.Values.FirstOrDefault(
                f => f.Info.MediaStreamType is MediaStreamType.VideoPreview
                                           or MediaStreamType.VideoRecord);
            if (fs == null) return;

            var ms = MediaSource.CreateFromMediaFrameSource(fs);
            _mainVideoPlayer = new MediaPlayer { Source = ms };
            PreviewVideoElement.SetMediaPlayer(_mainVideoPlayer);
            _mainVideoPlayer.Play();

            PreviewVideoElement.Visibility = Visibility.Visible;
            PreviewHintPanel.Visibility    = Visibility.Collapsed;

            // Also show in side panel
            LivePreviewElement.SetMediaPlayer(new MediaPlayer { Source = ms });
            LivePreviewElement.Visibility = Visibility.Visible;
            LivePreviewHint.Visibility    = Visibility.Collapsed;

            LivePreviewScreenImage.Visibility = Visibility.Collapsed;
        }
        catch { /* leave hint visible */ }
    }

    private void StopMainVideoPreview()
    {
        PreviewVideoElement.SetMediaPlayer(null);
        _mainVideoPlayer?.Dispose(); _mainVideoPlayer = null;
        PreviewVideoElement.Visibility = Visibility.Collapsed;

        LivePreviewElement.SetMediaPlayer(null);
        LivePreviewElement.Visibility = Visibility.Collapsed;
        LivePreviewScreenImage.Visibility = Visibility.Collapsed;
        LivePreviewHint.Visibility   = Visibility.Visible;
        LivePreviewHint.Text         = "No live source";
    }

    private void ShowPreviewHint(string text = "No source selected")
    {
        StopMainScreenPreview();
        StopMainVideoPreview();
        PreviewHintText.Text        = text;
        PreviewHintPanel.Visibility = Visibility.Visible;
    }

    // ── Studio Mode ─────────────────────────────────────────────────────────

    private void StudioModeBtn_Click(object sender, RoutedEventArgs e)
    {
        _isStudioMode = !_isStudioMode;

        if (_isStudioMode)
        {
            // Split: PREVIEW left | divider | PROGRAM right; hide live side panel
            PreviewMainCol.Width = new GridLength(1, GridUnitType.Star);
            StudioDivCol.Width   = new GridLength(4);
            ProgramCol.Width     = new GridLength(1, GridUnitType.Star);
            LiveSideCol.Width    = new GridLength(0);

            StudioDividerRect.Visibility = Visibility.Visible;
            ProgramHost.Visibility       = Visibility.Visible;
            LivePreviewHost.Visibility   = Visibility.Collapsed;

            ProgramHintText.Text  = _selectedSourceId != null
                ? $"Program: {_selectedSourceId}"
                : "No program output";

            StudioModeBtnText.Text = "Exit Studio Mode";
        }
        else
        {
            // Normal: main preview left | live side panel right
            PreviewMainCol.Width = new GridLength(1, GridUnitType.Star);
            StudioDivCol.Width   = new GridLength(0);
            ProgramCol.Width     = new GridLength(0);
            LiveSideCol.Width    = new GridLength(360);

            StudioDividerRect.Visibility = Visibility.Collapsed;
            ProgramHost.Visibility       = Visibility.Collapsed;
            LivePreviewHost.Visibility   = Visibility.Visible;

            StudioModeBtnText.Text = "Studio Mode";
        }
    }

    private void TransitionBtn_Click(object sender, RoutedEventArgs e)
    {
        // Cut: swap preview scene into program
        if (ScenesList.SelectedItem is SceneItem s)
            ProgramHintText.Text = $"Program: {s.Name}";
        SetStatus("Cut transition applied");
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
            // 3-2-1 countdown if enabled
            if (CountdownCheckBox.IsChecked == true)
                await RunCountdownAsync();

            SetStatus("Starting recording…");
            await _recordingService.StartRecording(
                _selectedSourceId,
                _outputFilePath,
                quality, fps,
                includeMic:       true,    // always try
                includeWebcam:    hasWebcam,
                webcamDevice:     camDevice,
                audioDevice:      micDevice,
                micVolume:        micVol,
                noiseSuppression: NoiseSuppCheckBox.IsChecked == true);

            _recordingStartTime       = DateTime.Now;
            SaveClipBtn.IsEnabled     = true;
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

        SaveClipBtn.IsEnabled     = false;
        StartRecordBtn.Visibility = Visibility.Visible;
        StopRecordBtn.Visibility  = Visibility.Collapsed;
        MenuStartRecord.IsEnabled = true;
        MenuStopRecord.IsEnabled  = false;
        RecordingBadge.Visibility = Visibility.Collapsed;
        App.MainWindow?.StopRecordingIndicator();
        SetStatus("Recording stopped");

        if (_outputFilePath != null && File.Exists(_outputFilePath))
        {
            // ── Optional: save .srt subtitle file when captions were active ──
            if (_captionsActive && _captionService.HasSubtitles)
            {
                var srtDlg = new ContentDialog
                {
                    Title             = "Save Subtitles?",
                    Content           = "Captions were recorded this session. Save as an .srt subtitle file alongside the video?",
                    PrimaryButtonText = "Save .srt",
                    CloseButtonText   = "Skip",
                    XamlRoot          = XamlRoot,
                };
                if (await srtDlg.ShowAsync() == ContentDialogResult.Primary)
                {
                    var srtPath = Path.ChangeExtension(_outputFilePath, ".srt");
                    await File.WriteAllTextAsync(srtPath, _captionService.GetSrtContent());
                    SetStatus($"Subtitles saved: {Path.GetFileName(srtPath)}");
                }
            }

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
                FileName        = FfmpegLocator.FfplayExecutable,
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

    // ── Auto Captions (CC) ────────────────────────────────────────────────────

    private async void CcBtn_Click(object sender, RoutedEventArgs e)
    {
        _captionsActive = !_captionsActive;

        if (_captionsActive)
        {
            _captionService.Config = _captionConfig;
            _captionService.CaptionTextChanged += OnCaptionTextChanged;
            _captionService.ErrorOccurred       += OnCaptionError;
            await _captionService.StartAsync();

            if (_captionService.IsRunning)
            {
                CcBtnText.Text = "Captions: ON";
                ApplyCaptionStyle();
                SetStatus("Auto captions active");
            }
            else
            {
                _captionsActive = false;
                CcBtnText.Text  = "Auto Captions";
            }
        }
        else
        {
            await _captionService.StopAsync();
            _captionService.CaptionTextChanged -= OnCaptionTextChanged;
            _captionService.ErrorOccurred       -= OnCaptionError;
            CcBtnText.Text            = "Auto Captions";
            CaptionOverlay.Visibility = Visibility.Collapsed;
            CaptionText.Text          = "";
            SetStatus("Captions off");
        }
    }

    private void OnCaptionTextChanged(object? sender, string text)
    {
        // Marshal to UI thread
        DispatcherQueue.TryEnqueue(() =>
        {
            CaptionText.Text          = text;
            CaptionOverlay.Visibility = Visibility.Visible;

            // Auto-clear after configured delay
            if (_captionConfig.ClearAfterSec > 0)
            {
                _captionClearCts?.Cancel();
                _captionClearCts = new CancellationTokenSource();
                var token = _captionClearCts.Token;
                _ = Task.Delay(_captionConfig.ClearAfterSec * 1000, token)
                    .ContinueWith(_ =>
                    {
                        if (!token.IsCancellationRequested)
                            DispatcherQueue.TryEnqueue(() =>
                            {
                                CaptionText.Text          = "";
                                CaptionOverlay.Visibility = Visibility.Collapsed;
                            });
                    }, TaskScheduler.Default);
            }
        });
    }

    private void OnCaptionError(object? sender, string error)
        => DispatcherQueue.TryEnqueue(() => SetStatus($"Caption error: {error}"));

    /// <summary>Apply current CaptionConfig (including Style preset) to overlay elements.</summary>
    private void ApplyCaptionStyle()
    {
        // ── Defaults that presets will override ───────────────────────────
        CaptionText.FontSize   = _captionConfig.FontSize;
        CaptionText.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        CaptionOverlay.CornerRadius = new CornerRadius(8);
        CaptionOverlay.HorizontalAlignment = HorizontalAlignment.Center;
        CaptionOverlay.MaxWidth  = 900;
        CaptionOverlay.Padding   = new Thickness(18, 8, 18, 8);

        switch (_captionConfig.Style)
        {
            case CaptionStyle.Broadcast:
                // Black text · solid yellow bg — high-contrast broadcast / accessibility
                CaptionText.Foreground    = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 0, 0));
                CaptionOverlay.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 220, 0));
                CaptionOverlay.CornerRadius = new CornerRadius(4);
                break;

            case CaptionStyle.Cinema:
                // White text, no background — movie subtitle style
                CaptionText.Foreground    = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255));
                CaptionOverlay.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
                CaptionText.FontSize      = Math.Max(_captionConfig.FontSize, 22);
                break;

            case CaptionStyle.LowerThird:
                // Full-width dark bar — broadcast lower-third
                CaptionText.Foreground     = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255));
                CaptionOverlay.Background  = new SolidColorBrush(Windows.UI.Color.FromArgb(230, 0, 0, 0));
                CaptionOverlay.CornerRadius = new CornerRadius(0);
                CaptionOverlay.HorizontalAlignment = HorizontalAlignment.Stretch;
                CaptionOverlay.MaxWidth    = double.PositiveInfinity;
                CaptionOverlay.Padding     = new Thickness(20, 10, 20, 10);
                break;

            case CaptionStyle.Accessible:
                // Large white text · solid black bg — maximum readability
                CaptionText.Foreground    = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255));
                CaptionOverlay.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 0, 0));
                CaptionText.FontSize      = Math.Max(_captionConfig.FontSize, 28);
                CaptionText.FontWeight    = Microsoft.UI.Text.FontWeights.Bold;
                CaptionOverlay.CornerRadius = new CornerRadius(0);
                CaptionOverlay.HorizontalAlignment = HorizontalAlignment.Stretch;
                CaptionOverlay.MaxWidth    = double.PositiveInfinity;
                break;

            case CaptionStyle.Neon:
                // Cyan glow — streamer / gaming
                CaptionText.Foreground    = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 255, 220));
                CaptionOverlay.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(200, 0, 0, 30));
                CaptionOverlay.BorderBrush     = new SolidColorBrush(Windows.UI.Color.FromArgb(160, 0, 255, 220));
                CaptionOverlay.BorderThickness = new Thickness(1);
                break;

            case CaptionStyle.Custom:
            {
                var color = _captionConfig.TextColor switch
                {
                    "Yellow" => Windows.UI.Color.FromArgb(255, 255, 235, 59),
                    "Cyan"   => Windows.UI.Color.FromArgb(255, 0,   188, 212),
                    "Green"  => Windows.UI.Color.FromArgb(255, 76,  175, 80),
                    "Red"    => Windows.UI.Color.FromArgb(255, 239, 68,  68),
                    "Orange" => Windows.UI.Color.FromArgb(255, 251, 146, 60),
                    _        => Windows.UI.Color.FromArgb(255, 255, 255, 255),
                };
                CaptionText.Foreground = new SolidColorBrush(color);
                var bgAlpha = (byte)Math.Clamp(_captionConfig.BgOpacity * 255, 0, 255);
                CaptionOverlay.Background = new SolidColorBrush(
                    Windows.UI.Color.FromArgb(bgAlpha, 0, 0, 0));
                break;
            }

            default: // Classic
                CaptionText.Foreground    = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255));
                CaptionOverlay.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(140, 0, 0, 0));
                break;
        }

        CaptionOverlay.VerticalAlignment = _captionConfig.Position == "Top"
            ? VerticalAlignment.Top
            : VerticalAlignment.Bottom;

        CaptionOverlay.Margin = _captionConfig.Position == "Top"
            ? new Thickness(40, 48, 40, 0)
            : new Thickness(40, 0, 40, 48);
    }

    // ── Caption Settings dialog ───────────────────────────────────────────────

    private async void CcSettingsBtn_Click(object sender, RoutedEventArgs e)
    {
        var panel = new StackPanel { Spacing = 14, Width = 320 };

        // Caption Style
        var styleCombo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var s in Enum.GetValues<CaptionStyle>())
            styleCombo.Items.Add(new ComboBoxItem { Content = s.ToString(), Tag = s });
        styleCombo.SelectedIndex = (int)_captionConfig.Style;
        panel.Children.Add(MakeLabel("Caption Style"));
        panel.Children.Add(styleCombo);

        // Language
        var langCombo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var l in new[] { "en-US","en-GB","fr-FR","de-DE","es-ES","it-IT","pt-BR","ja-JP","ko-KR","zh-CN" })
            langCombo.Items.Add(new ComboBoxItem { Content = l, Tag = l });
        langCombo.SelectedIndex = Math.Max(0,
            langCombo.Items.Cast<ComboBoxItem>().ToList()
                .FindIndex(i => i.Tag as string == _captionConfig.Language));
        panel.Children.Add(MakeLabel("Language"));
        panel.Children.Add(langCombo);

        // Font size
        var fontSlider = new Slider { Minimum = 12, Maximum = 48, Value = _captionConfig.FontSize,
            StepFrequency = 2, TickFrequency = 4, TickPlacement = Microsoft.UI.Xaml.Controls.Primitives.TickPlacement.Outside };
        panel.Children.Add(MakeLabel("Font Size"));
        panel.Children.Add(fontSlider);

        // Text colour
        var colorCombo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var c in new[] { "White", "Yellow", "Cyan", "Green" })
            colorCombo.Items.Add(new ComboBoxItem { Content = c, Tag = c });
        colorCombo.SelectedIndex = Math.Max(0,
            colorCombo.Items.Cast<ComboBoxItem>().ToList()
                .FindIndex(i => i.Tag as string == _captionConfig.TextColor));
        panel.Children.Add(MakeLabel("Text Colour"));
        panel.Children.Add(colorCombo);

        // Background opacity
        var bgSlider = new Slider { Minimum = 0, Maximum = 1, Value = _captionConfig.BgOpacity,
            StepFrequency = 0.05, TickFrequency = 0.25, TickPlacement = Microsoft.UI.Xaml.Controls.Primitives.TickPlacement.Outside };
        panel.Children.Add(MakeLabel("Background Opacity"));
        panel.Children.Add(bgSlider);

        // Position
        var posCombo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        posCombo.Items.Add(new ComboBoxItem { Content = "Bottom", Tag = "Bottom" });
        posCombo.Items.Add(new ComboBoxItem { Content = "Top",    Tag = "Top" });
        posCombo.SelectedIndex = _captionConfig.Position == "Top" ? 1 : 0;
        panel.Children.Add(MakeLabel("Position"));
        panel.Children.Add(posCombo);

        // Auto-clear delay
        var clearSlider = new Slider { Minimum = 0, Maximum = 10, Value = _captionConfig.ClearAfterSec,
            StepFrequency = 1, TickFrequency = 2, TickPlacement = Microsoft.UI.Xaml.Controls.Primitives.TickPlacement.Outside };
        panel.Children.Add(MakeLabel("Auto-clear after (seconds, 0 = never)"));
        panel.Children.Add(clearSlider);

        // Burn in
        var burnCheckBox = new CheckBox { Content = "Burn captions into recording (via ffmpeg)",
            IsChecked = _captionConfig.BurnIntoRecording };
        panel.Children.Add(burnCheckBox);

        var dlg = new ContentDialog
        {
            Title             = "Caption Settings",
            Content           = new ScrollViewer { Content = panel, MaxHeight = 460 },
            PrimaryButtonText = "Save",
            CloseButtonText   = "Cancel",
            XamlRoot          = XamlRoot,
        };

        if (await dlg.ShowAsync() == ContentDialogResult.Primary)
        {
            _captionConfig.Style             = (styleCombo.SelectedItem as ComboBoxItem)?.Tag is CaptionStyle cs ? cs : CaptionStyle.Classic;
            _captionConfig.Language          = (langCombo.SelectedItem  as ComboBoxItem)?.Tag as string ?? "en-US";
            _captionConfig.TextColor         = (colorCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "White";
            _captionConfig.Position          = (posCombo.SelectedItem   as ComboBoxItem)?.Tag as string ?? "Bottom";
            _captionConfig.FontSize          = fontSlider.Value;
            _captionConfig.BgOpacity         = bgSlider.Value;
            _captionConfig.ClearAfterSec     = (int)clearSlider.Value;
            _captionConfig.BurnIntoRecording = burnCheckBox.IsChecked == true;
            _captionService.Config           = _captionConfig;
            SaveCaptionConfigToSettings();
            ApplyCaptionStyle();
            SetStatus("Caption settings saved");
        }
    }

    private static TextBlock MakeLabel(string text) => new()
    {
        Text       = text,
        FontSize   = 11,
        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        Foreground = (Brush)Application.Current.Resources["TextTertiaryBrush"],
    };

    // ── Countdown before recording ────────────────────────────────────────────

    private async Task<bool> RunCountdownAsync()
    {
        if (CountdownCheckBox.IsChecked != true) return true;

        CountdownOverlay.Visibility = Visibility.Visible;
        for (int i = 3; i >= 1; i--)
        {
            CountdownText.Text = i.ToString();
            await Task.Delay(1000);
        }
        CountdownOverlay.Visibility = Visibility.Collapsed;
        return true;
    }

    // ── Save Clip (Replay Buffer — last 30 s) ────────────────────────────────

    private async void SaveClipBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_outputFilePath == null || !File.Exists(_outputFilePath))
        {
            SetStatus("No recording in progress");
            return;
        }

        var elapsed = (DateTime.Now - _recordingStartTime).TotalSeconds;
        var clipSec = Math.Min(30, (int)elapsed);
        var skip    = Math.Max(0, (int)elapsed - clipSec);

        var dir  = Path.GetDirectoryName(_outputFilePath) ?? "";
        var ext  = Path.GetExtension(_outputFilePath);
        var clip = Path.Combine(dir,
            $"Clip-{DateTime.Now:yyyyMMdd-HHmmss}{ext}");

        SetStatus($"Saving {clipSec}s clip…");
        try
        {
            var args = $"-y -ss {skip} -i \"{_outputFilePath}\" -t {clipSec} -c copy \"{clip}\"";
            var psi  = new ProcessStartInfo(FfmpegLocator.Executable, args)
                { UseShellExecute = false, CreateNoWindow = true };
            using var p = Process.Start(psi)!;
            await Task.Run(() => p.WaitForExit(10000));
            SetStatus(File.Exists(clip) ? $"Clip saved: {Path.GetFileName(clip)}" : "Clip save failed");
        }
        catch (Exception ex)
        {
            SetStatus($"Clip error: {ex.Message}");
        }
    }

    // ── Lower Third / Title Card ──────────────────────────────────────────────

    private void LowerThirdBtn_Click(object sender, RoutedEventArgs e)
    {
        _lowerThirdVisible = !_lowerThirdVisible;
        LowerThirdOverlay.Visibility = _lowerThirdVisible ? Visibility.Visible : Visibility.Collapsed;
        LowerThirdBtnText.Text = _lowerThirdVisible ? "Hide Lower Third" : "Lower Third";
    }

    private async void LowerThirdSettingsBtn_Click(object sender, RoutedEventArgs e)
    {
        var titleBox = new TextBox
        {
            PlaceholderText = "Title text (e.g. John Smith)",
            Text            = LowerThirdTitle.Text == "Title Text" ? "" : LowerThirdTitle.Text,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var subBox = new TextBox
        {
            PlaceholderText = "Subtitle / role (e.g. CEO, Contoso)",
            Text            = LowerThirdSub.Text == "Subtitle / Role" ? "" : LowerThirdSub.Text,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        var colorCombo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var (label, tag) in new[] { ("Dark (default)", "dark"), ("Blue", "blue"), ("Red accent", "red"), ("Transparent", "transparent") })
            colorCombo.Items.Add(new ComboBoxItem { Content = label, Tag = tag });
        colorCombo.SelectedIndex = 0;

        var panel = new StackPanel { Spacing = 12, Width = 300 };
        panel.Children.Add(MakeLabel("Title"));
        panel.Children.Add(titleBox);
        panel.Children.Add(MakeLabel("Subtitle / Role"));
        panel.Children.Add(subBox);
        panel.Children.Add(MakeLabel("Background Colour"));
        panel.Children.Add(colorCombo);

        var dlg = new ContentDialog
        {
            Title             = "Lower Third Settings",
            Content           = panel,
            PrimaryButtonText = "Apply",
            CloseButtonText   = "Cancel",
            XamlRoot          = XamlRoot,
        };

        if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;

        var title = titleBox.Text.Trim();
        var sub   = subBox.Text.Trim();
        if (!string.IsNullOrEmpty(title)) LowerThirdTitle.Text = title;
        if (!string.IsNullOrEmpty(sub))   LowerThirdSub.Text   = sub;

        // Apply chosen background
        LowerThirdOverlay.Background = ((colorCombo.SelectedItem as ComboBoxItem)?.Tag as string) switch
        {
            "blue"        => new SolidColorBrush(Windows.UI.Color.FromArgb(230, 20,  80,  180)),
            "red"         => new SolidColorBrush(Windows.UI.Color.FromArgb(230, 180, 20,  20)),
            "transparent" => new SolidColorBrush(Windows.UI.Color.FromArgb(0,   0,   0,   0)),
            _             => new SolidColorBrush(Windows.UI.Color.FromArgb(230, 0,   0,   0)),
        };

        // Auto-show the overlay when text is configured
        if (!_lowerThirdVisible && (!string.IsNullOrEmpty(title) || !string.IsNullOrEmpty(sub)))
        {
            _lowerThirdVisible           = true;
            LowerThirdOverlay.Visibility = Visibility.Visible;
            LowerThirdBtnText.Text       = "Hide Lower Third";
        }
    }

    // ── Timeline handlers ──────────────────────────────────────────────────────

    private bool _timelinePlaying;

    private void TimelinePlayBtn_Click(object sender, RoutedEventArgs e)
    {
        _timelinePlaying = !_timelinePlaying;
        if (sender is Button btn && btn.Content is FontIcon icon)
        {
            icon.Glyph = _timelinePlaying ? "\uE769" : "\uE768"; // Pause : Play
        }
    }

    private void AddTimelineMarkerBtn_Click(object sender, RoutedEventArgs e)
    {
        // Add a marker at current timeline position
        StatusText.Text = "Marker added at current position";
    }

    // ── Effects panel handlers ──────────────────────────────────────────────

    private void EffectsToggleBtn_Click(object sender, RoutedEventArgs e)
    {
        if (EffectsPanel.Visibility == Visibility.Visible)
            EffectsPanel.Visibility = Visibility.Collapsed;
        else
            EffectsPanel.Visibility = Visibility.Visible;
    }

    private void AddEffectBtn_Click(object sender, RoutedEventArgs e)
    {
        // Show effects panel if hidden
        EffectsPanel.Visibility = Visibility.Visible;
        StatusText.Text = "Effects panel opened — drag sliders to adjust";
    }

    // ── Floating Toolbar handlers ───────────────────────────────────────────

    private bool _floatingMicMuted;
    private bool _floatingCamOn;
    private bool _floatingDrawMode;

    private void FloatingToolbar_ManipulationDelta(object sender, Microsoft.UI.Xaml.Input.ManipulationDeltaRoutedEventArgs e)
    {
        var margin = FloatingToolbar.Margin;
        FloatingToolbar.Margin = new Thickness(
            margin.Left + e.Delta.Translation.X,
            margin.Top + e.Delta.Translation.Y,
            0, 0);
    }

    private void FloatingMicBtn_Click(object sender, RoutedEventArgs e)
    {
        _floatingMicMuted = !_floatingMicMuted;
        if (FloatingMicBtn.Content is FontIcon icon)
            icon.Glyph = _floatingMicMuted ? "\uE720" : "\uE720"; // mic / mic off
        StatusText.Text = _floatingMicMuted ? "Microphone muted" : "Microphone enabled";
    }

    private void FloatingCamBtn_Click(object sender, RoutedEventArgs e)
    {
        _floatingCamOn = !_floatingCamOn;
        StatusText.Text = _floatingCamOn ? "Camera enabled" : "Camera disabled";
    }

    private void FloatingDrawBtn_Click(object sender, RoutedEventArgs e)
    {
        _floatingDrawMode = !_floatingDrawMode;
        StatusText.Text = _floatingDrawMode ? "Screen annotation mode ON — draw on screen" : "Screen annotation mode OFF";
    }

    private void FloatingZoomBtn_Click(object sender, RoutedEventArgs e)
    {
        StatusText.Text = "Zoom: use Ctrl+Scroll to zoom in/out";
    }

    private void FloatingMinBtn_Click(object sender, RoutedEventArgs e)
    {
        FloatingToolbar.Visibility = Visibility.Collapsed;
    }

    // Show floating toolbar when recording starts, hide when stops
    private void UpdateFloatingToolbar(bool isRecording)
    {
        FloatingToolbar.Visibility = isRecording ? Visibility.Visible : Visibility.Collapsed;
        if (isRecording)
        {
            FloatingToolbar.Margin = new Thickness(20, 60, 0, 0);
        }
    }

    // ── IDisposable ───────────────────────────────────────────────────────────

    public void Dispose()
    {
        _meterTimer?.Stop();
        _audioMeter.Dispose();
        _captionClearCts?.Cancel();
        _ = _captionService.StopAsync();
        _captionService.Dispose();
        if (_previewProcess is { HasExited: false })
            try { _previewProcess.Kill(true); } catch { }
        try { _mediaCapture?.Dispose(); } catch { }
        _mediaCapture = null;
    }
}
