using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using RecordIt.Avalonia.Pages;
using RecordIt.Core.Services;
using Avalonia.Threading;
using System.Threading.Tasks;
using System.Linq;

namespace RecordIt.Avalonia;

public partial class MainWindow : Window
{
    private readonly RecordPage     _recordPage     = new();
    private readonly WhiteboardPage _whiteboardPage = new();
    private readonly LibraryPage    _libraryPage    = new();
    private readonly SettingsPage   _settingsPage   = new();

    private readonly ScreenRecordingService _captureService = new();

    private DispatcherTimer? _recTimer;

    public MainWindow()
    {
        InitializeComponent();
        NavigateTo("record");
        _ = SetupSourcesAsync();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    // ─── Navigation ──────────────────────────────────────────────────────────

    public void NavigateTo(string page)
    {
        ContentPanel.Content = page switch
        {
            "whiteboard" => (object)_whiteboardPage,
            "library"    => _libraryPage,
            "settings"   => _settingsPage,
            _            => _recordPage,
        };

        UpdateNavButton(RecordNavBtn,     page == "record");
        UpdateNavButton(WhiteboardNavBtn, page == "whiteboard");
        UpdateNavButton(LibraryNavBtn,    page == "library");
        UpdateNavButton(SettingsNavBtn,   page == "settings");
    }

    private static void UpdateNavButton(Button btn, bool active)
    {
        if (btn.Content is TextBlock tb)
        {
            tb.Foreground = active
                ? new SolidColorBrush(Color.Parse("#6366F1"))
                : new SolidColorBrush(Color.Parse("#666666"));
        }
    }

    // ─── Recording indicator ─────────────────────────────────────────────────

    public void StartRecordingIndicator(TimeSpan initial, DispatcherTimer timer)
    {
        RecordingIndicator.IsVisible = true;
        RecordingTimeText.Text = initial.ToString(@"mm\:ss");

        _recTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        var started = DateTime.UtcNow;
        _recTimer.Tick += (_, _) =>
        {
            var elapsed = DateTime.UtcNow - started;
            RecordingTimeText.Text = elapsed.ToString(@"mm\:ss");
        };
        _recTimer.Start();
    }

    public void StopRecordingIndicator()
    {
        _recTimer?.Stop();
        RecordingIndicator.IsVisible = false;
    }

    // ─── Title bar handlers ──────────────────────────────────────────────────

    private void RecordNavBtn_Click(object? sender, RoutedEventArgs e)     => NavigateTo("record");
    private void WhiteboardNavBtn_Click(object? sender, RoutedEventArgs e) => NavigateTo("whiteboard");
    private void LibraryNavBtn_Click(object? sender, RoutedEventArgs e)    => NavigateTo("library");
    private void SettingsNavBtn_Click(object? sender, RoutedEventArgs e)   => NavigateTo("settings");

    private void ThemeToggleBtn_Click(object? sender, RoutedEventArgs e)
    {
        if (ThemeToggleBtn.Content is string s)
            ThemeToggleBtn.Content = s == "🌙" ? "☀" : "🌙";
    }

    private void MinimizeBtn_Click(object? sender, RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    private void MaximizeBtn_Click(object? sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

    private void CloseBtn_Click(object? sender, RoutedEventArgs e) => Close();

    // Populate the consolidated Sources tabs and wire selection events
    private async Task SetupSourcesAsync()
    {
        try
        {
            // Find controls
            var displayList = this.FindControl<ListBox>("DisplayList");
            var windowList  = this.FindControl<ListBox>("WindowList");
            var cameraList  = this.FindControl<ListBox>("CameraList");

            if (displayList == null || windowList == null || cameraList == null)
                return;

            // Clear existing
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                displayList.Items = null;
                windowList.Items  = null;
                cameraList.Items  = null;
            });

            // Enumerate capture sources (monitors + windows)
            var sources = await _captureService.GetCaptureSources();
            var screens = sources.Where(s => s.Type == CaptureSourceType.Screen).ToList();
            var windows = sources.Where(s => s.Type == CaptureSourceType.Window).ToList();

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                foreach (var s in screens) displayList.Items = AppendItem(displayList, s);
                foreach (var s in windows)  windowList.Items  = AppendItem(windowList, s);
            });

            // Probe DirectShow devices for cameras/audio
            var devices = await _captureService.ProbeDevicesAsync();
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                int i = 0;
                foreach (var v in devices.VideoDevices)
                {
                    cameraList.Items = AppendItem(cameraList, v);
                    i++;
                }
            });

            // Wire selection handlers
            displayList.SelectionChanged += (s, e) => OnCaptureListSelection(displayList.SelectedItem);
            windowList.SelectionChanged  += (s, e) => OnCaptureListSelection(windowList.SelectedItem);
            cameraList.SelectionChanged  += (s, e) => OnCaptureListSelection(cameraList.SelectedItem);
        }
        catch { /* ignore probe errors */ }
    }

    private object[] AppendItem(ListBox lb, object item)
    {
        var items = lb.Items?.Cast<object>().ToList() ?? new System.Collections.Generic.List<object>();
        items.Add(item);
        return items.ToArray();
    }

    private void OnCaptureListSelection(object? selected)
    {
        if (selected == null) return;

        // If selected is CaptureSource, forward directly; if string (camera name), wrap
        if (selected is CaptureSource cs)
        {
            _recordPage.SelectCaptureSource(cs);
        }
        else if (selected is string name)
        {
            var wrapped = new CaptureSource { Id = name, Name = name, Type = CaptureSourceType.VideoDevice };
            _recordPage.SelectCaptureSource(wrapped);
        }
        else
        {
            _recordPage.SelectCaptureSource(new CaptureSource { Id = selected.ToString() ?? "", Name = selected.ToString() ?? "", Type = CaptureSourceType.Screen });
        }
    }
}
