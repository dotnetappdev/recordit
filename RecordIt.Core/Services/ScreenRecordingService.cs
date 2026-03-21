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
}

/// <summary>
/// Screen recording service that uses `ffmpeg` as a reliable backend.
/// This implementation spawns an `ffmpeg` process to capture the desktop (gdigrab),
/// optional webcam (dshow) and optional microphone (dshow), then overlays webcam
/// and writes an MP4 file. `ffmpeg` must be installed and available on PATH.
/// </summary>
public class ScreenRecordingService
{
    private Process? _ffmpegProcess;
    private bool _isRecording;

    public Task<IEnumerable<CaptureSource>> GetCaptureSources()
    {
        // Minimal static list for now — can be extended to enumerate windows/monitors.
        var sources = new List<CaptureSource>
        {
            new CaptureSource { Id = "screen:primary", Name = "Entire Screen (Primary)" },
            new CaptureSource { Id = "screen:all",     Name = "All Screens" }
        };

        return Task.FromResult<IEnumerable<CaptureSource>>(sources);
    }

    public bool IsRecording => _isRecording;

    /// <summary>
    /// Starts recording using ffmpeg. Parameters:
    /// - sourceId: ignored currently (uses entire desktop)
    /// - outputPath: full path to output file
    /// - resolution: e.g. "1920x1080"
    /// - fps: frames per second
    /// - includeMic: include microphone audio
    /// - includeWebcam: include webcam overlay
    /// - webcamDevice: optional dshow webcam device name
    /// - audioDevice: optional dshow audio device name
    /// </summary>
    public Task StartRecording(string sourceId, string outputPath, string resolution, int fps, bool includeMic,
        bool includeWebcam = false, string? webcamDevice = null, string? audioDevice = null)
    {
        if (_isRecording) return Task.CompletedTask;

        // Ensure output directory exists
        var outDir = Path.GetDirectoryName(outputPath) ?? Environment.CurrentDirectory;
        Directory.CreateDirectory(outDir);

        // Build ffmpeg arguments
        // Screen capture (gdigrab) - captures the primary desktop
        var args = $"-y -f gdigrab -framerate {fps} -i desktop";

        // Webcam input
        if (includeWebcam)
        {
            // Use provided device or default dshow video device
            var cam = webcamDevice != null ? webcamDevice : "video=Integrated Camera";
            args += $" -f dshow -i \"{cam}\"";
        }

        // Audio input
        if (includeMic)
        {
            var aud = audioDevice != null ? audioDevice : "audio=Microphone";
            args += $" -f dshow -i \"{aud}\"";
        }

        // Filter and mapping
        var filters = new List<string>();
        var mapArgs = new List<string>();

        // Video inputs mapping: 0 is desktop, 1 may be webcam
        if (includeWebcam)
        {
            // scale webcam to 320x180 by default and overlay bottom-right with 10px margin
            filters.Add("[1:v] scale=320:180 [cam]; [0:v][cam] overlay=main_w-overlay_w-10:main_h-overlay_h-10 [vout]");
            mapArgs.Add("-map [vout]");
        }
        else
        {
            mapArgs.Add("-map 0:v");
        }

        // Audio mapping
        if (includeMic)
        {
            // If webcam also provides audio, more advanced mapping would be needed.
            mapArgs.Add("-map 2:a? -c:a aac -b:a 128k");
        }

        // Video codec + encoding settings
        var videoCodec = "-c:v libx264 -preset veryfast -crf 23";

        // Compose final args
        var filterArg = filters.Count > 0 ? $" -filter_complex \"{string.Join("; ", filters)}\"" : string.Empty;
        var mapArg = mapArgs.Count > 0 ? " " + string.Join(' ', mapArgs) : string.Empty;

        var finalArgs = args + filterArg + mapArg + $" {videoCodec} -r {fps} -pix_fmt yuv420p \"{outputPath}\"";

        // Start ffmpeg process
        var startInfo = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            Arguments = finalArgs,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            RedirectStandardInput = true,
            CreateNoWindow = true
        };

        _ffmpegProcess = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        _ffmpegProcess.OutputDataReceived += (s, e) => { /* can log if needed */ };
        _ffmpegProcess.ErrorDataReceived += (s, e) => { /* ffmpeg logs progress on stderr */ };
        _ffmpegProcess.Exited += (s, e) => { _isRecording = false; };

        try
        {
            _ffmpegProcess.Start();
            _ffmpegProcess.BeginErrorReadLine();
            _ffmpegProcess.BeginOutputReadLine();
            _isRecording = true;
        }
        catch (Exception ex)
        {
            _ffmpegProcess = null;
            _isRecording = false;
            throw new InvalidOperationException("Failed to start ffmpeg. Is ffmpeg installed and on PATH?", ex);
        }

        return Task.CompletedTask;
    }

    public Task StopRecording()
    {
        if (!_isRecording || _ffmpegProcess == null) return Task.CompletedTask;

        try
        {
            // Ask ffmpeg to finish gracefully by sending q to stdin if available, otherwise kill
            if (!_ffmpegProcess.HasExited)
            {
                try { _ffmpegProcess.StandardInput.WriteLine("q"); } catch { }
                // give it a moment to exit
                if (!_ffmpegProcess.WaitForExit(2000))
                    _ffmpegProcess.Kill(true);
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

    /// <summary>
    /// Helper to probe dshow devices via ffmpeg - returns raw device list lines.
    /// </summary>
    public async Task<IEnumerable<string>> ProbeDshowDevicesAsync()
    {
        var psi = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            Arguments = "-hide_banner -f dshow -list_devices true -i dummy",
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        var list = new List<string>();
        try
        {
            using var p = Process.Start(psi)!;
            if (p == null) return list;
            var stderr = await p.StandardError.ReadToEndAsync();
            p.WaitForExit(2000);

            // extract lines that contain "\"<name>\""
            var matches = Regex.Matches(stderr, "\"(?<name>[^\"]+)\"");
            foreach (Match m in matches)
            {
                var name = m.Groups["name"].Value;
                if (!string.IsNullOrWhiteSpace(name) && !list.Contains(name)) list.Add(name);
            }
        }
        catch { }

        return list;
    }
}
