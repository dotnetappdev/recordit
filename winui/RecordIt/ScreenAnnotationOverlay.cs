using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace RecordIt;

/// <summary>
/// Always-on-top, full-monitor annotation overlay.
///
/// In <b>passthrough mode</b> (default) the window has WS_EX_TRANSPARENT so every
/// click, scroll and key press reaches the window behind it — buttons remain
/// fully usable.
///
/// In <b>draw mode</b> WS_EX_TRANSPARENT is removed and the overlay captures
/// mouse input for freehand pen strokes, arrows and highlight circles.
///
/// Annotations are painted with GDI on a near-black chroma-key background
/// (RGB 1,1,1) so that everything not explicitly drawn appears transparent.
/// </summary>
public sealed class ScreenAnnotationOverlay : IDisposable
{
    // ─── Tool IDs ────────────────────────────────────────────────────────────
    public const int ToolPen       = 0;
    public const int ToolArrow     = 1;
    public const int ToolCircle    = 2;
    public const int ToolRectangle = 3;

    // ─── Chroma-key background ───────────────────────────────────────────────
    // COLORREF RGB(1,1,1) — near-black; used as the transparent colour.
    private const int BG_COLORKEY = 0x00010101;

    // ─── Win32 structures ────────────────────────────────────────────────────
    private delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public uint   cbSize;
        public uint   style;
        public IntPtr lpfnWndProc;
        public int    cbClsExtra, cbWndExtra;
        public IntPtr hInstance, hIcon, hCursor, hbrBackground;
        public string? lpszMenuName;
        public string? lpszClassName;
        public IntPtr  hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct PAINTSTRUCT
    {
        public IntPtr hdc;
        [MarshalAs(UnmanagedType.Bool)] public bool fErase;
        public RECT rcPaint;
        [MarshalAs(UnmanagedType.Bool)] public bool fRestore;
        [MarshalAs(UnmanagedType.Bool)] public bool fIncUpdate;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] rgbReserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint   message;
        public IntPtr wParam, lParam;
        public uint   time;
        public int    ptX, ptY;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFOEX
    {
        public uint   cbSize;
        public RECT   rcMonitor, rcWork;
        public uint   dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
    }

    // ─── P/Invoke ─────────────────────────────────────────────────────────────
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassEx(ref WNDCLASSEX wc);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowEx(int exStyle, string cls, string title,
        int style, int x, int y, int w, int h,
        IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);
    [DllImport("user32.dll")] private static extern bool   DestroyWindow(IntPtr hw);
    [DllImport("user32.dll")] private static extern IntPtr DefWindowProc(IntPtr hw, uint m, IntPtr w, IntPtr l);
    [DllImport("kernel32.dll")] private static extern IntPtr GetModuleHandle(string? s);
    // Message loop
    [DllImport("user32.dll")] private static extern int    GetMessage(out MSG m, IntPtr hw, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool   TranslateMessage(ref MSG m);
    [DllImport("user32.dll")] private static extern IntPtr DispatchMessage(ref MSG m);
    [DllImport("user32.dll")] private static extern bool   PostMessage(IntPtr hw, uint msg, IntPtr wp, IntPtr lp);
    [DllImport("user32.dll")] private static extern void   PostQuitMessage(int code);
    // Layered / transparent
    [DllImport("user32.dll")] private static extern bool SetLayeredWindowAttributes(IntPtr hw, int cr, byte a, uint f);
    private const uint LWA_COLORKEY = 1;
    // Extended style
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hw, int idx);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hw, int idx, int val);
    private const int GWL_EXSTYLE = -20;
    // Painting
    [DllImport("user32.dll")] private static extern IntPtr BeginPaint(IntPtr hw, out PAINTSTRUCT ps);
    [DllImport("user32.dll")] private static extern bool   EndPaint(IntPtr hw, ref PAINTSTRUCT ps);
    [DllImport("user32.dll")] private static extern bool   InvalidateRect(IntPtr hw, IntPtr r, bool e);
    [DllImport("user32.dll")] private static extern bool   GetClientRect(IntPtr hw, out RECT r);
    [DllImport("user32.dll")] private static extern int    FillRect(IntPtr hdc, ref RECT r, IntPtr hbr);
    // GDI
    [DllImport("gdi32.dll")] private static extern IntPtr CreateSolidBrush(int col);
    [DllImport("gdi32.dll")] private static extern IntPtr CreatePen(int style, int width, int col);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);
    [DllImport("gdi32.dll")] private static extern bool   DeleteObject(IntPtr h);
    [DllImport("gdi32.dll")] private static extern bool   Polyline(IntPtr hdc, POINT[] pts, int n);
    [DllImport("gdi32.dll")] private static extern bool   MoveToEx(IntPtr hdc, int x, int y, IntPtr prev);
    [DllImport("gdi32.dll")] private static extern bool   LineTo(IntPtr hdc, int x, int y);
    [DllImport("gdi32.dll")] private static extern bool   Ellipse(IntPtr hdc, int l, int t, int r, int b);
    [DllImport("gdi32.dll")] private static extern bool   Rectangle(IntPtr hdc, int l, int t, int r, int b);
    [DllImport("gdi32.dll")] private static extern IntPtr GetStockObject(int obj);
    private const int PS_SOLID   = 0;
    private const int NULL_BRUSH = 5;
    // Mouse capture
    [DllImport("user32.dll")] private static extern IntPtr SetCapture(IntPtr hw);
    [DllImport("user32.dll")] private static extern bool   ReleaseCapture();
    // Monitor
    [DllImport("user32.dll")] private static extern bool   GetCursorPos(out POINT pt);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(POINT pt, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr hm, ref MONITORINFOEX mi);
    private const uint MONITOR_DEFAULTTONEAREST = 2;

    // Window styles / flags
    private const int WS_POPUP          = unchecked((int)0x80000000);
    private const int WS_VISIBLE        = 0x10000000;
    private const int WS_EX_TOPMOST     = 0x00000008;
    private const int WS_EX_LAYERED     = 0x00080000;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_TOOLWINDOW  = 0x00000080;
    private const int WS_EX_NOACTIVATE  = 0x08000000;

    // Win messages
    private const uint WM_DESTROY     = 0x0002;
    private const uint WM_PAINT       = 0x000F;
    private const uint WM_ERASEBKGND  = 0x0014;
    private const uint WM_LBUTTONDOWN = 0x0201;
    private const uint WM_MOUSEMOVE   = 0x0200;
    private const uint WM_LBUTTONUP   = 0x0202;

    // Custom inter-thread messages
    private const uint WM_APP_SET_TOOL   = 0x8001;
    private const uint WM_APP_SET_COLOR  = 0x8002;
    private const uint WM_APP_CLEAR      = 0x8003;
    private const uint WM_APP_DRAW_MODE  = 0x8004;
    private const uint WM_APP_STOP       = 0x8005;
    private const uint WM_APP_SET_WIDTH  = 0x8006;
    private const uint WM_APP_UNDO       = 0x8007;

    // ─── Stroke model ────────────────────────────────────────────────────────
    private sealed class Stroke
    {
        public int      Tool     { get; set; }
        public int      ColorRef { get; set; }
        public int      Width    { get; set; } = 3;
        public List<POINT> Points { get; } = new();
    }

    // ─── Internal state (all mutated on the message-loop thread) ─────────────
    private readonly List<Stroke> _strokes = new();
    private Stroke? _live;       // stroke currently being drawn
    private bool    _lmbDown;

    // These three are read by the WndProc and written via PostMessage
    private int  _currentTool     = ToolPen;
    private int  _currentColorRef = 0x0000FF; // Red
    private int  _strokeWidth     = 3;
    private bool _drawMode;

    private IntPtr _hwnd = IntPtr.Zero;
    private Thread? _thread;
    private bool    _disposed;
    private readonly ManualResetEventSlim _ready = new(false);
    private readonly WndProc _wndProcDelegate;

    // ─── Public API ───────────────────────────────────────────────────────────
    public bool IsActive   { get; private set; }
    public bool IsDrawMode => _drawMode;

    public ScreenAnnotationOverlay() { _wndProcDelegate = WindowProc; }

    public bool Start()
    {
        if (IsActive) return true;
        _thread = new Thread(RunLoop) { IsBackground = true, Name = "AnnotationOverlay" };
        _thread.Start();
        bool created = _ready.Wait(TimeSpan.FromSeconds(3));
        IsActive = created && _hwnd != IntPtr.Zero;
        if (IsActive) SetDrawMode(true); // enter draw mode immediately
        return IsActive;
    }

    public void Stop()
    {
        if (_hwnd != IntPtr.Zero)
            PostMessage(_hwnd, WM_APP_STOP, IntPtr.Zero, IntPtr.Zero);
        _thread?.Join(600);
        IsActive = false;
        _drawMode = false;
    }

    public void Clear()         => Send(WM_APP_CLEAR);
    public void UndoLastStroke()=> Send(WM_APP_UNDO);

    /// <param name="tool">Use ToolPen / ToolArrow / ToolCircle constants.</param>
    public void SetTool(int tool)      => Send(WM_APP_SET_TOOL,  tool);

    /// <param name="colorRef">Win32 COLORREF: 0x00BBGGRR (e.g. red = 0x000000FF).</param>
    public void SetColorRef(int colorRef) => Send(WM_APP_SET_COLOR, colorRef);

    public void SetStrokeWidth(int w)  => Send(WM_APP_SET_WIDTH, Math.Max(1, w));

    /// <summary>
    /// Toggle between draw mode (overlay captures mouse, click-through OFF) and
    /// passthrough mode (WS_EX_TRANSPARENT ON — all input reaches apps below).
    /// </summary>
    public void SetDrawMode(bool draw) => Send(WM_APP_DRAW_MODE, draw ? 1 : 0);

    private void Send(uint msg, int param = 0)
    {
        if (_hwnd != IntPtr.Zero)
            PostMessage(_hwnd, msg, (IntPtr)param, IntPtr.Zero);
    }

    // ─── Message-loop thread ──────────────────────────────────────────────────
    private void RunLoop()
    {
        if (!CreateOverlayWindow()) { _ready.Set(); return; }
        _ready.Set();

        int ret;
        while ((ret = GetMessage(out var msg, IntPtr.Zero, 0, 0)) != 0)
        {
            if (ret < 0) break;
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }

        if (_hwnd != IntPtr.Zero) { DestroyWindow(_hwnd); _hwnd = IntPtr.Zero; }
        IsActive  = false;
        _drawMode = false;
    }

    private bool CreateOverlayWindow()
    {
        var hInst = GetModuleHandle(null);
        const string cls = "RecordItAnnotationOverlay";

        var wc = new WNDCLASSEX
        {
            cbSize        = (uint)Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc   = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate),
            hInstance     = hInst,
            lpszClassName = cls,
        };
        RegisterClassEx(ref wc); // ignore if already registered

        // Position over the monitor that currently contains the cursor
        GetCursorPos(out var pt);
        var hMon = MonitorFromPoint(pt, MONITOR_DEFAULTTONEAREST);
        var mi   = new MONITORINFOEX { cbSize = (uint)Marshal.SizeOf<MONITORINFOEX>() };
        GetMonitorInfo(hMon, ref mi);
        var mr = mi.rcMonitor;

        // Start in passthrough mode (WS_EX_TRANSPARENT set)
        int exStyle = WS_EX_TOPMOST | WS_EX_LAYERED | WS_EX_TOOLWINDOW
                    | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT;

        _hwnd = CreateWindowEx(
            exStyle, cls, "RecordItAnnotation",
            WS_POPUP | WS_VISIBLE,
            mr.Left, mr.Top,
            mr.Right - mr.Left, mr.Bottom - mr.Top,
            IntPtr.Zero, IntPtr.Zero, hInst, IntPtr.Zero);

        if (_hwnd == IntPtr.Zero) return false;

        // Use near-black as transparent colour key
        SetLayeredWindowAttributes(_hwnd, BG_COLORKEY, 0, LWA_COLORKEY);
        return true;
    }

    // ─── Window procedure ─────────────────────────────────────────────────────
    private IntPtr WindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case WM_ERASEBKGND: return (IntPtr)1; // handled in WM_PAINT

            case WM_PAINT:
                Render(hWnd);
                return IntPtr.Zero;

            case WM_LBUTTONDOWN:
                OnDown(hWnd, lParam);
                return IntPtr.Zero;

            case WM_MOUSEMOVE:
                OnMove(hWnd, lParam);
                return IntPtr.Zero;

            case WM_LBUTTONUP:
                OnUp(hWnd, lParam);
                return IntPtr.Zero;

            case WM_APP_SET_TOOL:
                _currentTool = (int)wParam;
                return IntPtr.Zero;

            case WM_APP_SET_COLOR:
                _currentColorRef = (int)wParam;
                return IntPtr.Zero;

            case WM_APP_SET_WIDTH:
                _strokeWidth = Math.Max(1, (int)wParam);
                return IntPtr.Zero;

            case WM_APP_CLEAR:
                _strokes.Clear();
                _live = null;
                InvalidateRect(hWnd, IntPtr.Zero, true);
                return IntPtr.Zero;

            case WM_APP_UNDO:
                if (_strokes.Count > 0) _strokes.RemoveAt(_strokes.Count - 1);
                InvalidateRect(hWnd, IntPtr.Zero, true);
                return IntPtr.Zero;

            case WM_APP_DRAW_MODE:
                ApplyDrawMode(hWnd, (int)wParam != 0);
                return IntPtr.Zero;

            case WM_APP_STOP:
            case WM_DESTROY:
                PostQuitMessage(0);
                return IntPtr.Zero;
        }
        return DefWindowProc(hWnd, msg, wParam, lParam);
    }

    // ─── Draw-mode toggle ─────────────────────────────────────────────────────
    private void ApplyDrawMode(IntPtr hWnd, bool draw)
    {
        _drawMode = draw;
        var ex = GetWindowLong(hWnd, GWL_EXSTYLE);
        if (draw)
            ex &= ~WS_EX_TRANSPARENT;  // remove → overlay captures mouse
        else
            ex |= WS_EX_TRANSPARENT;   // add    → click-through
        SetWindowLong(hWnd, GWL_EXSTYLE, ex);
    }

    // ─── Mouse input ──────────────────────────────────────────────────────────
    private static (int x, int y) Unpack(IntPtr lp)
    {
        int v = lp.ToInt32();
        return ((short)(v & 0xFFFF), (short)((v >> 16) & 0xFFFF));
    }

    private void OnDown(IntPtr hWnd, IntPtr lParam)
    {
        if (!_drawMode) return;
        var (x, y) = Unpack(lParam);
        _lmbDown = true;
        _live = new Stroke
        {
            Tool     = _currentTool,
            ColorRef = _currentColorRef,
            Width    = _strokeWidth,
        };
        _live.Points.Add(new POINT { X = x, Y = y });
        SetCapture(hWnd);
    }

    private void OnMove(IntPtr hWnd, IntPtr lParam)
    {
        if (!_lmbDown || _live == null) return;
        var (x, y) = Unpack(lParam);
        var p = new POINT { X = x, Y = y };

        if (_live.Tool == ToolPen)
        {
            _live.Points.Add(p);
        }
        else
        {
            // Arrow and circle: keep only start + current end
            if (_live.Points.Count > 1) _live.Points[1] = p;
            else                        _live.Points.Add(p);
        }
        InvalidateRect(hWnd, IntPtr.Zero, false);
    }

    private void OnUp(IntPtr hWnd, IntPtr lParam)
    {
        if (!_lmbDown) return;
        _lmbDown = false;
        ReleaseCapture();

        if (_live != null && _live.Points.Count >= 2)
            _strokes.Add(_live);
        _live = null;
        InvalidateRect(hWnd, IntPtr.Zero, false);
    }

    // ─── Rendering ────────────────────────────────────────────────────────────
    private void Render(IntPtr hWnd)
    {
        var hdc = BeginPaint(hWnd, out var ps);

        // Fill entire client area with chroma key → appears transparent
        GetClientRect(hWnd, out var cr);
        var bgBrush = CreateSolidBrush(BG_COLORKEY);
        FillRect(hdc, ref cr, bgBrush);
        DeleteObject(bgBrush);

        foreach (var s in _strokes) DrawStroke(hdc, s);
        if (_live is { } ls && ls.Points.Count >= 2) DrawStroke(hdc, ls);

        EndPaint(hWnd, ref ps);
    }

    private void DrawStroke(IntPtr hdc, Stroke s)
    {
        if (s.Points.Count < 2) return;

        var pen    = CreatePen(PS_SOLID, s.Width, s.ColorRef);
        var oldPen = SelectObject(hdc, pen);
        var nullBr = GetStockObject(NULL_BRUSH);
        var oldBr  = SelectObject(hdc, nullBr);

        switch (s.Tool)
        {
            case ToolPen:
                Polyline(hdc, s.Points.ToArray(), s.Points.Count);
                break;

            case ToolArrow:
                DrawArrow(hdc, s.Points[0], s.Points[^1]);
                break;

            case ToolCircle:
            {
                var a = s.Points[0]; var b = s.Points[^1];
                Ellipse(hdc,
                    Math.Min(a.X, b.X), Math.Min(a.Y, b.Y),
                    Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
                break;
            }

            case ToolRectangle:
            {
                var a = s.Points[0]; var b = s.Points[^1];
                Rectangle(hdc,
                    Math.Min(a.X, b.X), Math.Min(a.Y, b.Y),
                    Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
                break;
            }
        }

        SelectObject(hdc, oldBr);
        SelectObject(hdc, oldPen);
        DeleteObject(pen);
    }

    private void DrawArrow(IntPtr hdc, POINT a, POINT b)
    {
        // Shaft
        MoveToEx(hdc, a.X, a.Y, IntPtr.Zero);
        LineTo(hdc, b.X, b.Y);

        // Arrowhead
        double dx = b.X - a.X, dy = b.Y - a.Y;
        double len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 1) return;

        double ndx = dx / len, ndy = dy / len; // unit direction
        double px  = -ndy,      py  =  ndx;    // perpendicular

        const double AL = 22, AW = 10; // arrowhead length and half-width

        int t1x = (int)(b.X - ndx * AL + px * AW);
        int t1y = (int)(b.Y - ndy * AL + py * AW);
        int t2x = (int)(b.X - ndx * AL - px * AW);
        int t2y = (int)(b.Y - ndy * AL - py * AW);

        MoveToEx(hdc, b.X, b.Y, IntPtr.Zero); LineTo(hdc, t1x, t1y);
        MoveToEx(hdc, b.X, b.Y, IntPtr.Zero); LineTo(hdc, t2x, t2y);
    }

    // ─── IDisposable ──────────────────────────────────────────────────────────
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }
}
