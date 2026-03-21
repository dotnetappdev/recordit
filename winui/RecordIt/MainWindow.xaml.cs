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

    public bool IsRecording { get; private set; }

    public MainWindow()
    {
        this.InitializeComponent();
        SetupWindow();
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

    private void NavigateToRecord(object sender, RoutedEventArgs e)
    {
        ContentFrame.Navigate(typeof(RecordPage));
        UpdateSidebarState("record");
    }

    private void NavigateToWhiteboard(object sender, RoutedEventArgs e)
    {
        ContentFrame.Navigate(typeof(WhiteboardPage));
        UpdateSidebarState("whiteboard");
    }

    private void NavigateToLibrary(object sender, RoutedEventArgs e)
    {
        ContentFrame.Navigate(typeof(LibraryPage));
        UpdateSidebarState("library");
    }

    private void NavigateToSettings(object sender, RoutedEventArgs e)
    {
        ContentFrame.Navigate(typeof(SettingsPage));
        UpdateSidebarState("settings");
    }

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
}
