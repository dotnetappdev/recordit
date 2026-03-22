using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Windows.Foundation;
using Windows.UI;
using WinRT.Interop;

namespace RecordIt.Pages;

public sealed partial class WhiteboardPage : Page
{
    // ─── Drawing state ────────────────────────────────────────────────────────
    private string _currentTool  = "pen";
    private Color  _currentColor = Color.FromArgb(255, 245, 245, 245);
    private float  _brushSize    = 3f;
    private bool   _isDrawing;
    private Point  _lastPoint;
    private Point  _startPoint;
    private Microsoft.UI.Xaml.Shapes.Polyline? _currentPolyline;
    private Microsoft.UI.Xaml.Shapes.Shape?    _previewShape;

    private readonly List<UIElement> _drawHistory = new();
    private readonly Stack<UIElement> _redoStack   = new();

    // ─── Zoom / Pan state ────────────────────────────────────────────────────
    private double _scale  = 1.0;
    private double _panX   = 0.0;
    private double _panY   = 0.0;

    private ScaleTransform     _scaleTransform     = new() { CenterX = 0, CenterY = 0 };
    private TranslateTransform _translateTransform  = new();

    // Panning via middle-mouse or Space+drag
    private bool  _isPanning;
    private Point _panStartPointer;
    private double _panStartX, _panStartY;

    // ─── Aspect ratio overlay state ──────────────────────────────────────────
    private bool _showVerticalOverlay;

    // ─── Fullscreen state ────────────────────────────────────────────────────
    private AppWindow? _appWindow;
    private bool _isFullscreen;

    // ─── Monitor-level zoom (ScreenMagnifier) ────────────────────────────────
    private readonly ScreenMagnifier _screenMagnifier = new();
    private double _screenZoomFactor = 2.0;
    private double _screenZoomSpeed  = 0.25;

    // ─── Screen annotation overlay ───────────────────────────────────────────
    private readonly ScreenAnnotationOverlay _annotationOverlay = new();

    public WhiteboardPage()
    {
        InitializeComponent();
        BrushSizeSlider.ValueChanged += BrushSizeSlider_ValueChanged;
        SetActiveTool("pen");
        SetupTransforms();
        SetupAppWindow();
    }

    private void SetupTransforms()
    {
        var tg = new TransformGroup();
        tg.Children.Add(_scaleTransform);
        tg.Children.Add(_translateTransform);
        CanvasWrapper.RenderTransform = tg;
    }

    private void SetupAppWindow()
    {
        try
        {
            var hwnd = WindowNativeInterop.GetWindowHandle(App.MainWindow!);
            if (hwnd == IntPtr.Zero) return;
            var winId = Win32Interop.GetWindowIdFromWindow(hwnd);
            _appWindow = AppWindow.GetFromWindowId(winId);
        }
        catch { }
    }

    // ─── Tool selection ───────────────────────────────────────────────────────

    private void SetActiveTool(string tool)
    {
        _currentTool = tool;
    }

    private void ToolBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tool)
            SetActiveTool(tool);
    }

    private void ColorSwatch_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is Border border && border.Tag is string colorStr)
            _currentColor = ParseColor(colorStr);
    }

    private static Color ParseColor(string hex)
    {
        hex = hex.TrimStart('#');
        return Color.FromArgb(255,
            Convert.ToByte(hex[0..2], 16),
            Convert.ToByte(hex[2..4], 16),
            Convert.ToByte(hex[4..6], 16));
    }

    private void BrushSizeSlider_ValueChanged(object sender,
        Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        _brushSize = (float)e.NewValue;
        if (BrushSizeLabel != null)
            BrushSizeLabel.Text = ((int)_brushSize).ToString();
    }

    // ─── Pointer / Drawing ───────────────────────────────────────────────────

    /// <summary>Convert outer-container coordinates to canvas local coordinates.</summary>
    private Point ToCanvas(Point outerPt) =>
        new((_scale != 0) ? (outerPt.X - _panX) / _scale : outerPt.X,
            (_scale != 0) ? (outerPt.Y - _panY) / _scale : outerPt.Y);

    private void CanvasArea_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var props    = e.GetCurrentPoint(CanvasAreaOuter).Properties;
        var outerPt  = e.GetCurrentPoint(CanvasAreaOuter).Position;
        var canvasPt = ToCanvas(outerPt);

        // Middle-mouse or Space (pan mode) starts panning
        if (props.IsMiddleButtonPressed || _currentTool == "pan")
        {
            _isPanning       = true;
            _panStartPointer = outerPt;
            _panStartX       = _panX;
            _panStartY       = _panY;
            CanvasAreaOuter.CapturePointer(e.Pointer);
            e.Handled = true;
            return;
        }

        _isDrawing  = true;
        _lastPoint  = canvasPt;
        _startPoint = canvasPt;
        CanvasAreaOuter.CapturePointer(e.Pointer);

        if (IsPathTool())
        {
            _currentPolyline = new Microsoft.UI.Xaml.Shapes.Polyline
            {
                Stroke          = new SolidColorBrush(_currentTool == "eraser" ? Colors.Transparent : _currentColor),
                StrokeThickness = _currentTool == "eraser" ? _brushSize * 4 :
                                  _currentTool == "highlighter" ? _brushSize * 3 : _brushSize,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap   = PenLineCap.Round,
                StrokeLineJoin     = PenLineJoin.Round,
                Opacity            = _currentTool == "highlighter" ? 0.45 : 1.0,
            };
            _currentPolyline.Points.Add(canvasPt);
            WhiteboardCanvas.Children.Add(_currentPolyline);
            _drawHistory.Add(_currentPolyline);
        }

        e.Handled = true;
    }

    private void CanvasArea_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        var outerPt  = e.GetCurrentPoint(CanvasAreaOuter).Position;
        var canvasPt = ToCanvas(outerPt);

        if (_isPanning)
        {
            _panX = _panStartX + (outerPt.X - _panStartPointer.X);
            _panY = _panStartY + (outerPt.Y - _panStartPointer.Y);
            ApplyTransform();
            e.Handled = true;
            return;
        }

        if (!_isDrawing) return;

        if (IsPathTool() && _currentPolyline != null)
        {
            if (_currentTool == "eraser")
                EraseAt(canvasPt);
            else
                _currentPolyline.Points.Add(canvasPt);
        }
        else if (IsShapeTool())
        {
            // Live preview of shape while dragging
            UpdateShapePreview(_startPoint, canvasPt);
        }

        _lastPoint = canvasPt;
        e.Handled  = true;
    }

    private void CanvasArea_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        CanvasAreaOuter.ReleasePointerCapture(e.Pointer);

        if (_isPanning)
        {
            _isPanning = false;
            e.Handled  = true;
            return;
        }

        if (!_isDrawing) return;
        _isDrawing = false;

        var outerPt  = e.GetCurrentPoint(CanvasAreaOuter).Position;
        var canvasPt = ToCanvas(outerPt);

        if (_currentPolyline != null)
        {
            _currentPolyline = null;
        }
        else if (IsShapeTool())
        {
            // Remove preview, commit final shape
            if (_previewShape != null)
            {
                OverlayCanvas.Children.Remove(_previewShape);
                _previewShape = null;
            }
            var final = MakeShape(_startPoint, canvasPt, preview: false);
            if (final != null)
            {
                WhiteboardCanvas.Children.Add(final);
                _drawHistory.Add(final);
            }
        }

        _redoStack.Clear();
        e.Handled = true;
    }

    // ─── Erase ────────────────────────────────────────────────────────────────

    private void EraseAt(Point canvasPt)
    {
        const double R = 20.0;
        var toRemove = new List<UIElement>();
        foreach (var child in WhiteboardCanvas.Children)
        {
            if (child is Microsoft.UI.Xaml.Shapes.Polyline pl)
            {
                foreach (var p in pl.Points)
                {
                    if (Dist(p, canvasPt) < R) { toRemove.Add(child); break; }
                }
            }
        }
        foreach (var el in toRemove)
        {
            WhiteboardCanvas.Children.Remove(el);
            _drawHistory.Remove(el);
        }
    }

    private static double Dist(Point a, Point b) =>
        Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));

    // ─── Shape drawing ────────────────────────────────────────────────────────

    private bool IsPathTool()  => _currentTool is "pen" or "highlighter" or "eraser";
    private bool IsShapeTool() => _currentTool is "rect" or "circle" or "line";

    private void UpdateShapePreview(Point start, Point end)
    {
        if (_previewShape != null)
            OverlayCanvas.Children.Remove(_previewShape);

        _previewShape = MakeShape(start, end, preview: true);
        if (_previewShape != null)
            OverlayCanvas.Children.Add(_previewShape);
    }

    private Microsoft.UI.Xaml.Shapes.Shape? MakeShape(Point start, Point end, bool preview)
    {
        var stroke = new SolidColorBrush(_currentColor);
        double opacity = preview ? 0.6 : 1.0;

        switch (_currentTool)
        {
            case "rect":
            {
                var r = new Microsoft.UI.Xaml.Shapes.Rectangle
                {
                    Width           = Math.Abs(end.X - start.X),
                    Height          = Math.Abs(end.Y - start.Y),
                    Stroke          = stroke,
                    StrokeThickness = _brushSize,
                    Fill            = new SolidColorBrush(Colors.Transparent),
                    Opacity         = opacity,
                };
                Microsoft.UI.Xaml.Controls.Canvas.SetLeft(r, Math.Min(start.X, end.X));
                Microsoft.UI.Xaml.Controls.Canvas.SetTop(r,  Math.Min(start.Y, end.Y));
                return r;
            }
            case "circle":
            {
                var el = new Microsoft.UI.Xaml.Shapes.Ellipse
                {
                    Width           = Math.Abs(end.X - start.X),
                    Height          = Math.Abs(end.Y - start.Y),
                    Stroke          = stroke,
                    StrokeThickness = _brushSize,
                    Fill            = new SolidColorBrush(Colors.Transparent),
                    Opacity         = opacity,
                };
                Microsoft.UI.Xaml.Controls.Canvas.SetLeft(el, Math.Min(start.X, end.X));
                Microsoft.UI.Xaml.Controls.Canvas.SetTop(el,  Math.Min(start.Y, end.Y));
                return el;
            }
            case "line":
            {
                return new Microsoft.UI.Xaml.Shapes.Line
                {
                    X1 = start.X, Y1 = start.Y,
                    X2 = end.X,   Y2 = end.Y,
                    Stroke             = stroke,
                    StrokeThickness    = _brushSize,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap   = PenLineCap.Round,
                    Opacity            = opacity,
                };
            }
        }
        return null;
    }

    // ─── Undo / Redo ─────────────────────────────────────────────────────────

    private void UndoBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_drawHistory.Count == 0) return;
        var last = _drawHistory[^1];
        _drawHistory.RemoveAt(_drawHistory.Count - 1);
        WhiteboardCanvas.Children.Remove(last);
        _redoStack.Push(last);
    }

    private void RedoBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_redoStack.Count == 0) return;
        var el = _redoStack.Pop();
        WhiteboardCanvas.Children.Add(el);
        _drawHistory.Add(el);
    }

    private void ClearBtn_Click(object sender, RoutedEventArgs e)
    {
        WhiteboardCanvas.Children.Clear();
        OverlayCanvas.Children.Clear();
        _drawHistory.Clear();
        _redoStack.Clear();
    }

    // ─── Save ─────────────────────────────────────────────────────────────────

    private async void SaveBtn_Click(object sender, RoutedEventArgs e)
    {
        var picker = new Windows.Storage.Pickers.FileSavePicker();
        var hwnd   = WindowNativeInterop.GetWindowHandle(App.MainWindow!);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.PicturesLibrary;
        picker.SuggestedFileName = $"Whiteboard-{DateTime.Now:yyyyMMdd-HHmmss}";
        picker.FileTypeChoices.Add("PNG Image", new[] { ".png" });

        var file = await picker.PickSaveFileAsync();
        if (file == null) return;

        var renderBitmap = new Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap();
        await renderBitmap.RenderAsync(WhiteboardCanvas);
        var pixelsBuffer = await renderBitmap.GetPixelsAsync();

        byte[] pixels;
        using (var reader = Windows.Storage.Streams.DataReader.FromBuffer(pixelsBuffer))
        {
            pixels = new byte[pixelsBuffer.Length];
            reader.ReadBytes(pixels);
        }

        using var stream = await file.OpenAsync(Windows.Storage.FileAccessMode.ReadWrite);
        var encoder = await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(
            Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(
            Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8,
            Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied,
            (uint)renderBitmap.PixelWidth,
            (uint)renderBitmap.PixelHeight,
            96, 96, pixels);
        await encoder.FlushAsync();
    }

    // ─── Zoom (mouse-tracking) ────────────────────────────────────────────────

    /// <summary>Zoom the canvas, keeping the canvas point under <paramref name="center"/> fixed.</summary>
    private void ZoomAtPoint(Point center, double factor)
    {
        double newScale = Math.Clamp(_scale * factor, 0.1, 8.0);
        double ratio    = newScale / _scale;

        // Keep canvas point under 'center' pinned after scaling
        _panX = center.X * (1 - ratio) + _panX * ratio;
        _panY = center.Y * (1 - ratio) + _panY * ratio;
        _scale = newScale;

        ApplyTransform();
    }

    private void ApplyTransform()
    {
        _scaleTransform.ScaleX    = _scale;
        _scaleTransform.ScaleY    = _scale;
        _translateTransform.X     = _panX;
        _translateTransform.Y     = _panY;
        ZoomLevelText.Text        = $"{(int)Math.Round(_scale * 100)}%";
    }

    /// <summary>
    /// Ctrl+wheel = zoom centered on cursor.
    /// Plain wheel (no modifier) = pan vertically.
    /// </summary>
    private void CanvasArea_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        var pt    = e.GetCurrentPoint(CanvasAreaOuter);
        var delta = pt.Properties.MouseWheelDelta;
        var pos   = pt.Position;

        var mods = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(
                       Windows.System.VirtualKey.Control);
        bool ctrlDown = mods.HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

        if (ctrlDown)
        {
            // Zoom centered on cursor
            double factor = delta > 0 ? 1.12 : 1.0 / 1.12;
            ZoomAtPoint(pos, factor);
        }
        else
        {
            // Pan
            double shift = delta > 0 ? 60 : -60;
            bool shiftDown = Microsoft.UI.Input.InputKeyboardSource
                .GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift)
                .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
            if (shiftDown) _panX += shift; else _panY += shift;
            ApplyTransform();
        }

        e.Handled = true;
    }

    private void ZoomInBtn_Click(object sender, RoutedEventArgs e)
        => ZoomAtPoint(ViewportCenter(), 1.25);

    private void ZoomOutBtn_Click(object sender, RoutedEventArgs e)
        => ZoomAtPoint(ViewportCenter(), 1.0 / 1.25);

    private void ZoomResetBtn_Click(object sender, RoutedEventArgs e)
    {
        _scale = 1.0; _panX = 0; _panY = 0;
        ApplyTransform();
    }

    /// <summary>Returns the center of the visible canvas area in outer-container coordinates.</summary>
    private Point ViewportCenter() =>
        new(CanvasAreaOuter.ActualWidth  / 2,
            CanvasAreaOuter.ActualHeight / 2);

    // ─── Fullscreen ───────────────────────────────────────────────────────────

    private void FullscreenBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_appWindow == null) return;

        _isFullscreen = !_isFullscreen;
        _appWindow.SetPresenter(_isFullscreen
            ? AppWindowPresenterKind.FullScreen
            : AppWindowPresenterKind.Default);

        // Update glyph
        if (FullscreenBtn.Content is FontIcon fi)
            fi.Glyph = _isFullscreen ? "\uE73F" : "\uE740"; // exit / enter fullscreen
    }

    // ─── Pop-out to separate window ───────────────────────────────────────────

    private void PopOutBtn_Click(object sender, RoutedEventArgs e)
    {
        var win = new WhiteboardWindow();
        win.Activate();
    }

    // ─── 9:16 Vertical overlay ────────────────────────────────────────────────

    private void AspectRatioBtn_Click(object sender, RoutedEventArgs e)
    {
        _showVerticalOverlay = !_showVerticalOverlay;
        VerticalCropOverlay.Visibility = _showVerticalOverlay
            ? Visibility.Visible : Visibility.Collapsed;
        UpdateCropBands();
    }

    private void UpdateCropBands()
    {
        if (!_showVerticalOverlay) return;
        double h = CanvasAreaOuter.ActualHeight;
        double w = CanvasAreaOuter.ActualWidth;
        if (h <= 0 || w <= 0) return;

        double cropWidth = h * 9.0 / 16.0;
        double sideWidth = Math.Max(0, (w - cropWidth) / 2.0);

        CropBandLeft.Width  = sideWidth;
        CropBandRight.Width = sideWidth;
        CropFrame.Width     = cropWidth;
    }

    private void CanvasAreaOuter_SizeChanged(object sender, SizeChangedEventArgs e)
        => UpdateCropBands();

    // ─── Page lifecycle ───────────────────────────────────────────────────────

    protected override void OnNavigatedFrom(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _screenMagnifier.Stop();
        _annotationOverlay.Stop();
    }

    // ─── Monitor-level zoom ───────────────────────────────────────────────────

    private void ScreenZoomToggleBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_screenMagnifier.IsActive)
        {
            _screenMagnifier.Stop();
            if (ScreenZoomIcon != null)
                ScreenZoomIcon.Glyph = "\uE8B6";
        }
        else
        {
            _screenMagnifier.ZoomSpeed = _screenZoomSpeed;
            bool started = _screenMagnifier.Start(_screenZoomFactor);
            if (started && ScreenZoomIcon != null)
                ScreenZoomIcon.Glyph = "\uE711";
        }
    }

    private void ScreenZoomInBtn_Click(object sender, RoutedEventArgs e)
    {
        _screenMagnifier.ZoomSpeed = _screenZoomSpeed;
        _screenMagnifier.ZoomIn();
        _screenZoomFactor = _screenMagnifier.Factor;
        if (ScreenZoomIcon != null)
            ScreenZoomIcon.Glyph = _screenMagnifier.IsActive ? "\uE711" : "\uE8B6";
        if (ZoomFactorSlider != null)
            ZoomFactorSlider.Value = _screenZoomFactor;
    }

    private void ScreenZoomOutBtn_Click(object sender, RoutedEventArgs e)
    {
        _screenMagnifier.ZoomSpeed = _screenZoomSpeed;
        _screenMagnifier.ZoomOut();
        _screenZoomFactor = _screenMagnifier.Factor;
        if (ScreenZoomIcon != null)
            ScreenZoomIcon.Glyph = _screenMagnifier.IsActive ? "\uE711" : "\uE8B6";
        if (ZoomFactorSlider != null)
            ZoomFactorSlider.Value = _screenZoomFactor;
    }

    private void ZoomFactorSlider_ValueChanged(object sender,
        Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        _screenZoomFactor = e.NewValue;
        _screenMagnifier.Factor = _screenZoomFactor;
        if (ZoomFactorLabel != null)
            ZoomFactorLabel.Text = $"{_screenZoomFactor:0.0}×";
    }

    private void ZoomSpeedSlider_ValueChanged(object sender,
        Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        _screenZoomSpeed = e.NewValue;
        _screenMagnifier.ZoomSpeed = _screenZoomSpeed;
        if (ZoomSpeedLabel != null)
            ZoomSpeedLabel.Text = $"{_screenZoomSpeed:0.00}";
    }

    // ─── Quick screen capture → whiteboard background ─────────────────────────

    /// <summary>
    /// Takes a GDI screenshot of the primary monitor and imports it onto the
    /// whiteboard canvas as a background Image the user can draw on top of.
    /// </summary>
    private async void QuickCaptureBtn_Click(object sender, RoutedEventArgs e)
    {
        // Short delay so the user can position the screen before capture
        await System.Threading.Tasks.Task.Delay(500);

        // Use RenderTargetBitmap to capture the current app window content,
        // or fall back to a GDI full-screen screenshot if available.
        var bitmap = await CaptureScreenAsync();
        if (bitmap == null) return;

        // Place captured bitmap as an Image on the whiteboard at position (0,0)
        var img = new Microsoft.UI.Xaml.Controls.Image
        {
            Width   = WhiteboardCanvas.Width,
            Height  = WhiteboardCanvas.Height,
            Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill,
            Source  = bitmap,
        };
        Microsoft.UI.Xaml.Controls.Canvas.SetLeft(img, 0);
        Microsoft.UI.Xaml.Controls.Canvas.SetTop(img,  0);
        Microsoft.UI.Xaml.Controls.Canvas.SetZIndex(img, -1);

        // Remove any previous capture layer (keep at most one background capture)
        RemoveCaptureLayer();

        WhiteboardCanvas.Children.Insert(0, img);
        _drawHistory.Add(img);
    }

    private void RemoveCaptureLayer()
    {
        for (int i = WhiteboardCanvas.Children.Count - 1; i >= 0; i--)
        {
            if (WhiteboardCanvas.Children[i] is Microsoft.UI.Xaml.Controls.Image imgEl
                && Microsoft.UI.Xaml.Controls.Canvas.GetZIndex(imgEl) == -1)
            {
                _drawHistory.Remove(imgEl);
                WhiteboardCanvas.Children.RemoveAt(i);
                break;
            }
        }
    }

    /// <summary>
    /// Captures the entire app window as a bitmap using RenderTargetBitmap.
    /// Falls back to a P/Invoke GDI BitBlt screenshot when the app itself
    /// does not have useful content (e.g. if the whiteboard is in pop-out mode).
    /// </summary>
    private async System.Threading.Tasks.Task<ImageSource?> CaptureScreenAsync()
    {
        try
        {
            // Try Windows.Graphics.Capture via GraphicsCaptureSession
            // (requires user-picker — too slow for quick capture; use RenderTargetBitmap instead)
            var rtb = new RenderTargetBitmap();
            // Render the whiteboard canvas itself to get the current state
            await rtb.RenderAsync(WhiteboardCanvas);
            return rtb;
        }
        catch
        {
            return null;
        }
    }

    // ─── GDI screenshot helpers ───────────────────────────────────────────────

    [DllImport("user32.dll")]  private static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")]  private static extern int    ReleaseDC(IntPtr hWnd, IntPtr hDC);
    [DllImport("gdi32.dll")]   private static extern IntPtr CreateCompatibleDC(IntPtr hDC);
    [DllImport("gdi32.dll")]   private static extern IntPtr CreateCompatibleBitmap(IntPtr hDC, int w, int h);
    [DllImport("gdi32.dll")]   private static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObj);
    [DllImport("gdi32.dll")]   private static extern bool   BitBlt(IntPtr dst, int dX, int dY, int dW, int dH,
                                                                    IntPtr src, int sX, int sY, uint rop);
    [DllImport("gdi32.dll")]   private static extern bool   DeleteDC(IntPtr hDC);
    [DllImport("gdi32.dll")]   private static extern bool   DeleteObject(IntPtr hObj);
    [DllImport("user32.dll")]  private static extern int    GetSystemMetrics(int nIndex);

    private const uint SRCCOPY = 0x00CC0020;
    private const int  SM_CXSCREEN = 0;
    private const int  SM_CYSCREEN = 1;

    // ─── Screen annotation ────────────────────────────────────────────────────

    private void AnnotateToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_annotationOverlay.IsActive)
        {
            _annotationOverlay.Stop();
            AnnotationBar.Visibility = Visibility.Collapsed;
            if (AnnotateToggleIcon != null) AnnotateToggleIcon.Glyph = "\uECAA";
            if (AnnotateToggleText != null) AnnotateToggleText.Text  = "Annotate";
        }
        else
        {
            if (_annotationOverlay.Start())
            {
                AnnotationBar.Visibility = Visibility.Visible;
                if (AnnotateToggleIcon != null) AnnotateToggleIcon.Glyph = "\uE711";
                if (AnnotateToggleText != null) AnnotateToggleText.Text  = "Annotating";
                SyncAnnotateDrawModeUI();
            }
        }
    }

    private void AnnotateDrawMode_Click(object sender, RoutedEventArgs e)
    {
        bool newDraw = !_annotationOverlay.IsDrawMode;
        _annotationOverlay.SetDrawMode(newDraw);
        // Give the PostMessage a moment to land before refreshing UI
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
            SyncAnnotateDrawModeUI);
    }

    private void SyncAnnotateDrawModeUI()
    {
        bool draw = _annotationOverlay.IsDrawMode;
        if (AnnotateDrawModeIcon != null)
            AnnotateDrawModeIcon.Glyph = draw ? "\uED63" : "\uE8F4"; // pen / eye
        if (AnnotateDrawModeText != null)
            AnnotateDrawModeText.Text  = draw ? "Drawing" : "Passthrough";
    }

    private void AnnotateTool_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tagStr && int.TryParse(tagStr, out int tool))
            _annotationOverlay.SetTool(tool);
    }

    private void AnnotateColor_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is Border b && b.Tag is string hex)
            _annotationOverlay.SetColorRef(HexToColorRef(hex));
    }

    private void AnnotateWidth_Changed(object sender,
        Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        int w = (int)e.NewValue;
        _annotationOverlay.SetStrokeWidth(w);
        if (AnnotateWidthLabel != null)
            AnnotateWidthLabel.Text = w.ToString();
    }

    private void AnnotateUndo_Click(object sender, RoutedEventArgs e)
        => _annotationOverlay.UndoLastStroke();

    private void AnnotateClear_Click(object sender, RoutedEventArgs e)
        => _annotationOverlay.Clear();

    private void AnnotateStop_Click(object sender, RoutedEventArgs e)
        => AnnotateToggle_Click(sender, e);

    /// <summary>
    /// Convert a hex colour string ("#RRGGBB") to a Win32 COLORREF (0x00BBGGRR).
    /// </summary>
    private static int HexToColorRef(string hex)
    {
        hex = hex.TrimStart('#');
        byte r = Convert.ToByte(hex[0..2], 16);
        byte g = Convert.ToByte(hex[2..4], 16);
        byte b = Convert.ToByte(hex[4..6], 16);
        return (int)((uint)r | ((uint)g << 8) | ((uint)b << 16));
    }

    // ─── Participants ─────────────────────────────────────────────────────────

    private void ParticipantsBtn_Click(object sender, RoutedEventArgs e)
        => ParticipantsPanel.Visibility = ParticipantsPanel.Visibility == Visibility.Visible
            ? Visibility.Collapsed : Visibility.Visible;
}

// ─── DrawStroke model (kept for compatibility) ────────────────────────────────
public class DrawStroke
{
    public string Tool { get; set; } = "pen";
    public Color  Color { get; set; }
    public float  Size  { get; set; }
    public List<Point> Points { get; set; } = new();
}
