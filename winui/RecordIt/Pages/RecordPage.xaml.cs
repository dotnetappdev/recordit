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
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Windowing;
using Windows.Foundation;
using Windows.Devices.Enumeration;
using Windows.Graphics.Capture;
using System.Runtime.InteropServices;
using Windows.Graphics.DirectX;
using Windows.Graphics.Imaging;
using Windows.Media.Capture;
using Windows.Media.Core;
using Windows.Media.Devices;
using Windows.Media.Playback;
using Windows.Storage.Pickers;
using Windows.UI;
using WinRT.Interop;
using Windows.UI.Core;
using Windows.System;

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
    // Capture method preference shown in the Properties dialog (e.g. Automatic, BitBlt, Windows 10...)
    public string CaptureMethod { get; set; } = "Automatic";
    // Transform properties for preview/recording placement
    public double TranslateX { get; set; } = 0.0;
    public double TranslateY { get; set; } = 0.0;
    public double Scale { get; set; } = 1.0;
    public double Rotation { get; set; } = 0.0; // degrees
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
    // Preview/LIVE splitter drag state
    private bool _isResizingPreviewSplitter;
    private double _splitterStartX;
    private double _previewStartWidth;
    private double _liveStartWidth;
    private const double MinPreviewWidth = 200;
    private const double MinLiveWidth = 120;

    // Bottom panel splitter drag state
    private bool _isResizingPanelSplitter;
    private string _activePanelSplitter = "";
    private double _panelSplitterStartX;
    private double _panelLeftStartW;
    private double _panelRightStartW;

    // Dock lock
    private bool _docksLocked;

    // Panel drag/drop state
    private UIElement? _draggedPanel;
    private bool _isDraggingPanel;
    private Windows.Foundation.Point _dragStartPoint;
    private Border? _dragGhost;
    // private Window? _floatingHostWindow; // Unused field
    private System.Timers.Timer? _holdTimer;
    private bool _holdTriggered;
    private readonly System.Collections.Generic.Dictionary<DependencyObject, Brush?> _originalHeaderBrush = new();
    // Tab drag source tracking
    private Microsoft.UI.Xaml.Controls.TabView? _draggedFromTabView;
    private Microsoft.UI.Xaml.Controls.TabViewItem? _draggedTabItem;

    // Streaming state
    private bool _isStreaming;
    private System.Timers.Timer? _streamTimer;
    private DateTime _streamStartTime;

    // Pause recording state
    private bool _recordingPaused;

    // Virtual Camera state
    private bool _virtualCamActive;

    // Lower Third
    private bool _lowerThirdVisible;

    // Per-scene source persistence
    private readonly Dictionary<string, List<SourceItem>> _sceneSources = new();

    // Captions
    private bool _captionsActive;
    private CancellationTokenSource? _captionClearCts;
    private CaptionConfig _captionConfig = new();

    // Performance monitoring
    private PerformanceMonitor? _performanceMonitor;
    private bool _showPerformanceStats;

    // Hotkey manager
    private HotkeyManager? _hotkeyManager;

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
    // Vertical-mode collections (separate sources/scenes)
    public ObservableCollection<SceneItem>  VerticalScenes  { get; } = new();
    public ObservableCollection<SourceItem> VerticalSources { get; } = new();

    // Floating vertical window state
    private Window? _verticalWindow;
    private ListBox? _floatingVerticalSourcesList;
    private ListBox? _floatingVerticalScenesList;
    private Image?   _floatingVerticalPreviewImage;
    private bool     _verticalUndocked = false;

    private const double VuMaxHeight = AudioChannelView.MeterH;

    public RecordPage()
    {
        InitializeComponent();
        ScenesList.ItemsSource  = Scenes;
        SourcesList.ItemsSource = Sources;

        // Bind vertical lists
        VerticalScenesList.ItemsSource  = VerticalScenes;
        VerticalSourcesList.ItemsSource = VerticalSources;

        // Initialize performance monitor
        _performanceMonitor = new PerformanceMonitor();
        _performanceMonitor.StatsUpdated += PerformanceMonitor_StatsUpdated;
        
        // Initialize hotkey manager
        _hotkeyManager = new HotkeyManager();
        _hotkeyManager.HotkeyPressed += HotkeyManager_HotkeyPressed;
        
        // Register default hotkeys
        _hotkeyManager.RegisterBinding("screenshot", new System.Windows.Input.KeyGesture(
            System.Windows.Input.Key.PrintScreen, System.Windows.Input.ModifierKeys.Control));
        _hotkeyManager.RegisterBinding("start_stop_recording", new System.Windows.Input.KeyGesture(
            System.Windows.Input.Key.R, System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Shift));
        _hotkeyManager.RegisterBinding("pause_resume", new System.Windows.Input.KeyGesture(
            System.Windows.Input.Key.P, System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Shift));

        // Seed with a default scene
        AddScene("Scene 1");
        SetStatus("Ready · select a capture source");

        // Auto-probe devices on load (async, non-blocking)
        _ = InitAsync();

        // Attach pointer handlers for header drag
        ScenesHeader.PointerPressed += PanelHeader_PointerPressed;
        ScenesHeader.PointerMoved += PanelHeader_PointerMoved;
        ScenesHeader.PointerReleased += PanelHeader_PointerReleased;

        SourcesHeader.PointerPressed += PanelHeader_PointerPressed;
        SourcesHeader.PointerMoved += PanelHeader_PointerMoved;
        SourcesHeader.PointerReleased += PanelHeader_PointerReleased;

        MixerHeader.PointerPressed += PanelHeader_PointerPressed;
        MixerHeader.PointerMoved += PanelHeader_PointerMoved;
        MixerHeader.PointerReleased += PanelHeader_PointerReleased;

        TransitionsHeader.PointerPressed += PanelHeader_PointerPressed;
        TransitionsHeader.PointerMoved += PanelHeader_PointerMoved;
        TransitionsHeader.PointerReleased += PanelHeader_PointerReleased;

        ControlsHeader.PointerPressed += PanelHeader_PointerPressed;
        ControlsHeader.PointerMoved += PanelHeader_PointerMoved;
        ControlsHeader.PointerReleased += PanelHeader_PointerReleased;

        // Subscribe to core window key state to detect modifiers during drag
        try
        {
            var cw = CoreWindow.GetForCurrentThread();
            if (cw != null)
            {
                cw.KeyDown += CoreWindow_KeyDown;
                cw.KeyUp += CoreWindow_KeyUp;
            }
        }
        catch { }
    }

    // Win32 interop for window positioning
    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(nint hWnd, out RECT lpRect);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;

    private void PositionFloatingWindow(Window win, int width, int height)
    {
        try
        {
            var mainHwnd = WindowNativeInterop.GetWindowHandle(App.MainWindow!);
            if (!GetWindowRect(mainHwnd, out var r)) return;
            int mainWidth = r.Right - r.Left;
            int mainHeight = r.Bottom - r.Top;

            // Place floating window flush-right of main window with slight inset
            int x = r.Right - width - 8; // align to right edge inside
            int y = r.Top + 40; // below menu bar

            // Activate then set size/position
            win.Activate();
            var hwnd = WindowNativeInterop.GetWindowHandle(win);
            SetWindowPos(hwnd, 0, x, y, width, Math.Min(height, mainHeight - 80), SWP_NOZORDER | SWP_NOACTIVATE);
        }
        catch { }
    }

    private nint? FindWindowByTitle(string title)
    {
        nint? found = null;
        try
        {
            EnumWindows((hWnd, lParam) =>
            {
                if (!IsWindowVisible(hWnd)) return true;
                int len = GetWindowTextLength(hWnd);
                if (len == 0) return true;
                var sb = new System.Text.StringBuilder(len + 1);
                GetWindowText(hWnd, sb, sb.Capacity);
                var wtitle = sb.ToString();
                if (!string.IsNullOrEmpty(wtitle) && wtitle.IndexOf(title, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    found = hWnd;
                    return false; // stop enumeration
                }
                return true;
            }, IntPtr.Zero);
        }
        catch { }
        return found;
    }

    private void MagnifyBtn_Click(object sender, RoutedEventArgs e)
    {
        OpenMagnifierForSelectedSource();
    }

    public void OpenMagnifierForSelectedSource()
    {
        try
        {
            if (SourcesList.SelectedItem is SourceItem si)
            {
                // Try to find a matching window by name
                var hwnd = FindWindowByTitle(si.Name);
                if (hwnd == null)
                {
                    SetStatus("Cannot find window to magnify");
                    return;
                }

                var zw = new ZoomWindow(hwnd.Value);
                // Use the current preview bitmap as the magnified image when available
                zw.ImageSource = _mainBitmapSrc;
                zw.Activate();

                // Persist magnifier open state and initial bounds
                try
                {
                    var h = WindowNativeInterop.GetWindowHandle(zw);
                    if (GetWindowRect(h, out var r))
                    {
                        _settings.Set("magnifier.open", "1");
                        _settings.Set("magnifier.x", r.Left.ToString(CultureInfo.InvariantCulture));
                        _settings.Set("magnifier.y", r.Top.ToString(CultureInfo.InvariantCulture));
                        _settings.Set("magnifier.w", (r.Right - r.Left).ToString(CultureInfo.InvariantCulture));
                        _settings.Set("magnifier.h", (r.Bottom - r.Top).ToString(CultureInfo.InvariantCulture));
                        _settings.Set("magnifier.target", si.Name);
                    }
                }
                catch { }

                zw.Closed += (_, _) =>
                {
                    try { _settings.Set("magnifier.open", "0"); } catch { }
                };
            }
        }
        catch (Exception ex)
        {
            SetStatus($"Magnifier failed: {ex.Message}");
        }
    }

    private async Task InitAsync()
    {
        // Load caption defaults from app settings so the CC toggle uses them.
        LoadCaptionConfigFromSettings();

        // Attach the persistent SoftwareBitmapSource to the screen-preview Image
        PreviewScreenImage.Source = _mainBitmapSrc;

        // Restore saved layout if available
        try
        {
            var leftW = _settings.Get("layout.leftpanels.width");
            if (double.TryParse(leftW, out var lw)) LeftPanelsColumn.Width = new GridLength(Math.Max(100, lw));
            var mixerW = _settings.Get("layout.mixer.width");
            if (double.TryParse(mixerW, out var mw)) MixerColumn.Width = new GridLength(Math.Max(150, mw));
            var transW = _settings.Get("layout.trans.width");
            if (double.TryParse(transW, out var tw)) TransColumn.Width = new GridLength(Math.Max(100, tw));
            var controlsW = _settings.Get("layout.controls.width");
            if (double.TryParse(controlsW, out var cw)) ControlsColumn.Width = new GridLength(Math.Max(120, cw));

            var previewMain = _settings.Get("layout.preview.main.width");
            if (double.TryParse(previewMain, out var pm)) PreviewMainCol.Width = new GridLength(Math.Max(200, pm));
            var liveSide = _settings.Get("layout.live.side.width");
            if (double.TryParse(liveSide, out var ls)) LiveSideCol.Width = new GridLength(Math.Max(120, ls));
        }
        catch { }

        // Restore any previously floated panels and magnifier
        try { RestoreFloatingElements(); } catch { }

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

        // Update recording status timer
        if (RecordingBadge?.Visibility == Visibility.Visible && !_recordingPaused)
        {
            var elapsed = DateTime.Now - _recordingStartTime;
            var ts = elapsed.ToString(@"hh\:mm\:ss");
            if (RecordingTimerText is not null) RecordingTimerText.Text = elapsed.ToString(@"mm\:ss");
            if (FloatingTimerText  is not null) FloatingTimerText.Text  = elapsed.ToString(@"mm\:ss");
            if (StatusRecTime       is not null) StatusRecTime.Text       = ts;
        }
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
            if (SelectedSourceText is not null) SelectedSourceText.Text = "No source selected";
            return;
        }

        if (SelectedSourceText is not null) SelectedSourceText.Text = src.Name;
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
        // Apply any per-source transform to the visible preview elements
        try { ApplySourceTransform(src); } catch { }
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

    // Vertical preview handlers
    private void VerticalSourcesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (VerticalSourcesList.SelectedItem is not SourceItem src)
        {
            VerticalPreviewImage.Visibility = Visibility.Collapsed;
            return;
        }

        // If the vertical source uses a WGC CaptureItem, show the shared bitmap source
        if (src.CaptureItem != null)
        {
            VerticalPreviewImage.Source = _mainBitmapSrc;
            VerticalPreviewImage.Visibility = Visibility.Visible;
        }
        else
        {
            // For simple cases we fall back to a hint (video preview not handled here)
            VerticalPreviewImage.Visibility = Visibility.Visible;
        }
    }

    private void AddVerticalSourceBtn_Click(object sender, RoutedEventArgs e)
    {
        // If a main source is selected, clone it into vertical sources for convenience
        if (SourcesList.SelectedItem is SourceItem mainSrc)
        {
            var clone = new SourceItem
            {
                Name = mainSrc.Name,
                Icon = mainSrc.Icon,
                SourceType = mainSrc.SourceType,
                CaptureItem = mainSrc.CaptureItem,
            };
            VerticalSources.Add(clone);
        }
        else
        {
            VerticalSources.Add(new SourceItem { Name = "Vertical Source", Icon = "\uE7F8", SourceType = "screen" });
        }
    }

    private void RemoveVerticalSourceBtn_Click(object sender, RoutedEventArgs e)
    {
        var sel = VerticalSourcesList.SelectedItem as SourceItem;
        if (sel != null) VerticalSources.Remove(sel);
    }

    private async void VerticalSourceSettingsBtn_Click(object sender, RoutedEventArgs e)
    {
        if (VerticalSourcesList.SelectedItem is SourceItem src)
            await ShowSourcePropertiesAsync(src);
    }

    private void AddVerticalSceneBtn_Click(object sender, RoutedEventArgs e)
    {
        VerticalScenes.Add(new SceneItem { Name = $"Vertical Scene {VerticalScenes.Count + 1}" });
    }

    private void RemoveVerticalSceneBtn_Click(object sender, RoutedEventArgs e)
    {
        if (VerticalScenesList.SelectedItem is SceneItem s)
            VerticalScenes.Remove(s);
    }

    // Undock/dock vertical preview into a floating window (OBS-style detachable dock)

    private void VerticalUndockBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_verticalUndocked)
        {
            _verticalWindow?.Activate();
            return;
        }

        var win = new Window();
        win.Title = "Vertical Preview";

        var root = new Grid { Background = (Brush)Application.Current.Resources["BgBaseBrush"] };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = new Border { Background = (Brush)Application.Current.Resources["BgPanelHeaderBrush"], Padding = new Thickness(8) };
        var headerRow = new Grid();
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = new TextBlock { Text = "Vertical Preview", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = (Brush)Application.Current.Resources["TextPrimaryBrush"] };
        Grid.SetColumn(title, 0);
        headerRow.Children.Add(title);
        var dockBtn = new Button { Content = "Dock", Margin = new Thickness(8,0,0,0), Style = (Style)Application.Current.Resources["AlvoniaGhostButtonStyle"] };
        dockBtn.Click += (_, _) => CloseFloatingVerticalWindow();
        Grid.SetColumn(dockBtn, 1);
        headerRow.Children.Add(dockBtn);
        header.Child = headerRow;

        Grid.SetRow(header, 0);
        root.Children.Add(header);

        var contentGrid = new Grid { Padding = new Thickness(8) };
        contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });

        _floatingVerticalPreviewImage = new Image { Stretch = Stretch.UniformToFill, Source = _mainBitmapSrc };
        var previewBorder = new Border { Background = (Brush)Application.Current.Resources["BgSurfaceBrush"], Child = _floatingVerticalPreviewImage, Height = 480, CornerRadius = new CornerRadius(6), BorderBrush = (Brush)Application.Current.Resources["BorderPanelBrush"], BorderThickness = new Thickness(1) };
        Grid.SetColumn(previewBorder, 0);
        contentGrid.Children.Add(previewBorder);

        var rightStack = new StackPanel { Spacing = 8 };
        rightStack.Children.Add(new TextBlock { Text = "Vertical Sources", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        _floatingVerticalSourcesList = new ListBox { Height = 220 };
        _floatingVerticalSourcesList.ItemsSource = VerticalSources;
        _floatingVerticalSourcesList.SelectionChanged += FloatingVerticalSources_SelectionChanged;
        rightStack.Children.Add(_floatingVerticalSourcesList);

        var btnRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var addBtn = new Button { Content = "Add", Style = (Style)Application.Current.Resources["ObsToolbarBtnStyle"] };
        addBtn.Click += (s, a) => AddVerticalSourceBtn_Click(s, a);
        var removeBtn = new Button { Content = "Remove", Style = (Style)Application.Current.Resources["ObsToolbarBtnStyle"] };
        removeBtn.Click += (s, a) =>
        {
            var sel = _floatingVerticalSourcesList.SelectedItem as SourceItem;
            if (sel != null) VerticalSources.Remove(sel);
        };
        var settingsBtn = new Button { Content = "Settings", Style = (Style)Application.Current.Resources["ObsToolbarBtnStyle"] };
        settingsBtn.Click += (s, a) =>
        {
            if (_floatingVerticalSourcesList.SelectedItem is SourceItem sitem) _ = ShowSourcePropertiesAsync(sitem);
        };
        btnRow.Children.Add(addBtn); btnRow.Children.Add(removeBtn); btnRow.Children.Add(settingsBtn);
        rightStack.Children.Add(btnRow);

        rightStack.Children.Add(new TextBlock { Text = "Scenes", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Margin = new Thickness(0,8,0,0) });
        _floatingVerticalScenesList = new ListBox { Height = 160 };
        _floatingVerticalScenesList.ItemsSource = VerticalScenes;
        rightStack.Children.Add(_floatingVerticalScenesList);

        contentGrid.Children.Add(rightStack);
        Grid.SetColumn(rightStack, 1);

        Grid.SetRow(contentGrid, 1);
        root.Children.Add(contentGrid);

        win.Content = root;

        _verticalWindow = win;
        _verticalUndocked = true;
        var vti = this.FindName("VerticalTabItem") as Microsoft.UI.Xaml.Controls.TabViewItem;
        if (vti != null) vti.Visibility = Visibility.Collapsed;
        win.Closed += (_, _) => CloseFloatingVerticalWindow();
        win.Activate();
        // Position the floating window near the right edge of the main window
        PositionFloatingWindow(win, 340, 720);
    }

    private void CloseFloatingVerticalWindow()
    {
        if (_verticalWindow != null)
        {
            try { _verticalWindow.Close(); } catch { }
            _verticalWindow = null;
        }
        _floatingVerticalSourcesList = null;
        _floatingVerticalScenesList = null;
        _floatingVerticalPreviewImage = null;
        _verticalUndocked = false;
        var vti2 = this.FindName("VerticalTabItem") as Microsoft.UI.Xaml.Controls.TabViewItem;
        if (vti2 != null) vti2.Visibility = Visibility.Visible;
    }

    private void FloatingVerticalSources_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox lb) return;
        if (lb.SelectedItem is not SourceItem src)
        {
            if (_floatingVerticalPreviewImage != null) _floatingVerticalPreviewImage.Visibility = Visibility.Collapsed;
            return;
        }
        if (src.CaptureItem != null)
        {
            if (_floatingVerticalPreviewImage != null)
            {
                _floatingVerticalPreviewImage.Source = _mainBitmapSrc;
                _floatingVerticalPreviewImage.Visibility = Visibility.Visible;
            }
        }
        else
        {
            if (_floatingVerticalPreviewImage != null)
            {
                _floatingVerticalPreviewImage.Visibility = Visibility.Visible;
            }
        }
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

    private Task ShowDisplayCapturePropertiesAsync(SourceItem src)
    {
        // Enumerate monitors synchronously via Win32
        var monitorList = EnumerateMonitors();

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
            new[] { "Automatic", "DXGI Desktop Duplication", "Windows 10 (1903 and up)" }, 0);

        // Build display list from Win32 HMONITOR enumeration
        var displayCombo = MakeWideCombo(new[] { "[Select a display to capture]" }, 0);
        int primaryIdx   = 1;
        for (int i = 0; i < monitorList.Count; i++)
        {
            displayCombo.Items.Add(monitorList[i].Label);
            if (monitorList[i].Label.Contains("Primary")) primaryIdx = i + 1;
        }

        return ShowDisplayCapturePropertiesInnerAsync(src, monitorList, primaryIdx,
            bitmapSrc, previewBorder, captureMethodCombo, displayCombo);
    }

    private async Task ShowDisplayCapturePropertiesInnerAsync(
        SourceItem src,
        List<(nint HMon, string Label)> monitorList,
        int primaryIdx,
        SoftwareBitmapSource bitmapSrc,
        Border previewBorder,
        ComboBox captureMethodCombo,
        ComboBox displayCombo)
    {
        // Transform editor button
        var editTransformBtn = new Button
        {
            Content = "Edit Transform…",
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 6, 0, 0),
        };
        editTransformBtn.Click += async (_, _) =>
        {
            await ShowTransformEditorAsync(src);
        };
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
                // Use triple buffering to reduce flickering
                dlgPool    = Direct3D11CaptureFramePool.Create(
                    dlgDevice, DirectXPixelFormat.B8G8R8A8UIntNormalized, 3, item.Size);
                dlgSession = dlgPool.CreateCaptureSession(item);
#pragma warning disable CA1416
                dlgSession.IsCursorCaptureEnabled = cursorChk.IsChecked == true;
#pragma warning restore CA1416
                dlgPool.FrameArrived += (pool, _) =>
                {
                    if (dlgBusy) { pool.TryGetNextFrame()?.Dispose(); return; }
                    dlgBusy = true;
                    var frame = pool.TryGetNextFrame();
                    if (frame == null) { dlgBusy = false; return; }
                    DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, async () =>
                    {
                        try
                        {
                            var sb = await SoftwareBitmap.CreateCopyFromSurfaceAsync(frame.Surface);
                            if (sb.BitmapPixelFormat != BitmapPixelFormat.Bgra8 ||
                                sb.BitmapAlphaMode   != BitmapAlphaMode.Premultiplied)
                            {
                                var converted = SoftwareBitmap.Convert(sb, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
                                sb.Dispose();
                                sb = converted;
                            }
                            await bitmapSrc.SetBitmapAsync(sb);
                            sb.Dispose();
                        }
                        catch { }
                        finally { frame.Dispose(); dlgBusy = false; }
                    });
                };
                dlgSession.StartCapture();
            }
            catch { }
        }

        // Auto-preview when display dropdown changes — uses HMONITOR via Win32 interop
        void TryStartPreviewForIndex(int idx)
        {
            int monIdx = idx - 1; // offset for [Select...] placeholder
            if (monIdx < 0 || monIdx >= monitorList.Count) { StopDlgCapture(); return; }
            var item = CaptureItemForMonitor(monitorList[monIdx].HMon);
            if (item != null) StartCaptureFromItem(item);
        }

        displayCombo.SelectionChanged += (_, _) => TryStartPreviewForIndex(displayCombo.SelectedIndex);
#pragma warning disable CA1416
        cursorChk.Checked   += (_, _) => { 
            if (dlgSession != null && Windows.Foundation.Metadata.ApiInformation.IsPropertyPresent("Windows.Graphics.Capture.GraphicsCaptureSession", "IsCursorCaptureEnabled")) 
                dlgSession.IsCursorCaptureEnabled = true;  
        };
        cursorChk.Unchecked += (_, _) => { 
            if (dlgSession != null && Windows.Foundation.Metadata.ApiInformation.IsPropertyPresent("Windows.Graphics.Capture.GraphicsCaptureSession", "IsCursorCaptureEnabled")) 
                dlgSession.IsCursorCaptureEnabled = false; 
        };
#pragma warning restore CA1416

        var content = new StackPanel { Spacing = 8, Width = 620 };
        content.Children.Add(previewBorder);
        content.Children.Add(MakePropRow("Capture Method", captureMethodCombo));
        content.Children.Add(MakePropRow("Display",        displayCombo));
        content.Children.Add(cursorChk);
        content.Children.Add(editTransformBtn);

        // Wrap in a ScrollViewer so the dialog doesn't require manual resizing on small screens
        var scroll = new ScrollViewer { Content = content, MaxHeight = 760 };

        var dlg = new ContentDialog
        {
            Title               = $"Properties for '{src.Name}'",
            Content             = scroll,
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
            // Persist chosen capture method
            try { src.CaptureMethod = (captureMethodCombo.SelectedItem as string) ?? "Automatic"; } catch { }
            StartMainScreenPreview(pickedItem);
            // Apply any per-source transform to the preview
            try { ApplySourceTransform(src); } catch { }
        }
    }

    // ── Window Capture Properties ────────────────────────────────────────────

    // ── Win32 / WGC interop ──────────────────────────────────────────────────

    // IGraphicsCaptureItemInterop — lets us create WGC items from HWND / HMONITOR
    [System.Runtime.InteropServices.ComImport]
    [System.Runtime.InteropServices.Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
    [System.Runtime.InteropServices.InterfaceType(System.Runtime.InteropServices.ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureItemInterop
    {
        nint CreateForWindow([System.Runtime.InteropServices.In] nint window,
                             [System.Runtime.InteropServices.In] ref Guid iid);
        nint CreateForMonitor([System.Runtime.InteropServices.In] nint monitor,
                              [System.Runtime.InteropServices.In] ref Guid iid);
    }

    [System.Runtime.InteropServices.DllImport("combase.dll", PreserveSig = false)]
    private static extern void RoGetActivationFactory(nint hstring, ref Guid iid, out nint factory);
    [System.Runtime.InteropServices.DllImport("combase.dll", PreserveSig = false)]
    private static extern void WindowsCreateString(
        [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)] string str,
        int length, out nint hstring);
    [System.Runtime.InteropServices.DllImport("combase.dll", PreserveSig = false)]
    private static extern void WindowsDeleteString(nint hstring);

    private static GraphicsCaptureItem? CaptureItemForMonitor(nint hMonitor)
    {
        nint hs = 0, fp = 0;
        try
        {
            const string cls = "Windows.Graphics.Capture.GraphicsCaptureItem";
            WindowsCreateString(cls, cls.Length, out hs);
            var iid = typeof(IGraphicsCaptureItemInterop).GUID;
            RoGetActivationFactory(hs, ref iid, out fp);
            var interop = (IGraphicsCaptureItemInterop)System.Runtime.InteropServices.Marshal.GetObjectForIUnknown(fp);
            var itemIid = typeof(GraphicsCaptureItem).GUID;
            var ptr     = interop.CreateForMonitor(hMonitor, ref itemIid);
            return WinRT.MarshalInterface<GraphicsCaptureItem>.FromAbi(ptr);
        }
        catch { return null; }
        finally
        {
            if (hs != 0) try { WindowsDeleteString(hs); } catch { }
            if (fp != 0) System.Runtime.InteropServices.Marshal.Release(fp);
        }
    }

    private static GraphicsCaptureItem? CaptureItemForWindow(nint hWnd)
    {
        nint hs = 0, fp = 0;
        try
        {
            const string cls = "Windows.Graphics.Capture.GraphicsCaptureItem";
            WindowsCreateString(cls, cls.Length, out hs);
            var iid = typeof(IGraphicsCaptureItemInterop).GUID;
            RoGetActivationFactory(hs, ref iid, out fp);
            var interop = (IGraphicsCaptureItemInterop)System.Runtime.InteropServices.Marshal.GetObjectForIUnknown(fp);
            var itemIid = typeof(GraphicsCaptureItem).GUID;
            var ptr     = interop.CreateForWindow(hWnd, ref itemIid);
            return WinRT.MarshalInterface<GraphicsCaptureItem>.FromAbi(ptr);
        }
        catch { return null; }
        finally
        {
            if (hs != 0) try { WindowsDeleteString(hs); } catch { }
            if (fp != 0) System.Runtime.InteropServices.Marshal.Release(fp);
        }
    }

    // Monitor enumeration
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(nint hdc, nint lprcClip,
        MonitorEnumProc lpfnEnum, nint dwData);
    private delegate bool MonitorEnumProc(nint hMon, nint hdcMon, ref RectL lprc, nint dw);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct RectL { public int L, T, R, B; }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Auto)]
    private struct MonitorInfoEx
    {
        public int  cbSize;
        public RectL rcMonitor, rcWork;
        public uint dwFlags;
        [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }
    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
    private static extern bool GetMonitorInfo(nint hMon, ref MonitorInfoEx lpmi);

    private static List<(nint HMon, string Label)> EnumerateMonitors()
    {
        var result = new List<(nint, string)>();
        int idx = 1;
        EnumDisplayMonitors(0, 0, (hMon, _, ref rc, _) =>
        {
            var mi = new MonitorInfoEx { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MonitorInfoEx>() };
            GetMonitorInfo(hMon, ref mi);
            bool primary = (mi.dwFlags & 1) != 0;
            int w = mi.rcMonitor.R - mi.rcMonitor.L;
            int h = mi.rcMonitor.B - mi.rcMonitor.T;
            var label = $"Display {idx}: {w}x{h} @ {mi.rcMonitor.L},{mi.rcMonitor.T}";
            if (primary) label += " (Primary Monitor)";
            result.Add((hMon, label));
            idx++;
            return true;
        }, 0);
        return result;
    }

    // Window enumeration
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
            new[] { "Automatic", "BitBlt", "Windows 10 (1903 and up)" }, 0);
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
                // Use triple buffering to reduce flickering
                dlgPool    = Direct3D11CaptureFramePool.Create(
                    dlgDevice, DirectXPixelFormat.B8G8R8A8UIntNormalized, 3, item.Size);
                dlgSession = dlgPool.CreateCaptureSession(item);
#pragma warning disable CA1416
                dlgSession.IsCursorCaptureEnabled = cursorChk.IsChecked == true;
#pragma warning restore CA1416
                dlgPool.FrameArrived += (pool, _) =>
                {
                    if (dlgBusy) { pool.TryGetNextFrame()?.Dispose(); return; }
                    dlgBusy = true;
                    var frame = pool.TryGetNextFrame();
                    if (frame == null) { dlgBusy = false; return; }
                    DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, async () =>
                    {
                        try
                        {
                            var sb = await SoftwareBitmap.CreateCopyFromSurfaceAsync(frame.Surface);
                            if (sb.BitmapPixelFormat != BitmapPixelFormat.Bgra8 ||
                                sb.BitmapAlphaMode   != BitmapAlphaMode.Premultiplied)
                            {
                                var converted = SoftwareBitmap.Convert(sb, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
                                sb.Dispose();
                                sb = converted;
                            }
                            await bitmapSrc.SetBitmapAsync(sb);
                            sb.Dispose();
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

#pragma warning disable CA1416
        cursorChk.Checked   += (_, _) => { 
            if (dlgSession != null && Windows.Foundation.Metadata.ApiInformation.IsPropertyPresent("Windows.Graphics.Capture.GraphicsCaptureSession", "IsCursorCaptureEnabled")) 
                dlgSession.IsCursorCaptureEnabled = true;  
        };
        cursorChk.Unchecked += (_, _) => { 
            if (dlgSession != null && Windows.Foundation.Metadata.ApiInformation.IsPropertyPresent("Windows.Graphics.Capture.GraphicsCaptureSession", "IsCursorCaptureEnabled")) 
                dlgSession.IsCursorCaptureEnabled = false; 
        };
#pragma warning restore CA1416

        var content = new StackPanel { Spacing = 8, Width = 620 };
        content.Children.Add(previewBorder);
        content.Children.Add(pickerBtn);
        content.Children.Add(MakePropRow("Window",               windowCombo));
        content.Children.Add(MakePropRow("Capture Method",       captureMethodCombo));
        content.Children.Add(MakePropRow("Window Match Priority", matchPriorityCombo));
        content.Children.Add(captureAudioChk);
        content.Children.Add(cursorChk);
        content.Children.Add(multiAdapterChk);

        var editTransformBtn = new Button
        {
            Content = "Edit Transform…",
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 6, 0, 0),
        };
        editTransformBtn.Click += async (_, _) => { await ShowTransformEditorAsync(src); };
        content.Children.Add(editTransformBtn);

        // Wrap in ScrollViewer to avoid large resize on small displays
        var scroll = new ScrollViewer { Content = content, MaxHeight = 760 };

        var dlg = new ContentDialog
        {
            Title               = $"Properties for '{src.Name}'",
            Content             = scroll,
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
            try { src.CaptureMethod = (captureMethodCombo.SelectedItem as string) ?? "Automatic"; } catch { }
            StartMainScreenPreview(pickedItem);
            try { ApplySourceTransform(src); } catch { }
        }
    }

    private async Task ShowTransformEditorAsync(SourceItem src)
    {
        var txBox = new TextBox { Text = src.TranslateX.ToString(CultureInfo.InvariantCulture), HorizontalAlignment = HorizontalAlignment.Stretch };
        var tyBox = new TextBox { Text = src.TranslateY.ToString(CultureInfo.InvariantCulture), HorizontalAlignment = HorizontalAlignment.Stretch };
        var scaleBox = new TextBox { Text = src.Scale.ToString(CultureInfo.InvariantCulture), HorizontalAlignment = HorizontalAlignment.Stretch };
        var rotBox = new TextBox { Text = src.Rotation.ToString(CultureInfo.InvariantCulture), HorizontalAlignment = HorizontalAlignment.Stretch };

        var resetBtn = new Button { Content = "Reset", HorizontalAlignment = HorizontalAlignment.Left };
        resetBtn.Click += (_, _) =>
        {
            txBox.Text = "0"; tyBox.Text = "0"; scaleBox.Text = "1"; rotBox.Text = "0";
        };

        var panel = new StackPanel { Spacing = 8, Width = 420 };
        panel.Children.Add(new TextBlock { Text = "Translate X (px)", FontSize = 12 });
        panel.Children.Add(txBox);
        panel.Children.Add(new TextBlock { Text = "Translate Y (px)", FontSize = 12 });
        panel.Children.Add(tyBox);
        panel.Children.Add(new TextBlock { Text = "Scale (1.0 = 100%)", FontSize = 12 });
        panel.Children.Add(scaleBox);
        panel.Children.Add(new TextBlock { Text = "Rotation (degrees)", FontSize = 12 });
        panel.Children.Add(rotBox);
        panel.Children.Add(resetBtn);

        var dlg = new ContentDialog
        {
            Title = $"Edit Transform: {src.Name}",
            Content = new ScrollViewer { Content = panel, MaxHeight = 520 },
            PrimaryButtonText = "OK",
            CloseButtonText = "Cancel",
            XamlRoot = XamlRoot,
        };

        if (await dlg.ShowAsync() == ContentDialogResult.Primary)
        {
            if (double.TryParse(txBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var tx)) src.TranslateX = tx;
            if (double.TryParse(tyBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var ty)) src.TranslateY = ty;
            if (double.TryParse(scaleBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var s)) src.Scale = s <= 0 ? 1.0 : s;
            if (double.TryParse(rotBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var r)) src.Rotation = r;
            try { ApplySourceTransform(src); } catch { }
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
            // Use triple buffering (3 frames) to reduce flickering
            _mainCapturePool = Direct3D11CaptureFramePool.Create(
                _mainCaptureDevice, DirectXPixelFormat.B8G8R8A8UIntNormalized, 3, item.Size);
            _mainCaptureSession = _mainCapturePool.CreateCaptureSession(item);
#pragma warning disable CA1416
            if (Windows.Foundation.Metadata.ApiInformation.IsPropertyPresent("Windows.Graphics.Capture.GraphicsCaptureSession", "IsCursorCaptureEnabled"))
                _mainCaptureSession.IsCursorCaptureEnabled = true;
#pragma warning restore CA1416
            
            // Set up UI visibility BEFORE starting capture to prevent flashing
            PreviewScreenImage.Visibility = Visibility.Visible;
            PreviewHintPanel.Visibility   = Visibility.Collapsed;
            LivePreviewScreenImage.Source     = _mainBitmapSrc;
            LivePreviewScreenImage.Visibility = Visibility.Visible;
            LivePreviewElement.SetMediaPlayer(null);
            LivePreviewElement.Visibility     = Visibility.Collapsed;
            LivePreviewHint.Visibility       = Visibility.Collapsed;
            
            _mainCapturePool.FrameArrived += (pool, _) =>
            {
                // Drop frames if we're still processing the previous one
                if (_mainCaptureBusy) 
                { 
                    pool.TryGetNextFrame()?.Dispose(); 
                    return; 
                }
                _mainCaptureBusy = true;
                var frame = pool.TryGetNextFrame();
                if (frame == null) { _mainCaptureBusy = false; return; }
                
                // Process frame on UI thread
                DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, async () =>
                {
                    try
                    {
                        var sb = await SoftwareBitmap.CreateCopyFromSurfaceAsync(frame.Surface);
                        if (sb.BitmapPixelFormat != BitmapPixelFormat.Bgra8 ||
                            sb.BitmapAlphaMode   != BitmapAlphaMode.Premultiplied)
                        {
                            var converted = SoftwareBitmap.Convert(sb, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
                            sb.Dispose();
                            sb = converted;
                        }
                        await _mainBitmapSrc.SetBitmapAsync(sb);
                        sb.Dispose();
                    }
                    catch { }
                    finally 
                    { 
                        frame.Dispose();
                        _mainCaptureBusy = false; 
                    }
                });
            };
            
            // Start capture AFTER everything is set up
            _mainCaptureSession.StartCapture();

            // Apply any transform for the currently selected source
            if (SourcesList.SelectedItem is SourceItem sel)
            {
                try { ApplySourceTransform(sel); } catch { }
            }
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

    private void ApplySourceTransform(SourceItem src)
    {
        var tx = src.TranslateX;
        var ty = src.TranslateY;
        var s  = src.Scale <= 0 ? 1.0 : src.Scale;
        var r  = src.Rotation;

        var transform = new CompositeTransform
        {
            TranslateX = tx,
            TranslateY = ty,
            ScaleX     = s,
            ScaleY     = s,
            Rotation   = r,
        };

        // Apply to screen preview images
        if (src.SourceType.StartsWith("screen", StringComparison.OrdinalIgnoreCase) ||
            src.SourceType.StartsWith("window", StringComparison.OrdinalIgnoreCase))
        {
            PreviewScreenImage.RenderTransform = transform;
            PreviewScreenImage.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
            LivePreviewScreenImage.RenderTransform = transform;
            LivePreviewScreenImage.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
        }
        // Apply to video/webcam preview elements
        if (src.SourceType.StartsWith("video", StringComparison.OrdinalIgnoreCase) ||
            src.SourceType.StartsWith("video=", StringComparison.OrdinalIgnoreCase))
        {
            PreviewVideoElement.RenderTransform = transform;
            PreviewVideoElement.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
            LivePreviewElement.RenderTransform = transform;
            LivePreviewElement.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
        }
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
            // Apply any transform for the currently selected source
            if (SourcesList.SelectedItem is SourceItem sel)
            {
                try { ApplySourceTransform(sel); } catch { }
            }
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

    // ── Preview/LIVE splitter handlers ──────────────────────────────
    private void PreviewLiveSplitter_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        _isResizingPreviewSplitter = true;
        (sender as UIElement)?.CapturePointer(e.Pointer);
        _splitterStartX = e.GetCurrentPoint(this).Position.X;
        _previewStartWidth = PreviewHost.ActualWidth;
        _liveStartWidth = LivePreviewHost.ActualWidth;
    }

    private void PreviewLiveSplitter_PointerMoved(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (!_isResizingPreviewSplitter) return;
        var pt = e.GetCurrentPoint(this).Position;
        var dx = pt.X - _splitterStartX;
        var newPreview = Math.Max(MinPreviewWidth, _previewStartWidth + dx);
        var newLive = Math.Max(MinLiveWidth, _liveStartWidth - dx);
        PreviewMainCol.Width = new GridLength(newPreview, GridUnitType.Pixel);
        LiveSideCol.Width = new GridLength(newLive, GridUnitType.Pixel);
    }

    private void PreviewLiveSplitter_PointerReleased(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (!_isResizingPreviewSplitter) return;
        _isResizingPreviewSplitter = false;
        (sender as UIElement)?.ReleasePointerCapture(e.Pointer);
        // Persist preview / live column widths
        try
        {
            _settings.Set("layout.preview.main.width", PreviewMainCol.Width.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            _settings.Set("layout.live.side.width", LiveSideCol.Width.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        catch { }
    }

    private void PreviewLiveSplitter_PointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        try
        {
            PreviewLiveSplitter.Background = (Brush)Application.Current.Resources["BgHoverBrush"];
            Window.Current.CoreWindow.PointerCursor = new Windows.UI.Core.CoreCursor(Windows.UI.Core.CoreCursorType.SizeWestEast, 1);
        }
        catch { }
    }

    private void PreviewLiveSplitter_PointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        try
        {
            PreviewLiveSplitter.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0,0,0,0));
            Window.Current.CoreWindow.PointerCursor = new Windows.UI.Core.CoreCursor(Windows.UI.Core.CoreCursorType.Arrow, 0);
        }
        catch { }
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

            _recordingStartTime        = DateTime.Now;
            SaveClipBtn.IsEnabled      = true;
            StartRecordBtn.Visibility  = Visibility.Collapsed;
            StopRecordBtn.Visibility   = Visibility.Visible;
            PauseRecordBtn.Visibility  = Visibility.Visible;
            RecordBtnText.Text         = "Stop Recording";
            MenuStartRecord.IsEnabled  = false;
            MenuStopRecord.IsEnabled   = true;
            RecordingBadge.Visibility  = Visibility.Visible;
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

        SaveClipBtn.IsEnabled      = false;
        StartRecordBtn.Visibility  = Visibility.Visible;
        StopRecordBtn.Visibility   = Visibility.Collapsed;
        PauseRecordBtn.Visibility  = Visibility.Collapsed;
        _recordingPaused           = false;
        PauseRecordIcon.Glyph      = "\uE769"; // reset to Pause icon
        RecordBtnText.Text         = "Start Recording";
        MenuStartRecord.IsEnabled  = true;
        MenuStopRecord.IsEnabled   = false;
        RecordingBadge.Visibility  = Visibility.Collapsed;
        if (StatusRecTime is not null) StatusRecTime.Text = "00:00:00";
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
        LeftPanelsColumn.Width = new GridLength(175);
        MixerColumn.Width      = new GridLength(1, GridUnitType.Star);
        TransColumn.Width      = new GridLength(155);
        ControlsColumn.Width   = new GridLength(155);
    }

    // ── Probe / Refresh ───────────────────────────────────────────────────────

    private void RefreshSourcesBtn_Click(object sender, RoutedEventArgs e) => _ = LoadCaptureSources();

    private void ProbeDevicesBtn_Click(object sender, RoutedEventArgs e)   => _ = ProbeAndPopulateDevicesAsync();

    // ── Export / Library ──────────────────────────────────────────────────────

    private void ExportBtn_Click(object sender, RoutedEventArgs e)
        => App.MainWindow?.NavigateTo("library");

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void SetStatus(string text)
    {
        if (StatusText is not null)
            StatusText.Text = text;
    }

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
        SetStatus("Marker added at current position");
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
        SetStatus("Effects panel opened — drag sliders to adjust");
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
        SetStatus(_floatingMicMuted ? "Microphone muted" : "Microphone enabled");
    }

    private void FloatingCamBtn_Click(object sender, RoutedEventArgs e)
    {
        _floatingCamOn = !_floatingCamOn;
        SetStatus(_floatingCamOn ? "Camera enabled" : "Camera disabled");
    }

    private void FloatingDrawBtn_Click(object sender, RoutedEventArgs e)
    {
        _floatingDrawMode = !_floatingDrawMode;
        SetStatus(_floatingDrawMode ? "Screen annotation mode ON — draw on screen" : "Screen annotation mode OFF");
    }

    private void FloatingZoomBtn_Click(object sender, RoutedEventArgs e)
    {
        SetStatus("Zoom: use Ctrl+Scroll to zoom in/out");
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

    // ── Bottom panel splitter handlers ────────────────────────────────────────

    private void PanelSplitter_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (_docksLocked) return;
        if (sender is not Border splitter) return;
        _isResizingPanelSplitter = true;
        _activePanelSplitter = splitter.Tag?.ToString() ?? "";
        (sender as UIElement)?.CapturePointer(e.Pointer);
        _panelSplitterStartX = e.GetCurrentPoint(this).Position.X;

        (_panelLeftStartW, _panelRightStartW) = _activePanelSplitter switch
        {
            "left-right"      => (LeftPanelsColumn.ActualWidth, 0),
            "mixer-trans"     => (MixerColumn.ActualWidth,      TransColumn.ActualWidth),
            "trans-controls"  => (TransColumn.ActualWidth,      ControlsColumn.ActualWidth),
            _                 => (0, 0),
        };
    }

    private void PanelSplitter_PointerMoved(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (!_isResizingPanelSplitter) return;
        var dx = e.GetCurrentPoint(this).Position.X - _panelSplitterStartX;
        switch (_activePanelSplitter)
        {
            case "left-right":
                LeftPanelsColumn.Width = new GridLength(Math.Max(100, _panelLeftStartW + dx));
                break;
            case "mixer-trans":
                MixerColumn.Width = new GridLength(Math.Max(150, _panelLeftStartW  + dx));
                TransColumn.Width = new GridLength(Math.Max(100, _panelRightStartW - dx));
                break;
            case "trans-controls":
                TransColumn.Width    = new GridLength(Math.Max(100, _panelLeftStartW  + dx));
                ControlsColumn.Width = new GridLength(Math.Max(120, _panelRightStartW - dx));
                break;
        }
    }

    private void PanelSplitter_PointerReleased(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (!_isResizingPanelSplitter) return;
        _isResizingPanelSplitter = false;
        (sender as UIElement)?.ReleasePointerCapture(e.Pointer);
        // Persist layout
        try
        {
            _settings.Set("layout.leftpanels.width", LeftPanelsColumn.Width.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            _settings.Set("layout.mixer.width", MixerColumn.Width.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            _settings.Set("layout.trans.width", TransColumn.Width.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            _settings.Set("layout.controls.width", ControlsColumn.Width.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        catch { }
    }

    private void PanelSplitter_PointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (_docksLocked || sender is not Border b) return;
        b.Background = (Brush)Application.Current.Resources["BgHoverBrush"];
        // Set resize cursor (horizontal splitter)
        try
        {
            Window.Current.CoreWindow.PointerCursor = new Windows.UI.Core.CoreCursor(Windows.UI.Core.CoreCursorType.SizeWestEast, 1);
        }
        catch { /* WinUI 3 may not support CoreWindow */ }
    }

    private void PanelSplitter_PointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (sender is Border b)
            b.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(25, 255, 255, 255));
        // Restore arrow cursor
        try
        {
            Window.Current.CoreWindow.PointerCursor = new Windows.UI.Core.CoreCursor(Windows.UI.Core.CoreCursorType.Arrow, 1);
        }
        catch { /* WinUI 3 may not support CoreWindow */ }
    }

    // ── Left panel (Scenes/Sources horizontal) splitter handlers ─────────────

    private bool   _isResizingLeftSplitter;
    private double _leftSplitterStartY;
    private double _scenesRowStartH;
    private double _sourcesRowStartH;

    private void LeftPanelSplitter_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (_docksLocked) return;
        _isResizingLeftSplitter = true;
        (sender as UIElement)?.CapturePointer(e.Pointer);
        _leftSplitterStartY = e.GetCurrentPoint(this).Position.Y;
        // The left panels grid is the parent; its Row 0 and Row 2 are * height
        // We read the current ActualHeight of ScenesPanel and SourcesPanel
        _scenesRowStartH  = ScenesPanel.ActualHeight;
        _sourcesRowStartH = SourcesPanel.ActualHeight;
    }

    private void LeftPanelSplitter_PointerMoved(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (!_isResizingLeftSplitter) return;
        var dy = e.GetCurrentPoint(this).Position.Y - _leftSplitterStartY;
        var newScenes  = Math.Max(60, _scenesRowStartH  + dy);
        var newSources = Math.Max(60, _sourcesRowStartH - dy);
        // Update the grid row heights on the left panels container
        if (ScenesPanel.Parent is Grid leftGrid)
        {
            leftGrid.RowDefinitions[0].Height = new GridLength(newScenes,  GridUnitType.Pixel);
            leftGrid.RowDefinitions[2].Height = new GridLength(newSources, GridUnitType.Pixel);
        }
    }

    private void LeftPanelSplitter_PointerReleased(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (!_isResizingLeftSplitter) return;
        _isResizingLeftSplitter = false;
        (sender as UIElement)?.ReleasePointerCapture(e.Pointer);
        // Persist left panels heights
        try
        {
            if (ScenesPanel.Parent is Grid lg)
            {
                var scenesH = lg.RowDefinitions[0].ActualHeight;
                var sourcesH = lg.RowDefinitions[2].ActualHeight;
                _settings.Set("layout.leftpanels.scenes.height", scenesH.ToString(System.Globalization.CultureInfo.InvariantCulture));
                _settings.Set("layout.leftpanels.sources.height", sourcesH.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
        }
        catch { }
    }

    private void LeftPanelSplitter_PointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (_docksLocked || sender is not Border b) return;
        b.Background = (Brush)Application.Current.Resources["BgHoverBrush"];
        // Set resize cursor (vertical splitter)
        try
        {
            Window.Current.CoreWindow.PointerCursor = new Windows.UI.Core.CoreCursor(Windows.UI.Core.CoreCursorType.SizeNorthSouth, 1);
        }
        catch { /* WinUI 3 may not support CoreWindow */ }
    }

    private void LeftPanelSplitter_PointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (sender is Border b)
            b.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(25, 255, 255, 255));
        // Restore arrow cursor
        try
        {
            Window.Current.CoreWindow.PointerCursor = new Windows.UI.Core.CoreCursor(Windows.UI.Core.CoreCursorType.Arrow, 1);
        }
        catch { /* WinUI 3 may not support CoreWindow */ }
    }

    // ── Lock Docks ────────────────────────────────────────────────────────────

    private void MenuLockDocks_Click(object sender, RoutedEventArgs e)
    {
        _docksLocked = !_docksLocked;
        if (sender is ToggleMenuFlyoutItem item)
            item.IsChecked = _docksLocked;
        // Visually indicate locked state on splitters
        var opacity = _docksLocked ? 0.3 : 1.0;
        if (LeftRightSplitter           is not null) LeftRightSplitter.Opacity           = opacity;
        if (ScenesSourcesHorzSplitter   is not null) ScenesSourcesHorzSplitter.Opacity   = opacity;
        if (MixerTransSplitter          is not null) MixerTransSplitter.Opacity          = opacity;
        if (TransControlsSplitter       is not null) TransControlsSplitter.Opacity       = opacity;
        SetStatus(_docksLocked ? "Docks locked" : "Docks unlocked");
    }

    // ── Start Streaming ───────────────────────────────────────────────────────

    private void StartStreamBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_isStreaming)
        {
            // Stop streaming
            _isStreaming = false;
            _streamTimer?.Stop();
            _streamTimer = null;
            StreamBtnText.Text     = "Start Streaming";
            if (StatusStreamTime is not null) StatusStreamTime.Text = "00:00:00";
            StartStreamBtn.Style   = (Style)Application.Current.Resources["ControlsPanelBtnStyle"];
            SetStatus("Streaming stopped");
        }
        else
        {
            // Start streaming
            _isStreaming    = true;
            _streamStartTime = DateTime.Now;
            StreamBtnText.Text = "Stop Streaming";
            StartStreamBtn.Style = (Style)Application.Current.Resources["AlvoniaRecordButtonStyle"];

            _streamTimer = new System.Timers.Timer(500);
            _streamTimer.Elapsed += (_, _) =>
            {
                var elapsed = DateTime.Now - _streamStartTime;
                DispatcherQueue.TryEnqueue(() => {
                    if (StatusStreamTime is not null) StatusStreamTime.Text = elapsed.ToString(@"hh\:mm\:ss");
                });
            };
            _streamTimer.Start();
            SetStatus("Streaming started (stub — configure stream key in Settings)");
        }
    }

    // ── Pause Recording ───────────────────────────────────────────────────────

    private void PauseRecordBtn_Click(object sender, RoutedEventArgs e)
    {
        _recordingPaused = !_recordingPaused;
        PauseRecordIcon.Glyph = _recordingPaused ? "\uE768" : "\uE769"; // Play : Pause
        SetStatus(_recordingPaused ? "Recording paused" : "Recording resumed");
    }

    // ── Virtual Camera ────────────────────────────────────────────────────────

    private void VirtualCamBtn_Click(object sender, RoutedEventArgs e)
    {
        _virtualCamActive = !_virtualCamActive;
        VirtualCamBtnText.Text = _virtualCamActive ? "Stop Virtual Camera" : "Start Virtual Camera";
        VirtualCamBtn.Style = _virtualCamActive
            ? (Style)Application.Current.Resources["AlvoniaPrimaryButtonStyle"]
            : (Style)Application.Current.Resources["ControlsPanelBtnStyle"];
        SetStatus(_virtualCamActive
            ? "Virtual Camera active (requires OBS-VirtualCam plugin)"
            : "Virtual Camera stopped");
    }

    // ── Float / Undock panels ─────────────────────────────────────────────────

    private void FloatPanel_Click(object sender, RoutedEventArgs e)
    {
        var tag = (sender as FrameworkElement)?.Tag?.ToString() ?? "";
        FloatPanelByTag(tag);
    }

    private void MenuFloatPanel_Click(object sender, RoutedEventArgs e)
    {
        var tag = (sender as MenuFlyoutItem)?.Tag?.ToString() ?? "";
        FloatPanelByTag(tag);
    }

    private void FloatPanelByTag(string tag)
    {
        UIElement? panelContent = tag switch
        {
            "scenes"      => ScenesPanel,
            "sources"     => SourcesPanel,
            "mixer"       => MixerPanel,
            "transitions" => TransitionsPanel,
            "controls"    => ControlsPanel,
            _             => null,
        };

        if (panelContent == null) return;

        // If already hosted in a floating window, bring it front
        // Note: Parent is never Window in this WinUI 3 context, but check is kept for future compatibility
#pragma warning disable CS0184
        if (panelContent is FrameworkElement feCheck && feCheck.Parent is Window) return;
#pragma warning restore CS0184

        // Create a floating window and move the panel into it
        var win = new Window();
        win.Title = tag switch
        {
            "scenes"      => "Scenes — RecordIt",
            "sources"     => "Sources — RecordIt",
            "mixer"       => "Audio Mixer — RecordIt",
            "transitions" => "Scene Transitions — RecordIt",
            "controls"    => "Controls — RecordIt",
            _             => $"{tag} — RecordIt",
        };
        // Detach from current parent and host in new window
        try
        {
            if (panelContent is FrameworkElement fe && fe.Parent is Microsoft.UI.Xaml.Controls.Panel parentPanel)
            {
                parentPanel.Children.Remove(panelContent);
                win.Content = panelContent;
                win.Activate();
                PositionFloatingWindow(win, 360, 480);
                SetStatus($"{win.Title} floated to separate window");
            }
        }
        catch { /* ignore */ }
    }

    // ── Panel header drag handlers (basic snap-to-dock) ───────────────────
    private void PanelHeader_PointerPressed(object? sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (_docksLocked) return;
        if (sender is Border header)
        {
            _dragStartPoint = e.GetCurrentPoint(this).Position;
            // Map header to its parent panel grid
            var panel = header.Parent as FrameworkElement;
            while (panel != null && !(panel is Grid && (panel.Name.EndsWith("Panel") || panel.Name == "ScenesPanel")))
            {
                panel = panel.Parent as FrameworkElement;
            }
            _draggedPanel = panel as UIElement ?? header.Parent as UIElement;

            // If header is inside a TabViewItem, capture the TabViewItem so we can remove/reorder it
            _draggedFromTabView = null;
            _draggedTabItem = null;
            try
            {
                var p = header as FrameworkElement;
                while (p != null)
                {
                    if (p is Microsoft.UI.Xaml.Controls.TabViewItem tvi)
                    {
                        // find parent TabView
                        var parent = tvi.Parent;
                        while (parent != null && !(parent is Microsoft.UI.Xaml.Controls.TabView))
                        {
                            parent = (parent as FrameworkElement)?.Parent;
                        }
                        _draggedFromTabView = parent as Microsoft.UI.Xaml.Controls.TabView;
                        _draggedTabItem = tvi;
                        break;
                    }
                    p = p.Parent as FrameworkElement;
                }
            }
            catch { }
            _isDraggingPanel = true;

            // Drag visual handled by header highlight; no floating ghost added here.
            _dragGhost = null;

            // Start hold timer (for floating release behavior)
            _holdTriggered = false;
            _holdTimer = new System.Timers.Timer(500) { AutoReset = false };
            _holdTimer.Elapsed += (_, _) =>
            {
                _holdTriggered = true;
            };
            _holdTimer.Start();
        }
    }

    private void PanelHeader_PointerMoved(object? sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (!_isDraggingPanel || _draggedPanel == null) return;
        var pt = e.GetCurrentPoint(this).Position;
        // Move ghost
        // (no ghost movement; visual feedback via header background)
        // Determine hover dock target and show accept indicator
        var hit = HitTestForDockTarget(pt);
        // Restore previous highlights
        foreach (var kv in _originalHeaderBrush)
        {
            try
            {
                if (kv.Key is Control ctrl) ctrl.Background = kv.Value;
                else if (kv.Key is Border br) br.Background = kv.Value;
            }
            catch { }
        }
        _originalHeaderBrush.Clear();
        if (hit is FrameworkElement fe)
        {
            // store old brush and set a highlight on supported types
            var highlight = new SolidColorBrush(Windows.UI.Color.FromArgb(60, 99, 102, 241));
            if (fe is Control c)
            {
                _originalHeaderBrush[c] = c.Background;
                c.Background = highlight;
            }
            else if (fe is Border b)
            {
                _originalHeaderBrush[b] = b.Background;
                b.Background = highlight;
            }
        }

        // Show dock overlay visuals for current hit target
        ShowDockOverlayForTarget(hit);
    }

    private void PanelHeader_PointerReleased(object? sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (!_isDraggingPanel) return;
        _isDraggingPanel = false;
        _holdTimer?.Stop();
        _holdTimer = null;

        // Remove ghost
        if (_dragGhost != null)
        {
            Window.Current.Content.DispatcherQueue.TryEnqueue(() =>
            {
                var root = Window.Current.Content as FrameworkElement;
                if (root is Grid g && g.Children.Contains(_dragGhost)) g.Children.Remove(_dragGhost);
            });
            _dragGhost = null;
        }

        // Determine drop target based on pointer position
        var pt = e.GetCurrentPoint(this).Position;
        var target = HitTestForDockTarget(pt);
        // Hide overlays
        HideDockOverlay();

        // Modifier-aware behavior: Shift = float, Ctrl = tab, otherwise default
        var forceFloat = IsShiftPressed() || _holdTriggered;
        var forceTab = IsCtrlPressed();

        if (forceFloat)
        {
            if (_draggedPanel != null)
            {
                var win = new Window();
                string tag = GetPanelTagFromElement(_draggedPanel as FrameworkElement);
                DetachFromParent(_draggedPanel);
                win.Content = _draggedPanel;
                win.Activate();
                PositionFloatingWindow(win, 360, 480);
                RegisterFloatingWindow(win, tag);
                SetStatus("Panel floated to separate window");
            }
        }
        else if (target != null)
        {
            // If dragged from a TabView, remove its original TabViewItem
            if (_draggedFromTabView != null && _draggedTabItem != null)
            {
                try { _draggedFromTabView.TabItems.Remove(_draggedTabItem); } catch { }
                _draggedFromTabView = null; _draggedTabItem = null;
            }

            // Ctrl forces adding as a tab
            if (forceTab)
            {
                    // If the target's parent already contains a TabView, append as tab
                if (target.Parent is Microsoft.UI.Xaml.Controls.Panel tp)
                {
                    Microsoft.UI.Xaml.Controls.TabView? existing = null;
                    foreach (var c in tp.Children) if (c is Microsoft.UI.Xaml.Controls.TabView tv) { existing = tv; break; }
                    if (existing != null)
                    {
                        var title = GetPanelTitle(_draggedPanel as FrameworkElement) ?? "Panel";
                        HidePanelHeader(_draggedPanel as FrameworkElement);
                        DetachFromParent(_draggedPanel);
                        var item = new Microsoft.UI.Xaml.Controls.TabViewItem { Header = title, Content = _draggedPanel };
                        existing.TabItems.Add(item);
                        existing.SelectedItem = item;
                        PersistTabOrder(GetPanelTagFromElement(target), existing);
                    }
                    else
                    {
                        DockPanelToTarget(_draggedPanel, target);
                    }
                }
                else
                {
                    DockPanelToTarget(_draggedPanel, target);
                }
            }
            else
            {
                DockPanelToTarget(_draggedPanel, target);
            }

            SetStatus("Panel docked");
        }
        else
        {
            // No target -> float
            if (_draggedPanel != null)
            {
                var win = new Window();
                string tag = GetPanelTagFromElement(_draggedPanel as FrameworkElement);
                DetachFromParent(_draggedPanel);
                win.Content = _draggedPanel;
                win.Activate();
                PositionFloatingWindow(win, 360, 480);
                RegisterFloatingWindow(win, tag);
                SetStatus("Panel floated to separate window");
            }
        }

        _draggedPanel = null;
    }

    private FrameworkElement? HitTestForDockTarget(Windows.Foundation.Point p)
    {
        // Enhanced hit-testing with center/top/tab targets
        try
        {
            var w = this.ActualWidth;
            var h = this.ActualHeight;
            // left dock: within left 220px
            if (p.X < 220) return ScenesPanel;
            // right dock: within right 280px
            if (p.X > w - 280) return ControlsPanel;
            // bottom dock: within bottom 260px
            if (p.Y > h - 260) return MixerPanel;
            // top dock above preview: within 120px from top of preview host
            var previewHostTransform = PreviewHost.TransformToVisual(this);
            var previewTop = previewHostTransform.TransformPoint(new Point(0, 0)).Y;
            if (p.Y >= previewTop && p.Y < previewTop + 120) return DockAcceptTop;
            // center tab target: near screen centre
            var cx = w / 2.0;
            var cy = h / 2.0;
            var dx = Math.Abs(p.X - cx);
            var dy = Math.Abs(p.Y - cy);
            if (dx < 200 && dy < 120) return DockAcceptTab;
        }
        catch { }
        return null;
    }

    private string GetPanelTagFromElement(FrameworkElement? fe)
    {
        if (fe == ScenesPanel) return "scenes";
        if (fe == SourcesPanel) return "sources";
        if (fe == MixerPanel) return "mixer";
        if (fe == TransitionsPanel) return "transitions";
        if (fe == ControlsPanel) return "controls";
        return "panel";
    }

    private void RegisterFloatingWindow(Window win, string tag)
    {
        try
        {
            // Save a simple open flag and the window bounds
            var hwnd = WindowNativeInterop.GetWindowHandle(win);
            if (!GetWindowRect(hwnd, out var r)) return;
            _settings.Set($"floating.{tag}.open", "1");
            _settings.Set($"floating.{tag}.x", r.Left.ToString(CultureInfo.InvariantCulture));
            _settings.Set($"floating.{tag}.y", r.Top.ToString(CultureInfo.InvariantCulture));
            _settings.Set($"floating.{tag}.w", (r.Right - r.Left).ToString(CultureInfo.InvariantCulture));
            _settings.Set($"floating.{tag}.h", (r.Bottom - r.Top).ToString(CultureInfo.InvariantCulture));

            win.Closed += (_, _) =>
            {
                try { _settings.Set($"floating.{tag}.open", "0"); } catch { }
            };
        }
        catch { }
    }

    private void RestoreFloatingElements()
    {
        // Restore known panels
        var panels = new (string tag, FrameworkElement element)[] {
            ("scenes", ScenesPanel), ("sources", SourcesPanel), ("mixer", MixerPanel), ("transitions", TransitionsPanel), ("controls", ControlsPanel)
        };
        foreach (var (tag, element) in panels)
        {
            try
            {
                var open = _settings.Get($"floating.{tag}.open");
                if (open != "1") continue;
                var xs = _settings.Get($"floating.{tag}.x");
                var ys = _settings.Get($"floating.{tag}.y");
                var ws = _settings.Get($"floating.{tag}.w");
                var hs = _settings.Get($"floating.{tag}.h");
                if (!int.TryParse(xs, out var x)) continue;
                if (!int.TryParse(ys, out var y)) continue;
                if (!int.TryParse(ws, out var w)) w = 360;
                if (!int.TryParse(hs, out var h)) h = 480;

                // detach element from current parent
                DetachFromParent(element);
                var win = new Window();
                win.Content = element;
                win.Activate();
                var hwnd = WindowNativeInterop.GetWindowHandle(win);
                SetWindowPos(hwnd, 0, x, y, w, h, SWP_NOZORDER | SWP_NOACTIVATE);
            }
            catch { }
        }

        // Magnifier
        try
        {
            var magOpen = _settings.Get("magnifier.open");
            if (magOpen == "1")
            {
                var xs = _settings.Get("magnifier.x");
                var ys = _settings.Get("magnifier.y");
                var ws = _settings.Get("magnifier.w");
                var hs = _settings.Get("magnifier.h");
                var zw = new ZoomWindow(IntPtr.Zero);
                zw.ImageSource = _mainBitmapSrc;
                zw.Activate();
                try
                {
                    if (int.TryParse(xs, out var x) && int.TryParse(ys, out var y) && int.TryParse(ws, out var w) && int.TryParse(hs, out var h))
                    {
                        var hwnd = WindowNativeInterop.GetWindowHandle(zw);
                        SetWindowPos(hwnd, 0, x, y, w, h, SWP_NOZORDER | SWP_NOACTIVATE);
                    }
                }
                catch { }
            }
        }
        catch { }

        // Restore tab orders for main regions
        try
        {
            RestoreTabOrderForRegion("scenes", ScenesPanel);
            RestoreTabOrderForRegion("sources", SourcesPanel);
            RestoreTabOrderForRegion("mixer", MixerPanel);
            RestoreTabOrderForRegion("transitions", TransitionsPanel);
            RestoreTabOrderForRegion("controls", ControlsPanel);
        }
        catch { }
    }

    private void ShowDockOverlayForTarget(FrameworkElement? target)
    {
        try
        {
            // make overlay visible and animate opacity
            DockOverlayRoot.Visibility = Visibility.Visible;
            DockAcceptLeft.Visibility = Visibility.Collapsed;
            DockAcceptRight.Visibility = Visibility.Collapsed;
            DockAcceptBottom.Visibility = Visibility.Collapsed;
            DockAcceptTab.Visibility = Visibility.Collapsed;
            DockAcceptTop.Visibility = Visibility.Collapsed;

            if (target == ScenesPanel)
            {
                DockAcceptLeft.Visibility = Visibility.Visible;
                AnimateOverlay(DockAcceptLeft, true);
            }
            else if (target == MixerPanel)
            {
                DockAcceptBottom.Visibility = Visibility.Visible;
                AnimateOverlay(DockAcceptBottom, true);
            }
            else if (target == ControlsPanel)
            {
                DockAcceptRight.Visibility = Visibility.Visible;
                AnimateOverlay(DockAcceptRight, true);
            }
            else if (target == DockAcceptTop)
            {
                DockAcceptTop.Visibility = Visibility.Visible;
                AnimateOverlay(DockAcceptTop, true);
            }
            else if (target == DockAcceptTab)
            {
                DockAcceptTab.Visibility = Visibility.Visible;
                AnimateOverlay(DockAcceptTab, true);
            }
        }
        catch { }
    }

    private void AnimateOverlay(FrameworkElement element, bool show)
    {
        try
        {
            var sb = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
            var da = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
            {
                Duration = new Microsoft.UI.Xaml.Duration(TimeSpan.FromMilliseconds(180))
            };
            var prop = Microsoft.UI.Xaml.Media.Animation.Storyboard.TargetPropertyProperty;
            if (show) da.To = 1.0; else da.To = 0.0;
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(da, element);
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(da, "Opacity");
            sb.Children.Add(da);
            if (!show)
            {
                sb.Completed += (s, e) => { element.Visibility = Visibility.Collapsed; };
            }
            sb.Begin();
        }
        catch { }
    }

    private void HideDockOverlay()
    {
        try
        {
            DockOverlayRoot.Visibility = Visibility.Collapsed;
            DockAcceptLeft.Visibility = Visibility.Collapsed;
            DockAcceptRight.Visibility = Visibility.Collapsed;
            DockAcceptBottom.Visibility = Visibility.Collapsed;
            DockAcceptTab.Visibility = Visibility.Collapsed;
        }
        catch { }
    }

    private void CoreWindow_KeyDown(CoreWindow sender, KeyEventArgs args)
    {
        // no-op placeholder; we read modifier state directly when needed
    }

    private void CoreWindow_KeyUp(CoreWindow sender, KeyEventArgs args)
    {
        // no-op placeholder
    }

    private bool IsShiftPressed()
    {
        try
        {
            var s = CoreWindow.GetForCurrentThread().GetKeyState(VirtualKey.Shift);
            return (s & CoreVirtualKeyStates.Down) == CoreVirtualKeyStates.Down;
        }
        catch { return false; }
    }

    private bool IsCtrlPressed()
    {
        try
        {
            var s = CoreWindow.GetForCurrentThread().GetKeyState(VirtualKey.Control);
            return (s & CoreVirtualKeyStates.Down) == CoreVirtualKeyStates.Down;
        }
        catch { return false; }
    }

    private void DockPanelToTarget(UIElement? panel, FrameworkElement target)
    {
        if (panel == null || target == null) return;
        // If panel is currently in a parent, remove it
        DetachFromParent(panel);
        // Insert into target's parent grid cell. If the target already contains a TabView,
        // add the panel as a new tab. Otherwise replace the target with a TabView and
        // host both panels as tabs (hiding their internal headers to avoid duplication).
                var tgtParent = target.Parent as Microsoft.UI.Xaml.Controls.Panel;
        if (tgtParent == null) return;

        // Helper: find existing TabView sibling occupying same position
        Microsoft.UI.Xaml.Controls.TabView? existingTabView = null;
        foreach (var child in tgtParent.Children)
        {
            if (child is Microsoft.UI.Xaml.Controls.TabView tv) { existingTabView = tv; break; }
        }

        void HidePanelHeader(FrameworkElement pnl)
        {
            try
            {
                // try known header names
                if (pnl.FindName("ScenesHeader") is Border b) b.Visibility = Visibility.Collapsed;
                if (pnl.FindName("SourcesHeader") is Border b2) b2.Visibility = Visibility.Collapsed;
                if (pnl.FindName("MixerHeader") is Border b3) b3.Visibility = Visibility.Collapsed;
                if (pnl.FindName("TransitionsHeader") is Border b4) b4.Visibility = Visibility.Collapsed;
                if (pnl.FindName("ControlsHeader") is Border b5) b5.Visibility = Visibility.Collapsed;
            }
            catch { }
        }

        // Create a TabViewItem from a panel
        Microsoft.UI.Xaml.Controls.TabViewItem MakeTabFromPanel(FrameworkElement pnl, string title)
        {
            // Ensure panel is detached from any existing parent before placing into a TabViewItem
            DetachFromParent(pnl);
            HidePanelHeader(pnl);
            var item = new Microsoft.UI.Xaml.Controls.TabViewItem { Header = title, Content = pnl };
            return item;
        }

        // If there's an existing TabView, append this panel as a new tab
        if (existingTabView != null)
        {
            // Create item from the panel and add
            var panelElement = panel as FrameworkElement;
            if (panelElement == null) return;
            var title = GetPanelTitle(panelElement) ?? "Panel";
            var item = MakeTabFromPanel(panelElement, title);
            existingTabView.TabItems.Add(item);
            existingTabView.SelectedItem = item;
            // persist tab order for this region
            try { PersistTabOrder(GetPanelTagFromElement(target), existingTabView); } catch { }
            return;
        }

        // No TabView yet - replace the target element with a new TabView
        var tabView = new Microsoft.UI.Xaml.Controls.TabView();
        // preserve grid row/column if parent is Grid
        if (tgtParent is Grid g && tgtParent.Children.Contains(target))
        {
            int row = Grid.GetRow(target);
            int col = Grid.GetColumn(target);
            // Remove target from parent
            tgtParent.Children.Remove(target);
            // Create tab items for the original target and the new panel
            var panelElement = panel as FrameworkElement;
            if (panelElement == null) return;
            var titleA = GetPanelTitle(target) ?? "Panel";
            var titleB = GetPanelTitle(panelElement) ?? "Panel";
            tabView.TabItems.Add(MakeTabFromPanel(target, titleA));
            tabView.TabItems.Add(MakeTabFromPanel(panelElement, titleB));
            // Place TabView into the same grid cell
            Grid.SetRow(tabView, row);
            Grid.SetColumn(tabView, col);
            tgtParent.Children.Add(tabView);
            tabView.SelectedIndex = 1; // focus the newly added tab
            try { PersistTabOrder(GetPanelTagFromElement(target), tabView); } catch { }
            return;
        }

        // Fallback: add tabView to parent and add both panels as tabs
        try
        {
            var panelElement = panel as FrameworkElement;
            if (panelElement == null) return;
            var title1 = GetPanelTitle(target) ?? "Panel";
            var title2 = GetPanelTitle(panelElement) ?? "Panel";
            tabView.TabItems.Add(MakeTabFromPanel(target, title1));
            tabView.TabItems.Add(MakeTabFromPanel(panelElement, title2));
            tgtParent.Children.Add(tabView);
            tabView.SelectedIndex = 1;
        }
        catch { }
    }

    private string? GetPanelTitle(object? pnl)
    {
        try
        {
            if (pnl is FrameworkElement fe)
            {
                // look for a TextBlock inside known header names
                if (fe.FindName("ScenesHeader") is Border b && b.Child is Grid g1)
                {
                    foreach (var c in g1.Children)
                        if (c is TextBlock tb) return tb.Text;
                }
                if (fe.FindName("SourcesHeader") is Border b2 && b2.Child is Grid g2)
                {
                    foreach (var c in g2.Children)
                        if (c is TextBlock tb) return tb.Text;
                }
                if (fe.FindName("MixerHeader") is Border b3 && b3.Child is Grid g3)
                {
                    foreach (var c in g3.Children)
                        if (c is TextBlock tb) return tb.Text;
                }
                if (fe.FindName("TransitionsHeader") is Border b4 && b4.Child is Grid g4)
                {
                    foreach (var c in g4.Children)
                        if (c is TextBlock tb) return tb.Text;
                }
                if (fe.FindName("ControlsHeader") is Border b5 && b5.Child is Grid g5)
                {
                    foreach (var c in g5.Children)
                        if (c is TextBlock tb) return tb.Text;
                }
            }
        }
        catch { }
        return null;
    }

    private void HidePanelHeader(FrameworkElement? pnl)
    {
        try
        {
            if (pnl == null) return;
            if (pnl.FindName("ScenesHeader") is Border b) b.Visibility = Visibility.Collapsed;
            if (pnl.FindName("SourcesHeader") is Border b2) b2.Visibility = Visibility.Collapsed;
            if (pnl.FindName("MixerHeader") is Border b3) b3.Visibility = Visibility.Collapsed;
            if (pnl.FindName("TransitionsHeader") is Border b4) b4.Visibility = Visibility.Collapsed;
            if (pnl.FindName("ControlsHeader") is Border b5) b5.Visibility = Visibility.Collapsed;
        }
        catch { }
    }

    private void DetachFromParent(UIElement? element)
    {
        if (element == null) return;
        try
        {
            if (element is FrameworkElement fe)
            {
                var parent = fe.Parent;
                if (parent is Microsoft.UI.Xaml.Controls.Panel p)
                {
                    if (p.Children.Contains(element)) p.Children.Remove(element);
                    return;
                }
                if (parent is ContentControl cc)
                {
                    if (ReferenceEquals(cc.Content, element)) cc.Content = null;
                    return;
                }
                if (parent is Border b)
                {
                    if (ReferenceEquals(b.Child, element)) b.Child = null;
                    return;
                }
                if (parent is Microsoft.UI.Xaml.Controls.TabViewItem tvi)
                {
                    if (ReferenceEquals(tvi.Content, element)) tvi.Content = null;
                    return;
                }
            }
        }
        catch { }
    }

    private void PersistTabOrder(string regionTag, Microsoft.UI.Xaml.Controls.TabView tabView)
    {
        try
        {
            if (tabView == null || string.IsNullOrEmpty(regionTag)) return;
            var headers = new List<string>();
            foreach (var it in tabView.TabItems)
            {
                if (it is Microsoft.UI.Xaml.Controls.TabViewItem tvi)
                {
                    headers.Add(tvi.Header?.ToString() ?? "");
                }
            }
            var value = string.Join("|", headers);
            _settings.Set($"tabs.{regionTag}.order", value);
        }
        catch { }
    }

    private void RestoreTabOrderForRegion(string regionTag, FrameworkElement regionHost)
    {
        try
        {
            var val = _settings.Get($"tabs.{regionTag}.order");
            if (string.IsNullOrEmpty(val)) return;
            if (!(regionHost?.Parent is Microsoft.UI.Xaml.Controls.Panel parent)) return;
            Microsoft.UI.Xaml.Controls.TabView? tv = null;
            foreach (var c in parent.Children) if (c is Microsoft.UI.Xaml.Controls.TabView t) { tv = t; break; }
            if (tv == null) return;
            var desired = val.Split('|');
            // Reorder tabs to match desired order where possible
            var items = tv.TabItems.Cast<object>().OfType<Microsoft.UI.Xaml.Controls.TabViewItem>().ToList();
            var reordered = new List<Microsoft.UI.Xaml.Controls.TabViewItem>();
            foreach (var h in desired)
            {
                var found = items.FirstOrDefault(x => (x.Header?.ToString() ?? "") == h);
                if (found != null) { reordered.Add(found); items.Remove(found); }
            }
            // Append remaining
            reordered.AddRange(items);
            tv.TabItems.Clear();
            foreach (var it in reordered) tv.TabItems.Add(it);
        }
        catch { }
    }

    // ── Status bar toggle ─────────────────────────────────────────────────────

    private void MenuToggleStatusBar_Click(object sender, RoutedEventArgs e)
    {
        // Toggle the status bar row (Row 3) visibility
        if (StatusText?.Parent is Grid sbGrid && sbGrid.Parent is Border sbBorder)
        {
            bool nowVisible = sbBorder.Visibility == Visibility.Visible;
            sbBorder.Visibility = nowVisible ? Visibility.Collapsed : Visibility.Visible;
            if (sender is ToggleMenuFlyoutItem toggle) toggle.IsChecked = !nowVisible;
        }
    }

    // ── Transition handlers ───────────────────────────────────────────────────

    private void TransitionTypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TransitionTypeCombo.SelectedItem is ComboBoxItem item)
            SetStatus($"Transition: {item.Content}");
    }

    private void TransitionPropertiesBtn_Click(object sender, RoutedEventArgs e)
        => SetStatus("Transition properties — configure via Settings");

    // ── Properties / Filters bar handlers ────────────────────────────────────

    private async void PropertiesBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SourcesList.SelectedItem is SourceItem src)
            await ShowSourcePropertiesAsync(src);
        else
            SetStatus("Select a source first to open its properties");
    }

    private void FiltersBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SourcesList.SelectedItem is SourceItem src)
            SetStatus($"Filters for '{src.Name}' — coming soon");
        else
            SetStatus("Select a source first to open its filters");
    }

    private void TransitionDurationCustomBtn_Click(object sender, RoutedEventArgs e)
        => SetStatus("Set custom duration via the combo box");

    private void AddQuickTransitionBtn_Click(object sender, RoutedEventArgs e)
        => SetStatus("Quick transition added");

    private void RemoveQuickTransitionBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Parent is Grid g && g.Parent is Border b
            && b.Parent is StackPanel sp)
            sp.Children.Remove(b);
    }

    // ── Scene Collection menu handlers ────────────────────────────────────────

    private void MenuDuplicateScene_Click(object sender, RoutedEventArgs e)
    {
        if (ScenesList.SelectedItem is SceneItem s)
            AddScene($"{s.Name} (copy)");
        else
            SetStatus("Select a scene to duplicate");
    }

    private async void MenuRenameScene_Click(object sender, RoutedEventArgs e)
    {
        if (ScenesList.SelectedItem is not SceneItem s) { SetStatus("Select a scene to rename"); return; }
        var box = new TextBox { Text = s.Name, PlaceholderText = "Scene name" };
        var dlg = new ContentDialog
        {
            Title             = "Rename Scene",
            Content           = box,
            PrimaryButtonText = "Rename",
            CloseButtonText   = "Cancel",
            XamlRoot          = XamlRoot,
        };
        if (await dlg.ShowAsync() == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(box.Text))
        {
            s.Name = box.Text;
            ScenesList.ItemsSource = null;
            ScenesList.ItemsSource = Scenes;
            SetStatus($"Scene renamed to \"{box.Text}\"");
        }
    }

    private void MenuExportScene_Click(object sender, RoutedEventArgs e)
        => SetStatus("Export scene collection — not yet implemented");

    private void MenuImportScene_Click(object sender, RoutedEventArgs e)
        => SetStatus("Import scene collection — not yet implemented");

    // ── Profile menu handlers ─────────────────────────────────────────────────

    private void MenuNewProfile_Click(object sender, RoutedEventArgs e)       => SetStatus("New profile created");
    private void MenuDuplicateProfile_Click(object sender, RoutedEventArgs e) => SetStatus("Profile duplicated");
    private void MenuRenameProfile_Click(object sender, RoutedEventArgs e)    => SetStatus("Profile renamed");
    private void MenuExportProfile_Click(object sender, RoutedEventArgs e)    => SetStatus("Export profile — not yet implemented");
    private void MenuImportProfile_Click(object sender, RoutedEventArgs e)    => SetStatus("Import profile — not yet implemented");

    // ── Templates ─────────────────────────────────────────────────────────────

    private async void MenuApplyTemplate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem item) return;
        var tag = item.Tag?.ToString() ?? "";

        var (templateName, scenes) = tag switch
        {
            "gaming"      => ("Gaming / Game Capture",
                              new[] { "Game Capture", "Starting Soon", "Be Right Back", "Ending" }),
            "chatting"    => ("Just Chatting",
                              new[] { "Just Chatting", "BRB", "Starting", "Ending" }),
            "screenshare" => ("Screen Share / Presentation",
                              new[] { "Screen Share", "Waiting Room", "Q&A", "Break" }),
            "webcam"      => ("Webcam Only",
                              new[] { "Main View", "BRB", "Starting Soon" }),
            "irl"         => ("IRL / Mobile",
                              new[] { "IRL Live", "Intermission", "Ending" }),
            "podcast"     => ("Podcast / Interview",
                              new[] { "Guest 1", "Guest 2", "Both Guests", "Solo Host", "Intro/Outro" }),
            _             => ("", Array.Empty<string>()),
        };

        if (string.IsNullOrEmpty(templateName)) return;

        var dlg = new ContentDialog
        {
            Title             = $"Apply Template: {templateName}",
            Content           = $"This will add the following scenes:\n\n• {string.Join("\n• ", scenes)}\n\nExisting scenes will not be removed.",
            PrimaryButtonText = "Apply",
            CloseButtonText   = "Cancel",
            XamlRoot          = XamlRoot,
        };

        if (await dlg.ShowAsync() == ContentDialogResult.Primary)
        {
            foreach (var s in scenes)
                if (!Scenes.Any(x => x.Name.Equals(s, StringComparison.OrdinalIgnoreCase)))
                    AddScene(s);
            SetStatus($"Template \"{templateName}\" applied — {scenes.Length} scenes added");
        }
    }
}
