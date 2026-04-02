using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace RecordIt.Core.Services;

// ── Win32 P/Invoke for monitor + window enumeration ──────────────────────────

internal static class Win32Capture
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    public struct MONITORINFOEX
    {
        public int  cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    public delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")] public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
    [DllImport("user32.dll")] public static extern int GetWindowTextLength(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern IntPtr GetShellWindow();

    public const uint MONITORINFOF_PRIMARY = 1;
}

public class CaptureSource
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Thumbnail { get; set; }
    public CaptureSourceType Type { get; set; } = CaptureSourceType.Screen;
}

public enum CaptureSourceType
{
    Screen,
    Window,
    AudioInput,
    AudioOutput,
    VideoDevice,
}

public class DshowDeviceList
{
    public List<string> VideoDevices { get; } = new();
    public List<string> AudioDevices { get; } = new();
}

/// <summary>
/// Screen recording service backed by ffmpeg.
/// Fixes: correct audio stream index mapping, proper dshow device separation.
/// </summary>
public class ScreenRecordingService
{
    private Process? _ffmpegProcess;
    private bool _isRecording;
    public bool IsRecording => _isRecording;

    // ── Source enumeration ────────────────────────────────────────────────

    /// <summary>
    /// Enumerates real monitors via Win32 EnumDisplayMonitors, then adds
    /// all visible top-level windows via EnumWindows, plus a "Custom Region" entry.
    /// </summary>
    public Task<IEnumerable<CaptureSource>> GetCaptureSources()
    {
        var sources = new List<CaptureSource>();

        // ── Monitors ─────────────────────────────────────────────────────
        int monitorIndex = 0;
        try
        {
            Win32Capture.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (hMon, _, ref _, _) =>
            {
                var info = new Win32Capture.MONITORINFOEX { cbSize = Marshal.SizeOf<Win32Capture.MONITORINFOEX>() };
                if (!Win32Capture.GetMonitorInfo(hMon, ref info)) return true;

                bool isPrimary = (info.dwFlags & Win32Capture.MONITORINFOF_PRIMARY) != 0;
                int  w  = info.rcMonitor.Right  - info.rcMonitor.Left;
                int  h  = info.rcMonitor.Bottom - info.rcMonitor.Top;
                var  id = isPrimary ? "screen:primary" : $"screen:monitor{monitorIndex}";
                var  name = isPrimary
                    ? $"Primary Display ({w}×{h})"
                    : $"Display {monitorIndex + 1} ({w}×{h})";

                sources.Add(new CaptureSource { Id = id, Name = name, Type = CaptureSourceType.Screen });
                monitorIndex++;
                return true;
            }, IntPtr.Zero);
        }
        catch { }

        // Fallback if EnumDisplayMonitors returned nothing
        if (monitorIndex == 0)
            sources.Add(new CaptureSource { Id = "screen:primary", Name = "Primary Display", Type = CaptureSourceType.Screen });

        sources.Add(new CaptureSource { Id = "screen:all",    Name = "All Displays",  Type = CaptureSourceType.Screen });
        sources.Add(new CaptureSource { Id = "screen:region", Name = "Custom Region", Type = CaptureSourceType.Screen });

        // ── Visible top-level windows ─────────────────────────────────────
        var shellWnd = Win32Capture.GetShellWindow();
        try
        {
            Win32Capture.EnumWindows((hWnd, _) =>
            {
                if (hWnd == shellWnd) return true;
                if (!Win32Capture.IsWindowVisible(hWnd)) return true;

                int len = Win32Capture.GetWindowTextLength(hWnd);
                if (len < 3) return true;   // skip untitled / single-char windows

                var sb = new StringBuilder(len + 1);
                Win32Capture.GetWindowText(hWnd, sb, sb.Capacity);
                var title = sb.ToString().Trim();
                if (string.IsNullOrEmpty(title)) return true;

                // Skip our own window
                if (title.StartsWith("RecordIt", StringComparison.OrdinalIgnoreCase)) return true;

                sources.Add(new CaptureSource
                {
                    Id   = $"title={title}",
                    Name = title,
                    Type = CaptureSourceType.Window,
                });
                return true;
            }, IntPtr.Zero);
        }
        catch { }

        return Task.FromResult<IEnumerable<CaptureSource>>(sources);
    }

    // ── Device probing ────────────────────────────────────────────────────

    /// <summary>
    /// Probes DirectShow devices and returns video and audio devices separately.
    /// Also injects a "Desktop Audio (Loopback)" virtual entry for system audio.
    /// </summary>
    public async Task<DshowDeviceList> ProbeDevicesAsync()
    {
        var result = new DshowDeviceList();
        // Always offer a loopback / desktop-audio option first
        result.AudioDevices.Add("Desktop Audio (Loopback)");

        var psi = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            Arguments = "-hide_banner -f dshow -list_devices true -i dummy",
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        try
        {
            using var p = Process.Start(psi)!;
            if (p == null) return result;
            var stderr = await p.StandardError.ReadToEndAsync();
            p.WaitForExit(3000);

            // Parse ffmpeg dshow output:
            //   "DirectShow video devices"
            //     "Device Name"
            //   "DirectShow audio devices"
            //     "Device Name"
            bool inVideoSection = false;
            bool inAudioSection = false;

            foreach (var line in stderr.Split('\n'))
            {
                if (line.Contains("DirectShow video devices", StringComparison.OrdinalIgnoreCase))
                {
                    inVideoSection = true;
                    inAudioSection = false;
                    continue;
                }
                if (line.Contains("DirectShow audio devices", StringComparison.OrdinalIgnoreCase))
                {
                    inVideoSection = false;
                    inAudioSection = true;
                    continue;
                }

                // Device lines look like:  [dshow @...]  "Device Name"
                var m = Regex.Match(line, "\"(?<name>[^\"]+)\"");
                if (!m.Success) continue;
                var name = m.Groups["name"].Value.Trim();
                if (string.IsNullOrWhiteSpace(name)) continue;

                // Skip alternator lines (ffmpeg prints the device name twice with @...)
                if (line.Contains("@device_", StringComparison.OrdinalIgnoreCase)) continue;

                if (inVideoSection && !result.VideoDevices.Contains(name))
                    result.VideoDevices.Add(name);
                else if (inAudioSection && !result.AudioDevices.Contains(name))
                    result.AudioDevices.Add(name);
            }
        }
        catch { }

        return result;
    }

    /// <summary>Legacy flat probe used by older callers.</summary>
    public async Task<IEnumerable<string>> ProbeDshowDevicesAsync()
    {
        var d = await ProbeDevicesAsync();
        var all = new List<string>();
        all.AddRange(d.VideoDevices);
        all.AddRange(d.AudioDevices);
        return all;
    }

    // ── Recording ─────────────────────────────────────────────────────────

    /// <summary>
    /// Starts recording with ffmpeg.
    /// Correctly handles audio stream index based on whether webcam is also captured.
    /// </summary>
    public Task StartRecording(
        string sourceId,
        string outputPath,
        string resolution,
        int fps,
        bool includeMic,
        bool includeWebcam    = false,
        string? webcamDevice  = null,
        string? audioDevice   = null,
        float micVolume       = 1.0f,
        float desktopVolume   = 1.0f,
        bool noiseSuppression = false)
    {
        if (_isRecording) return Task.CompletedTask;

        var outDir = Path.GetDirectoryName(outputPath) ?? Environment.CurrentDirectory;
        Directory.CreateDirectory(outDir);

        // ── Build input chain ────────────────────────────────────────────
        // Input 0: gdigrab — either full desktop or a specific window by title
        string gdigrabTarget = sourceId.StartsWith("title=", StringComparison.OrdinalIgnoreCase)
            ? $"title=\"{sourceId.Substring(6)}\""
            : "desktop";
        var inputArgs = $"-f gdigrab -framerate {fps} -i {gdigrabTarget}";
        int nextInput = 1;
        int webcamVideoInput = -1;
        int audioInputIdx = -1;

        if (includeWebcam)
        {
            var cam = string.IsNullOrWhiteSpace(webcamDevice)
                ? "video=Integrated Camera"
                : $"video={webcamDevice}";
            inputArgs += $" -f dshow -i \"{cam}\"";
            webcamVideoInput = nextInput++;
        }

        if (includeMic)
        {
            string aud;
            if (string.IsNullOrWhiteSpace(audioDevice) || audioDevice == "Default")
            {
                // Default mic via dshow
                aud = "audio=Microphone";
            }
            else if (audioDevice == "Desktop Audio (Loopback)")
            {
                // WASAPI loopback for system audio
                inputArgs += " -f wasapi -loopback";
                aud = "default";
            }
            else
            {
                aud = $"audio={audioDevice}";
            }

            if (audioDevice != "Desktop Audio (Loopback)")
                inputArgs += $" -f dshow -i \"{aud}\"";
            else
                inputArgs += $" -i \"{aud}\"";

            audioInputIdx = nextInput++;
        }

        // ── Build filter_complex + mapping ───────────────────────────────
        var filterParts = new List<string>();
        var finalArgs = $"-y {inputArgs}";

        if (includeWebcam && webcamVideoInput >= 0)
        {
            // Scale webcam and overlay bottom-right
            filterParts.Add(
                $"[{webcamVideoInput}:v] scale=320:180 [cam]; " +
                $"[0:v][cam] overlay=main_w-overlay_w-10:main_h-overlay_h-10 [vout]");
            finalArgs += $" -filter_complex \"{string.Join("; ", filterParts)}\" -map [vout]";
        }
        else
        {
            // Apply optional scaling if resolution differs from capture
            finalArgs += " -map 0:v";
        }

        // Audio mapping — index is correct now
        if (includeMic && audioInputIdx >= 0)
        {
            // Build audio filter chain: noise suppression + optional volume
            var audioFilters = new List<string>();
            if (noiseSuppression)
                audioFilters.Add("arnndn");          // AI real-time noise suppression
            if (Math.Abs(micVolume - 1.0f) > 0.01f)
                audioFilters.Add($"volume={micVolume:0.00}");
            if (audioFilters.Count > 0)
                finalArgs += $" -af \"{string.Join(",", audioFilters)}\"";

            finalArgs += $" -map {audioInputIdx}:a? -c:a aac -b:a 192k";
        }
        else
        {
            // No explicit audio — try to map any audio that gdigrab might carry (rare)
            finalArgs += " -an";
        }

        // ── Video codec (set in VideoEncoderSettings from the Encoder library) ─
        finalArgs += $" {VideoEncoderSettings.BuildVideoArgs(fps)}";

        // Add faststart flag for better MP4 compatibility and recovery
        finalArgs += " -movflags +faststart";

        // Output
        finalArgs += $" \"{outputPath}\"";

        // ── Launch ffmpeg ────────────────────────────────────────────────
        var startInfo = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            Arguments = finalArgs,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            RedirectStandardInput = true,
            CreateNoWindow = true,
        };

        _ffmpegProcess = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        _ffmpegProcess.Exited += (_, _) => { _isRecording = false; };

        try
        {
            _ffmpegProcess.Start();
            _ffmpegProcess.BeginErrorReadLine();
            _ffmpegProcess.BeginOutputReadLine();
            _isRecording = true;
        }
        catch (Exception ex)
        {
            _ffmpegProcess?.Dispose();
            _ffmpegProcess = null;
            _isRecording = false;
            throw new InvalidOperationException(
                "Failed to start ffmpeg. Ensure ffmpeg is installed and on PATH.\n" + ex.Message, ex);
        }

        return Task.CompletedTask;
    }

    public Task StopRecording()
    {
        if (!_isRecording || _ffmpegProcess == null) return Task.CompletedTask;

        try
        {
            if (!_ffmpegProcess.HasExited)
            {
                try 
                { 
                    // Flush any pending input and send quit command
                    _ffmpegProcess.StandardInput.Flush();
                    _ffmpegProcess.StandardInput.WriteLine("q");
                    _ffmpegProcess.StandardInput.Flush();
                } 
                catch { }
                
                // Give ffmpeg plenty of time to finalize the MP4 file (write moov atom)
                // Increased from 4 seconds to 15 seconds to prevent corruption
                if (!_ffmpegProcess.WaitForExit(15000))
                {
                    // If still not exited, try closing stdin
                    try { _ffmpegProcess.StandardInput.Close(); } catch { }
                    
                    // Wait another 5 seconds
                    if (!_ffmpegProcess.WaitForExit(5000))
                    {
                        // Last resort: forcefully terminate
                        _ffmpegProcess.Kill(entireProcessTree: true);
                    }
                }
            }
        }
        catch { }
        finally
        {
            _ffmpegProcess?.Dispose();
            _ffmpegProcess = null;
            _isRecording = false;
        }

        return Task.CompletedTask;
    }
}
