using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace RecordIt;

/// <summary>
/// Monitor-level screen magnifier.
///
/// Creates an always-on-top, click-through, borderless overlay window that covers
/// the target monitor and uses the Windows Magnification API to magnify the area
/// around the mouse cursor in real-time.
///
/// The overlay is WS_EX_TRANSPARENT so ALL mouse / keyboard input passes through
/// to the windows behind it — buttons and UI elements remain fully clickable.
/// </summary>
public sealed class ScreenMagnifier : IDisposable
{
    // ─── Win32 / Magnification API ────────────────────────────────────────────

    private const string WC_MAGNIFIER = "Magnifier";

    private const int WS_CHILD    = 0x40000000;
    private const int WS_VISIBLE  = 0x10000000;
    private const int WS_POPUP    = unchecked((int)0x80000000);
    private const int WS_EX_TOPMOST      = 0x00000008;
    private const int WS_EX_LAYERED      = 0x00080000;
    private const int WS_EX_TRANSPARENT  = 0x00000020;
    private const int WS_EX_TOOLWINDOW   = 0x00000080;
    private const int WS_EX_NOACTIVATE   = 0x08000000;

    private const int SW_SHOW = 5;
    private const int SW_HIDE = 0;

    private const uint WM_DESTROY = 0x0002;
    private const uint WM_SIZE    = 0x0005;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct WNDCLASSEX
    {
        public uint      cbSize;
        public uint      style;
        public IntPtr    lpfnWndProc;
        public int       cbClsExtra;
        public int       cbWndExtra;
        public IntPtr    hInstance;
        public IntPtr    hIcon;
        public IntPtr    hCursor;
        public IntPtr    hbrBackground;
        public string?   lpszMenuName;
        public string?   lpszClassName;
        public IntPtr    hIconSm;
    }

    // Transformation matrix (3×3 float stored as 9 floats)
    [StructLayout(LayoutKind.Sequential)]
    private struct MAGTRANSFORM
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 9)]
        public float[] v;
    }

    [DllImport("magnification.dll", SetLastError = true)]
    private static extern bool MagInitialize();

    [DllImport("magnification.dll", SetLastError = true)]
    private static extern bool MagUninitialize();

    [DllImport("magnification.dll", SetLastError = true)]
    private static extern bool MagSetWindowSource(IntPtr hwnd, RECT rect);

    [DllImport("magnification.dll", SetLastError = true)]
    private static extern bool MagSetWindowTransform(IntPtr hwnd, ref MAGTRANSFORM pTransform);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassEx(ref WNDCLASSEX lpWndClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(
        int     dwExStyle,
        string  lpClassName,
        string  lpWindowName,
        int     dwStyle,
        int     x, int y, int nWidth, int nHeight,
        IntPtr  hWndParent,
        IntPtr  hMenu,
        IntPtr  hInstance,
        IntPtr  lpParam);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool UpdateWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDesktopWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    private const uint MONITOR_DEFAULTTONEAREST = 0x00000002;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFOEX
    {
        public uint   cbSize;
        public RECT   rcMonitor;
        public RECT   rcWork;
        public uint   dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    // ─── State ────────────────────────────────────────────────────────────────

    private IntPtr _hostHwnd    = IntPtr.Zero;
    private IntPtr _magHwnd     = IntPtr.Zero;
    private Timer? _timer;

    private double _factor      = 2.0;
    private bool   _disposed;
    private bool   _initialized;

    // Keep delegate alive to prevent GC
    private readonly WndProc _wndProcDelegate;
    private delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    // ─── Public API ───────────────────────────────────────────────────────────

    public double Factor
    {
        get => _factor;
        set => _factor = Math.Clamp(value, 1.25, 8.0);
    }

    public bool IsActive { get; private set; }

    public ScreenMagnifier()
    {
        _wndProcDelegate = HostWndProc;
    }

    /// <summary>Start the screen magnifier overlay on the monitor that contains the cursor.</summary>
    public bool Start(double factor = 2.0)
    {
        if (IsActive) return true;

        _factor = Math.Clamp(factor, 1.25, 8.0);

        if (!MagInitialize()) return false;
        _initialized = true;

        if (!CreateHostWindow()) { Cleanup(); return false; }
        if (!CreateMagnifierChild()) { Cleanup(); return false; }

        ShowWindow(_hostHwnd, SW_SHOW);
        UpdateWindow(_hostHwnd);
        IsActive = true;

        // Refresh at ~30 fps
        _timer = new Timer(_ => Update(), null, 0, 33);
        return true;
    }

    /// <summary>Stop the magnifier and remove the overlay.</summary>
    public void Stop()
    {
        if (!IsActive) return;
        _timer?.Dispose();
        _timer = null;
        Cleanup();
        IsActive = false;
    }

    // ─── Window creation ──────────────────────────────────────────────────────

    private bool CreateHostWindow()
    {
        var hInstance = GetModuleHandle(null);
        const string className = "RecordItMagHost";

        var wc = new WNDCLASSEX
        {
            cbSize        = (uint)Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc   = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate),
            hInstance     = hInstance,
            lpszClassName = className,
        };
        RegisterClassEx(ref wc); // ignore error if already registered

        // Determine size of the primary monitor (start there; Update() repositions each frame)
        GetCursorPos(out var pt);
        var monitor = MonitorFromPoint(pt, MONITOR_DEFAULTTONEAREST);
        var mi = new MONITORINFOEX { cbSize = (uint)Marshal.SizeOf<MONITORINFOEX>() };
        GetMonitorInfo(monitor, ref mi);
        var r = mi.rcMonitor;

        _hostHwnd = CreateWindowEx(
            WS_EX_TOPMOST | WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE,
            className,
            "ScreenMagnifier",
            WS_POPUP | WS_VISIBLE,
            r.Left, r.Top,
            r.Right - r.Left, r.Bottom - r.Top,
            IntPtr.Zero, IntPtr.Zero, hInstance, IntPtr.Zero);

        return _hostHwnd != IntPtr.Zero;
    }

    private bool CreateMagnifierChild()
    {
        // The magnifier child must fill the host
        RECT hostRect = GetClientRect(_hostHwnd);
        int w = hostRect.Right  - hostRect.Left;
        int h = hostRect.Bottom - hostRect.Top;

        _magHwnd = CreateWindowEx(
            0,
            WC_MAGNIFIER,
            "MagnifierControl",
            WS_CHILD | WS_VISIBLE,
            0, 0, w, h,
            _hostHwnd,
            IntPtr.Zero,
            GetModuleHandle(null),
            IntPtr.Zero);

        return _magHwnd != IntPtr.Zero;
    }

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);
    private static RECT GetClientRect(IntPtr hWnd) { GetClientRect(hWnd, out var r); return r; }

    // ─── Per-frame update ─────────────────────────────────────────────────────

    private void Update()
    {
        if (_hostHwnd == IntPtr.Zero || _magHwnd == IntPtr.Zero) return;

        GetCursorPos(out var pt);

        // Keep host on the monitor that currently contains the cursor
        var monitor = MonitorFromPoint(pt, MONITOR_DEFAULTTONEAREST);
        var mi = new MONITORINFOEX { cbSize = (uint)Marshal.SizeOf<MONITORINFOEX>() };
        GetMonitorInfo(monitor, ref mi);
        var mr = mi.rcMonitor;

        int mw = mr.Right  - mr.Left;
        int mh = mr.Bottom - mr.Top;

        // Move host to cover the right monitor
        MoveWindow(_hostHwnd, mr.Left, mr.Top, mw, mh, false);

        // Resize magnifier child to fill host
        MoveWindow(_magHwnd, 0, 0, mw, mh, false);

        // Compute the source rect (the region of the screen that will be magnified)
        // Center it on the cursor so the content under the cursor stays centered.
        double invScale = 1.0 / _factor;
        int srcW = (int)(mw * invScale);
        int srcH = (int)(mh * invScale);
        int srcX = Math.Clamp(pt.X - srcW / 2, mr.Left,  mr.Right  - srcW);
        int srcY = Math.Clamp(pt.Y - srcH / 2, mr.Top,   mr.Bottom - srcH);

        var srcRect = new RECT
        {
            Left   = srcX,
            Top    = srcY,
            Right  = srcX + srcW,
            Bottom = srcY + srcH,
        };

        MagSetWindowSource(_magHwnd, srcRect);

        var matrix = new MAGTRANSFORM { v = new float[9] };
        matrix.v[0] = (float)_factor;
        matrix.v[4] = (float)_factor;
        matrix.v[8] = 1.0f;
        MagSetWindowTransform(_magHwnd, ref matrix);
    }

    private IntPtr HostWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        => DefWindowProc(hWnd, msg, wParam, lParam);

    // ─── Cleanup ──────────────────────────────────────────────────────────────

    private void Cleanup()
    {
        if (_magHwnd != IntPtr.Zero)  { DestroyWindow(_magHwnd);  _magHwnd  = IntPtr.Zero; }
        if (_hostHwnd != IntPtr.Zero) { DestroyWindow(_hostHwnd); _hostHwnd = IntPtr.Zero; }
        if (_initialized) { MagUninitialize(); _initialized = false; }
    }

    public void Dispose()
    {
        if (_disposed) return;
        Stop();
        _disposed = true;
    }
}
