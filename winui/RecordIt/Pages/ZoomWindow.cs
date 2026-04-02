using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Windows.Graphics.Imaging;
using WinRT.Interop;
using Windows.Foundation;

namespace RecordIt.Pages
{
    public class ZoomWindow : Window
    {
        private Image _img;
        private Canvas _overlay;
        private nint _targetHwnd;

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(nint hWnd, out RECT lpRect);
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint hWnd);
        [DllImport("user32.dll")] private static extern bool SetCursorPos(int X, int Y);
        [DllImport("user32.dll")] private static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);

        private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const uint MOUSEEVENTF_LEFTUP   = 0x0004;

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        public ZoomWindow(nint targetHwnd)
        {
            _targetHwnd = targetHwnd;
            // Window sizing will be handled by the host when activated
            this.Title = "Magnifier";

            var root = new Grid { Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 20, 20, 20)) };
            _img = new Image { Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
            _overlay = new Canvas { IsHitTestVisible = true };

            root.Children.Add(_img);
            root.Children.Add(_overlay);

            Content = root;

            // pointer events for drawing and click-forwarding
            _overlay.PointerPressed += Overlay_PointerPressed;
            _overlay.PointerMoved  += Overlay_PointerMoved;
            _overlay.PointerReleased += Overlay_PointerReleased;
        }

        public ImageSource ImageSource
        {
            get => _img.Source;
            set => _img.Source = value;
        }

        private Polyline? _currentStroke;
        private void Overlay_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            var pt = e.GetCurrentPoint(_overlay).Position;
            // begin drawing stroke
            _currentStroke = new Polyline { Stroke = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 0, 0)), StrokeThickness = 3 };
            _currentStroke.Points.Add(new Windows.Foundation.Point(pt.X, pt.Y));
            _overlay.Children.Add(_currentStroke);

            // Also forward a click to the underlying window at the mapped coordinate
            ForwardClickToTarget(pt);
        }

        private void Overlay_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (_currentStroke == null) return;
            var pt = e.GetCurrentPoint(_overlay).Position;
            _currentStroke.Points.Add(new Windows.Foundation.Point(pt.X, pt.Y));
        }

        private void Overlay_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            _currentStroke = null;
        }

        private void ForwardClickToTarget(Windows.Foundation.Point localPt)
        {
            try
            {
                if (!GetWindowRect(_targetHwnd, out var rect)) return;
                // Determine image rendered size
                var imgActual = _img.ActualWidth > 0 ? _img.ActualWidth : _overlay.ActualWidth;
                var imgActualH = _img.ActualHeight > 0 ? _img.ActualHeight : _overlay.ActualHeight;

                // compute relative position inside image
                double relX = localPt.X / Math.Max(1.0, imgActual);
                double relY = localPt.Y / Math.Max(1.0, imgActualH);

                int targetX = rect.Left + (int)(relX * (rect.Right - rect.Left));
                int targetY = rect.Top  + (int)(relY * (rect.Bottom - rect.Top));

                // bring target to foreground and synthesize click
                SetForegroundWindow(_targetHwnd);
                SetCursorPos(targetX, targetY);
                mouse_event(MOUSEEVENTF_LEFTDOWN, (uint)targetX, (uint)targetY, 0, UIntPtr.Zero);
                mouse_event(MOUSEEVENTF_LEFTUP,   (uint)targetX, (uint)targetY, 0, UIntPtr.Zero);
            }
            catch { }
        }
    }
}
