using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;

namespace RecordIt;

public class DockedPreviewManager : IDisposable
{
    private Process? _proc;
    private IntPtr _childHwnd = IntPtr.Zero;
    private readonly IntPtr _parentHwnd;
    private readonly DispatcherQueueTimer _timer;

    public int Width { get; set; } = 420;
    public int Height { get; set; } = 720;
    public int Margin { get; set; } = 20;

    public DockedPreviewManager(IntPtr parentHwnd)
    {
        _parentHwnd = parentHwnd;
        _timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(250);
        _timer.Tick += (_, __) => RepositionChild();
    }

    public void StartPreview(string ffplayArgs)
    {
        if (_proc != null && !_proc.HasExited) return;
        var psi = new ProcessStartInfo
        {
            FileName = "ffplay",
            Arguments = ffplayArgs,
            UseShellExecute = false,
            CreateNoWindow = false
        };

        _proc = Process.Start(psi);
        _ = Task.Run(async () =>
        {
            // Wait for window
            for (int i = 0; i < 50; i++)
            {
                if (_proc == null || _proc.HasExited) break;
                _proc.Refresh();
                if (_proc.MainWindowHandle != IntPtr.Zero)
                {
                    _childHwnd = _proc.MainWindowHandle;
                    SetParent(_childHwnd, _parentHwnd);
                    // remove border/title
                    SetWindowLong(_childHwnd, GWL_STYLE, WS_VISIBLE);
                    RepositionChild();
                    _timer.Start();
                    break;
                }
                await Task.Delay(100);
            }
        });
    }

    public void StopPreview()
    {
        try
        {
            _timer.Stop();
            if (_proc != null && !_proc.HasExited)
            {
                _proc.Kill(true);
                _proc.Dispose();
            }
        }
        catch { }
        finally
        {
            _proc = null;
            _childHwnd = IntPtr.Zero;
        }
    }

    private void RepositionChild()
    {
        if (_childHwnd == IntPtr.Zero) return;
        if (!GetWindowRect(_parentHwnd, out var rect)) return;

        int x = rect.Right - Width - Margin;
        int y = rect.Top + Margin;
        SetWindowPos(_childHwnd, IntPtr.Zero, x, y, Width, Height, SWP_NOZORDER | SWP_SHOWWINDOW);
    }

    public void Dispose()
    {
        StopPreview();
    }

    #region Win32
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    private const int GWL_STYLE = -16;
    private const int WS_VISIBLE = 0x10000000; // keep visible style
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_SHOWWINDOW = 0x0040;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
    #endregion
}
