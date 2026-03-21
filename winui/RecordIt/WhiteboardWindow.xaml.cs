using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using RecordIt.Pages;
using Windows.Graphics;
using WinRT.Interop;

namespace RecordIt;

/// <summary>
/// Detached whiteboard window — launched from the whiteboard toolbar Pop-Out button.
/// Hosts a full WhiteboardPage and is designed to be dragged to a second monitor.
/// Window title "RecordIt Whiteboard" is used by ffmpeg gdigrab as a capture source.
/// </summary>
public sealed partial class WhiteboardWindow : Window
{
    private AppWindow? _appWindow;

    public WhiteboardWindow()
    {
        InitializeComponent();
        SetupWindow();
        ContentFrame.Navigate(typeof(WhiteboardPage));
    }

    private void SetupWindow()
    {
        try
        {
            var hwnd   = WindowNativeInterop.GetWindowHandle(this);
            if (hwnd == System.IntPtr.Zero) return;

            var winId  = Win32Interop.GetWindowIdFromWindow(hwnd);
            _appWindow = AppWindow.GetFromWindowId(winId);
            if (_appWindow == null) return;

            // Dark title bar
            _appWindow.TitleBar.ExtendsContentIntoTitleBar      = false;
            _appWindow.TitleBar.ButtonBackgroundColor           = Colors.Transparent;
            _appWindow.TitleBar.ButtonInactiveBackgroundColor   = Colors.Transparent;
            _appWindow.TitleBar.ButtonForegroundColor           = Colors.White;

            // Reasonable default size — user can resize / move to other monitor
            _appWindow.Resize(new SizeInt32(1280, 800));
        }
        catch
        {
            _appWindow = null;
        }
    }
}
