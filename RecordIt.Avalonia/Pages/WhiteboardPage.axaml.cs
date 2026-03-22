using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace RecordIt.Avalonia.Pages;

public partial class WhiteboardPage : UserControl
{
    private string  _activeTool  = "pen";
    private Color   _activeColor = Color.Parse("#6366F1");
    private double  _brushSize   = 4;
    private double  _zoom        = 1.0;
    private bool    _isDrawing;
    private Point   _lastPoint;
    private Polyline? _currentLine;

    private readonly Stack<Control> _undoStack = new();
    private readonly Stack<Control> _redoStack = new();

    public WhiteboardPage()
    {
        InitializeComponent();
    }

    // ─── Drawing ─────────────────────────────────────────────────────────────

    private void Canvas_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _isDrawing = true;
        _lastPoint = e.GetPosition(DrawingCanvas);
        _redoStack.Clear();

        if (_activeTool == "pen" || _activeTool == "highlight")
        {
            _currentLine = new Polyline
            {
                Stroke          = new SolidColorBrush(_activeColor),
                StrokeThickness = _activeTool == "highlight" ? _brushSize * 3 : _brushSize,
                Opacity         = _activeTool == "highlight" ? 0.4 : 1.0,
                StrokeLineCap   = PenLineCap.Round,
                StrokeLineJoin  = PenLineJoin.Round,
            };
            _currentLine.Points.Add(_lastPoint);
            DrawingCanvas.Children.Add(_currentLine);
            _undoStack.Push(_currentLine);
        }
        else if (_activeTool == "eraser")
        {
            EraseAt(_lastPoint);
        }
    }

    private void Canvas_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isDrawing) return;
        var pt = e.GetPosition(DrawingCanvas);

        if (_activeTool is "pen" or "highlight" && _currentLine != null)
        {
            _currentLine.Points.Add(pt);
        }
        else if (_activeTool == "eraser")
        {
            EraseAt(pt);
        }
        _lastPoint = pt;
    }

    private void Canvas_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _isDrawing   = false;
        _currentLine = null;
    }

    private void EraseAt(Point pt)
    {
        const double eraserRadius = 16;
        var toRemove = new List<Control>();
        foreach (var child in DrawingCanvas.Children)
        {
            if (child is Polyline pl)
            {
                foreach (var p in pl.Points)
                {
                    if (Distance(p, pt) < eraserRadius)
                    {
                        toRemove.Add(child);
                        break;
                    }
                }
            }
        }
        foreach (var ctrl in toRemove)
            DrawingCanvas.Children.Remove(ctrl);
    }

    private static double Distance(Point a, Point b)
        => Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));

    // ─── Tool selection ──────────────────────────────────────────────────────

    private void ToolBtn_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tag)
            _activeTool = tag;
    }

    private void ColorSwatch_Click(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border b && b.Background is SolidColorBrush scb)
            _activeColor = scb.Color;
    }

    // ─── Undo / Redo ─────────────────────────────────────────────────────────

    private void UndoBtn_Click(object? sender, RoutedEventArgs e)
    {
        if (_undoStack.Count == 0) return;
        var ctrl = _undoStack.Pop();
        DrawingCanvas.Children.Remove(ctrl);
        _redoStack.Push(ctrl);
    }

    private void RedoBtn_Click(object? sender, RoutedEventArgs e)
    {
        if (_redoStack.Count == 0) return;
        var ctrl = _redoStack.Pop();
        DrawingCanvas.Children.Add(ctrl);
        _undoStack.Push(ctrl);
    }

    private void ClearBtn_Click(object? sender, RoutedEventArgs e)
    {
        DrawingCanvas.Children.Clear();
        _undoStack.Clear();
        _redoStack.Clear();
    }

    // ─── Zoom ─────────────────────────────────────────────────────────────────

    private void ZoomInBtn_Click(object? sender, RoutedEventArgs e)  => SetZoom(_zoom * 1.25);
    private void ZoomOutBtn_Click(object? sender, RoutedEventArgs e) => SetZoom(_zoom / 1.25);
    private void ZoomFitBtn_Click(object? sender, RoutedEventArgs e) => SetZoom(1.0);

    private void SetZoom(double z)
    {
        _zoom = Math.Clamp(z, 0.25, 4.0);
        DrawingCanvas.RenderTransform = new ScaleTransform(_zoom, _zoom);
        ZoomLabel.Text = $"{(int)(_zoom * 100)}%";
    }

    // ─── Save ─────────────────────────────────────────────────────────────────

    private async void SaveBtn_Click(object? sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            DefaultExtension = "png",
            InitialFileName  = $"Whiteboard_{DateTime.Now:yyyyMMdd_HHmmss}.png",
            Filters          = new() { new FileDialogFilter { Name = "PNG Image", Extensions = { "png" } } },
        };
        var path = await dialog.ShowAsync(VisualRoot as Window);
        if (path == null) return;

        // Render canvas to bitmap
        var pxSize = new PixelSize((int)DrawingCanvas.Bounds.Width, (int)DrawingCanvas.Bounds.Height);
        var bitmap = new RenderTargetBitmap(pxSize);
        bitmap.Render(DrawingCanvas);
        bitmap.Save(path);
    }
}
