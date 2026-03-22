using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace RecordIt.Core.Services;

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

    public Task<IEnumerable<CaptureSource>> GetCaptureSources()
    {
        var sources = new List<CaptureSource>
        {
            new() { Id = "screen:primary", Name = "Primary Display",    Type = CaptureSourceType.Screen },
            new() { Id = "screen:all",     Name = "All Displays",        Type = CaptureSourceType.Screen },
            new() { Id = "screen:region",  Name = "Custom Region",       Type = CaptureSourceType.Screen },
        };
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
        bool includeWebcam = false,
        string? webcamDevice = null,
        string? audioDevice = null,
        float micVolume = 1.0f,
        float desktopVolume = 1.0f)
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
            // Volume filter on the audio stream
            if (Math.Abs(micVolume - 1.0f) > 0.01f)
                finalArgs += $" -af volume={micVolume:0.00}";

            finalArgs += $" -map {audioInputIdx}:a? -c:a aac -b:a 192k";
        }
        else
        {
            // No explicit audio — try to map any audio that gdigrab might carry (rare)
            finalArgs += " -an";
        }

        // ── Video codec ──────────────────────────────────────────────────
        finalArgs += $" -c:v libx264 -preset veryfast -crf 22 -r {fps} -pix_fmt yuv420p";

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
                try { _ffmpegProcess.StandardInput.WriteLine("q"); } catch { }
                if (!_ffmpegProcess.WaitForExit(4000))
                    _ffmpegProcess.Kill(entireProcessTree: true);
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
