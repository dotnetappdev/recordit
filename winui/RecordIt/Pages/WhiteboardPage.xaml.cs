using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Numerics;
using Windows.Foundation;
using Windows.UI;

namespace RecordIt.Pages;

public sealed partial class WhiteboardPage : Page
{
    private string _currentTool = "pen";
    private Color _currentColor = Color.FromArgb(255, 245, 245, 245);
    private float _brushSize = 3f;
    private int _zoomLevel = 100;
    private bool _isDrawing = false;
    private Point _lastPoint;
    private Point _startPoint;

    // Drawing storage
    private readonly List<DrawStroke> _strokes = new();
    private readonly List<DrawStroke> _redoStack = new();
    private DrawStroke? _currentStroke;

    public WhiteboardPage()
    {
        this.InitializeComponent();
        BrushSizeSlider.ValueChanged += BrushSizeSlider_ValueChanged;
        SetActiveTool("pen");
    }

    private void SetActiveTool(string tool)
    {
        _currentTool = tool;
        // Visual feedback - handled by style changes
    }

    private void ToolBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tool)
            SetActiveTool(tool);
    }

    private void ColorSwatch_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is Border border && border.Tag is string colorStr)
        {
            _currentColor = ParseColor(colorStr);
        }
    }

    private static Color ParseColor(string hex)
    {
        hex = hex.TrimStart('#');
        return Color.FromArgb(255,
            Convert.ToByte(hex[0..2], 16),
            Convert.ToByte(hex[2..4], 16),
            Convert.ToByte(hex[4..6], 16));
    }

    private void BrushSizeSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        _brushSize = (float)e.NewValue;
        if (BrushSizeLabel != null)
            BrushSizeLabel.Text = ((int)_brushSize).ToString();
    }

    private void Canvas_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _isDrawing = true;
        var point = e.GetCurrentPoint(WhiteboardCanvas);
        _lastPoint = point.Position;
        _startPoint = point.Position;
        WhiteboardCanvas.CapturePointer(e.Pointer);

        if (IsPathTool())
        {
            _currentStroke = new DrawStroke
            {
                Tool = _currentTool,
                Color = _currentColor,
                Size = _brushSize,
                Points = new List<Point> { point.Position }
            };
        }

        e.Handled = true;
    }

    private void Canvas_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_isDrawing) return;
        var point = e.GetCurrentPoint(WhiteboardCanvas);

        if (IsPathTool() && _currentStroke != null)
        {
            _currentStroke.Points.Add(point.Position);
            // Draw incrementally using Win2D or fallback polyline
            DrawIncrementalLine(_lastPoint, point.Position);
        }

        _lastPoint = point.Position;
        e.Handled = true;
    }

    private void Canvas_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_isDrawing) return;
        _isDrawing = false;
        WhiteboardCanvas.ReleasePointerCapture(e.Pointer);

        if (_currentStroke != null)
        {
            _strokes.Add(_currentStroke);
            _redoStack.Clear();
            _currentStroke = null;
        }
        else if (!IsPathTool())
        {
            // Shape finalize
            var endPoint = e.GetCurrentPoint(WhiteboardCanvas).Position;
            DrawFinalShape(_startPoint, endPoint);
        }

        e.Handled = true;
    }

    private bool IsPathTool() =>
        _currentTool is "pen" or "highlighter" or "eraser";

    private void DrawIncrementalLine(Point from, Point to)
    {
        // Draw a line segment on the canvas using polyline or Win2D
        var line = new Microsoft.UI.Xaml.Shapes.Line
        {
            X1 = from.X, Y1 = from.Y,
            X2 = to.X, Y2 = to.Y,
            Stroke = new SolidColorBrush(_currentTool == "eraser" ? Colors.Transparent : _currentColor),
            StrokeThickness = _currentTool == "eraser" ? _brushSize * 4 :
                              _currentTool == "highlighter" ? _brushSize * 4 : _brushSize,
            StrokeLineCap = PenLineCap.Round,
            Opacity = _currentTool == "highlighter" ? 0.4 : 1.0
        };
        WhiteboardCanvas.Children.Add(line);
    }

    private void DrawFinalShape(Point start, Point end)
    {
        UIElement? shape = null;
        var stroke = new SolidColorBrush(_currentColor);

        switch (_currentTool)
        {
            case "rect":
                var rect = new Microsoft.UI.Xaml.Shapes.Rectangle
                {
                    Width = Math.Abs(end.X - start.X),
                    Height = Math.Abs(end.Y - start.Y),
                    Stroke = stroke,
                    StrokeThickness = _brushSize,
                    Fill = new SolidColorBrush(Colors.Transparent)
                };
                Canvas.SetLeft(rect, Math.Min(start.X, end.X));
                Canvas.SetTop(rect, Math.Min(start.Y, end.Y));
                shape = rect;
                break;
            case "circle":
                var ellipse = new Microsoft.UI.Xaml.Shapes.Ellipse
                {
                    Width = Math.Abs(end.X - start.X),
                    Height = Math.Abs(end.Y - start.Y),
                    Stroke = stroke,
                    StrokeThickness = _brushSize,
                    Fill = new SolidColorBrush(Colors.Transparent)
                };
                Canvas.SetLeft(ellipse, Math.Min(start.X, end.X));
                Canvas.SetTop(ellipse, Math.Min(start.Y, end.Y));
                shape = ellipse;
                break;
            case "line":
                var line = new Microsoft.UI.Xaml.Shapes.Line
                {
                    X1 = start.X, Y1 = start.Y,
                    X2 = end.X, Y2 = end.Y,
                    Stroke = stroke,
                    StrokeThickness = _brushSize,
                    StrokeLineCap = PenLineCap.Round
                };
                shape = line;
                break;
        }

        if (shape != null) WhiteboardCanvas.Children.Add(shape);
    }

    private void UndoBtn_Click(object sender, RoutedEventArgs e)
    {
        if (WhiteboardCanvas.Children.Count > 0)
        {
            var last = WhiteboardCanvas.Children[^1];
            WhiteboardCanvas.Children.Remove(last);
        }
    }

    private void RedoBtn_Click(object sender, RoutedEventArgs e) { /* Redo logic */ }

    private void ClearBtn_Click(object sender, RoutedEventArgs e)
        => WhiteboardCanvas.Children.Clear();

    private async void SaveBtn_Click(object sender, RoutedEventArgs e)
    {
        var picker = new Windows.Storage.Pickers.FileSavePicker();
        var hwnd = WinRT.Interop.WindowNativeInterop.GetWindowHandle(App.MainWindow!);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.PicturesLibrary;
        picker.SuggestedFileName = $"Whiteboard-{DateTime.Now:yyyyMMdd-HHmmss}";
        picker.FileTypeChoices.Add("PNG Image", new[] { ".png" });

        var file = await picker.PickSaveFileAsync();
        if (file == null) return;

        // Save canvas as PNG using RenderTargetBitmap
        var renderBitmap = new Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap();
        await renderBitmap.RenderAsync(WhiteboardCanvas);
        var pixels = await renderBitmap.GetPixelsAsync();

        using var stream = await file.OpenAsync(Windows.Storage.FileAccessMode.ReadWrite);
        var encoder = await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(
            Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(
            Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8,
            Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied,
            (uint)renderBitmap.PixelWidth,
            (uint)renderBitmap.PixelHeight,
            96, 96, pixels.ToArray());
        await encoder.FlushAsync();
    }

    private void ParticipantsBtn_Click(object sender, RoutedEventArgs e)
        => ParticipantsPanel.Visibility = ParticipantsPanel.Visibility == Visibility.Visible
            ? Visibility.Collapsed : Visibility.Visible;

    private void ZoomInBtn_Click(object sender, RoutedEventArgs e)
    {
        _zoomLevel = Math.Min(_zoomLevel + 25, 400);
        ZoomLevelText.Text = $"{_zoomLevel}%";
    }

    private void ZoomOutBtn_Click(object sender, RoutedEventArgs e)
    {
        _zoomLevel = Math.Max(_zoomLevel - 25, 25);
        ZoomLevelText.Text = $"{_zoomLevel}%";
    }
}

public class DrawStroke
{
    public string Tool { get; set; } = "pen";
    public Color Color { get; set; }
    public float Size { get; set; }
    public List<Point> Points { get; set; } = new();
}
