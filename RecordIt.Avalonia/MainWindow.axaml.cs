using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using RecordIt.Avalonia.Pages;

namespace RecordIt.Avalonia;

public partial class MainWindow : Window
{
    private readonly RecordPage     _recordPage     = new();
    private readonly WhiteboardPage _whiteboardPage = new();
    private readonly LibraryPage    _libraryPage    = new();
    private readonly SettingsPage   _settingsPage   = new();

    private DispatcherTimer? _recTimer;

    public MainWindow()
    {
        InitializeComponent();
        NavigateTo("record");
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
}
