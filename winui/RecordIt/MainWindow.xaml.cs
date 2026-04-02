using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RecordIt.Pages;
using System;
using System.Threading;
using Windows.Graphics;
using WinRT.Interop;
using System.Runtime.InteropServices;

namespace RecordIt;

public sealed partial class MainWindow : Window
{
    private AppWindow? _appWindow;
    private IntPtr _hWnd = IntPtr.Zero;
    private Timer? _recordingTimer;
    private int _recordingSeconds;
    private bool _isDarkTheme = true;
    private double _zoomLevel = 1.0;

    public bool IsRecording { get; private set; }

    public MainWindow()
    {
        this.InitializeComponent();
        SetupWindow();
        SetupKeyboardShortcuts();
        ContentFrame.Navigate(typeof(RecordPage));
    }

    private void SetupWindow()
    {
        try
        {
            _hWnd = WindowNativeInterop.GetWindowHandle(this);
            if (_hWnd == IntPtr.Zero)
            {
                // Couldn't get native handle; skip AppWindow-specific setup.
                return;
            }

            var windowId = Win32Interop.GetWindowIdFromWindow(_hWnd);
            _appWindow = AppWindow.GetFromWindowId(windowId);

            if (_appWindow == null)
            {
                // AppWindow not available in this environment; bail out gracefully.
                return;
            }

            // Configure custom title bar
            _appWindow.TitleBar.ExtendsContentIntoTitleBar = true;
            _appWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
            _appWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
            _appWindow.TitleBar.ButtonForegroundColor = Colors.White;
            _appWindow.TitleBar.ButtonHoverBackgroundColor = Windows.UI.Color.FromArgb(255, 51, 51, 51);
            _appWindow.TitleBar.ButtonPressedBackgroundColor = Windows.UI.Color.FromArgb(255, 61, 61, 61);

            ExtendsContentIntoTitleBar = true;
            SetTitleBar(TitleBar);

            // Set initial size
            _appWindow.Resize(new SizeInt32(1280, 800));
            _appWindow.MoveAndResize(new RectInt32(
                (int)(Microsoft.UI.Windowing.DisplayArea.Primary.WorkArea.Width - 1280) / 2,
                (int)(Microsoft.UI.Windowing.DisplayArea.Primary.WorkArea.Height - 800) / 2,
                1280, 800
            ));
        }
        catch
        {
            // If anything goes wrong while obtaining native window or AppWindow, leave defaults.
            _appWindow = null;
        }
    }

    public void StartRecordingIndicator()
    {
        IsRecording = true;
        _recordingSeconds = 0;
        RecordingIndicator.Visibility = Visibility.Visible;

        _recordingTimer = new Timer(_ =>
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                _recordingSeconds++;
                var h = _recordingSeconds / 3600;
                var m = (_recordingSeconds % 3600) / 60;
                var s = _recordingSeconds % 60;
                RecordingTimeText.Text = h > 0
                    ? $"{h}:{m:D2}:{s:D2}"
                    : $"{m:D2}:{s:D2}";
            });
        }, null, 1000, 1000);
    }

    public void StopRecordingIndicator()
    {
        IsRecording = false;
        _recordingTimer?.Dispose();
        _recordingTimer = null;
        RecordingIndicator.Visibility = Visibility.Collapsed;
        RecordingTimeText.Text = "00:00";
        _recordingSeconds = 0;
    }

    /// <summary>Programmatic navigation used by child pages (e.g. RecordPage menu).</summary>
    public void NavigateTo(string page)
    {
        switch (page)
        {
            case "record":     ContentFrame.Navigate(typeof(RecordPage));     break;
            case "whiteboard": ContentFrame.Navigate(typeof(WhiteboardPage)); break;
            case "library":    ContentFrame.Navigate(typeof(LibraryPage));    break;
            case "settings":   ContentFrame.Navigate(typeof(SettingsPage));   break;
        }
        UpdateSidebarState(page);
    }

    private void NavigateToRecord(object sender, RoutedEventArgs e)    => NavigateTo("record");
    private void NavigateToWhiteboard(object sender, RoutedEventArgs e)=> NavigateTo("whiteboard");
    private void NavigateToLibrary(object sender, RoutedEventArgs e)   => NavigateTo("library");
    private void NavigateToSettings(object sender, RoutedEventArgs e)  => NavigateTo("settings");

    private void UpdateSidebarState(string page)
    {
        // Reset all
        foreach (var btn in new[] { RecordNavBtn, WhiteboardNavBtn, LibraryNavBtn, SettingsNavBtn })
        {
            if (btn?.Content is FontIcon fi)
                fi.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextTertiaryBrush"];
        }

        // Highlight active
        var activeBtn = page switch
        {
            "record" => RecordNavBtn,
            "whiteboard" => WhiteboardNavBtn,
            "library" => LibraryNavBtn,
            "settings" => SettingsNavBtn,
            _ => null
        };

        if (activeBtn?.Content is FontIcon activeFi)
            activeFi.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["BrandPrimaryBrush"];
    }

    private void ThemeToggleBtn_Click(object sender, RoutedEventArgs e)
    {
        _isDarkTheme = !_isDarkTheme;
        // Theme switching is handled at app level in production
    }

    private void MinimizeBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_appWindow != null)
        {
            _appWindow.Presenter.SetPresenter(AppWindowPresenterKind.Default);
            ((OverlappedPresenter)_appWindow.Presenter).Minimize();
            return;
        }

        // Fallback: use Win32 ShowWindow if we have a valid HWND
        if (_hWnd != IntPtr.Zero)
        {
            NativeMethods.ShowWindow(_hWnd, NativeMethods.SW_MINIMIZE);
        }
    }

    private void MaximizeBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_appWindow != null)
        {
            var presenter = (OverlappedPresenter)_appWindow.Presenter;
            if (presenter.State == OverlappedPresenterState.Maximized)
                presenter.Restore();
            else
                presenter.Maximize();
            return;
        }

        if (_hWnd != IntPtr.Zero)
        {
            // Toggle maximize/restore using Win32
            if (NativeMethods.IsZoomed(_hWnd))
                NativeMethods.ShowWindow(_hWnd, NativeMethods.SW_RESTORE);
            else
                NativeMethods.ShowWindow(_hWnd, NativeMethods.SW_MAXIMIZE);
        }
    }

    private void CloseBtn_Click(object sender, RoutedEventArgs e)
    {
        this.Close();
    }

    // ─── Zoom functionality (Ctrl+Plus/Minus) ────────────────────────────────

    private void SetupKeyboardShortcuts()
    {
        this.PreviewKeyDown += MainWindow_PreviewKeyDown;
    }

    private void MainWindow_PreviewKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        var ctrlPressed = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        
        if (ctrlPressed)
        {
            if (e.Key == Windows.System.VirtualKey.Add || e.Key == (Windows.System.VirtualKey)187) // Plus key (187 is = key which is + with shift)
            {
                ZoomIn();
                e.Handled = true;
            }
            else if (e.Key == Windows.System.VirtualKey.Subtract || e.Key == (Windows.System.VirtualKey)189) // Minus key
            {
                ZoomOut();
                e.Handled = true;
            }
            else if (e.Key == (Windows.System.VirtualKey)48) // 0 key - reset zoom
            {
                ResetZoom();
                e.Handled = true;
            }
        }
    }

    private void ZoomIn()
    {
        _zoomLevel = Math.Min(_zoomLevel + 0.1, 2.0); // Max 200%
        ApplyZoom();
    }

    private void ZoomOut()
    {
        _zoomLevel = Math.Max(_zoomLevel - 0.1, 0.5); // Min 50%
        ApplyZoom();
    }

    private void ResetZoom()
    {
        _zoomLevel = 1.0;
        ApplyZoom();
    }

    private void ApplyZoom()
    {
        // Apply scale transform to the main content frame
        if (ContentFrame != null)
        {
            ContentFrame.RenderTransform = new Microsoft.UI.Xaml.Media.ScaleTransform
            {
                ScaleX = _zoomLevel,
                ScaleY = _zoomLevel,
                CenterX = ContentFrame.ActualWidth / 2,
                CenterY = ContentFrame.ActualHeight / 2
            };
        }

        // Show brief zoom indicator (optional - could add a transient popup)
        System.Diagnostics.Debug.WriteLine($"Zoom level: {_zoomLevel:P0}");
    }
}
