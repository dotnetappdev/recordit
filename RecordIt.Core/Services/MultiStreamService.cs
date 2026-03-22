using System.Diagnostics;
using RecordIt.Core.Models;

namespace RecordIt.Core.Services;

/// <summary>
/// Streams to one or more RTMP endpoints simultaneously by spawning one
/// FFmpeg process per platform.
///
/// Landscape platforms (Twitch, YouTube, Kick) can optionally be combined
/// into a single FFmpeg process using the tee muxer to save CPU.
/// Vertical platforms (TikTok, YouTube Shorts) always get their own process
/// because they need a separate video filter chain.
///
/// Dependencies: FFmpeg must be on PATH or the path provided via
/// <see cref="FfmpegLocator"/>.
/// </summary>
public class MultiStreamService : IDisposable
{
    private readonly FfmpegLocator _locator;
    private readonly StreamingDatabase _db;
    private readonly List<Process> _processes = [];
    private bool _disposed;

    public event EventHandler<StreamStatusEventArgs>? StatusChanged;

    public MultiStreamService(FfmpegLocator locator, StreamingDatabase db)
    {
        _locator = locator;
        _db = db;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public bool IsStreaming => _processes.Count > 0;

    public async Task StartAsync(StartStreamRequest request, CancellationToken ct = default)
    {
        if (IsStreaming)
            throw new InvalidOperationException("A stream is already active. Call StopAsync first.");

        var enabled = request.Platforms.Where(p => p.Enabled).ToList();
        if (enabled.Count == 0)
            throw new ArgumentException("At least one platform must be enabled.", nameof(request));

        // Resolve stream keys from DB for platforms that don't have one baked in.
        foreach (var p in enabled)
        {
            if (string.IsNullOrWhiteSpace(p.StreamKey))
                p.StreamKey = await _db.GetStreamKeyAsync(p.Id.ToString()) ?? string.Empty;
        }

        var landscape = enabled.Where(p => !p.IsVertical).ToList();
        var vertical  = enabled.Where(p => p.IsVertical).ToList();

        // Landscape: single process with tee muxer when >1 target, otherwise plain output.
        if (landscape.Count > 0)
        {
            var proc = BuildLandscapeProcess(landscape, request.Encoder, request.CaptureSourceId);
            StartProcess(proc, "landscape");
        }

        // Vertical: one process per platform (different crop dimensions possible in future).
        foreach (var p in vertical)
        {
            var proc = BuildVerticalProcess(p, request.Encoder, request.CaptureSourceId);
            StartProcess(proc, p.Id.ToString());
        }

        await Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        foreach (var proc in _processes)
        {
            try
            {
                if (!proc.HasExited)
                {
                    // Ask FFmpeg to stop gracefully first.
                    await proc.StandardInput.WriteAsync('q');
                    await Task.Delay(1000);
                    if (!proc.HasExited) proc.Kill(entireProcessTree: true);
                }
                proc.Dispose();
            }
            catch { /* best effort */ }
        }
        _processes.Clear();
        StatusChanged?.Invoke(this, new StreamStatusEventArgs("all", StreamStatus.Idle));
    }

    // ── Process builders ──────────────────────────────────────────────────────

    private Process BuildLandscapeProcess(
        List<StreamingPlatformConfig> platforms,
        StreamEncoderSettings enc,
        string? sourceId)
    {
        var inputArgs = BuildInputArgs(sourceId, enc.FrameRate);
        var encArgs   = BuildEncodeArgs(enc, isVertical: false);

        string outputArg;
        if (platforms.Count == 1)
        {
            outputArg = $"-f flv \"{platforms[0].FullRtmpUrl}\"";
        }
        else
        {
            // Use FFmpeg tee muxer so one encode feeds N destinations.
            var teeTargets = string.Join("|", platforms.Select(p => $"[f=flv]\"{p.FullRtmpUrl}\""));
            outputArg = $"-f tee -map 0:v -map 0:a \"{teeTargets}\"";
        }

        var args = $"{inputArgs} {encArgs} {outputArg}";
        return CreateProcess(args);
    }

    private Process BuildVerticalProcess(
        StreamingPlatformConfig platform,
        StreamEncoderSettings enc,
        string? sourceId)
    {
        var inputArgs = BuildInputArgs(sourceId, enc.FrameRate);
        var encArgs   = BuildEncodeArgs(enc, isVertical: true);
        var args = $"{inputArgs} {encArgs} -f flv \"{platform.FullRtmpUrl}\"";
        return CreateProcess(args);
    }

    // ── FFmpeg argument builders ──────────────────────────────────────────────

    private static string BuildInputArgs(string? sourceId, int fps)
    {
        if (OperatingSystem.IsWindows())
        {
            // Use GDI screen grabber on Windows.
            return $"-f gdigrab -framerate {fps} -draw_mouse 1 -i desktop " +
                   $"-f dshow -i audio=\"virtual-audio-capturer\"";
        }
        if (OperatingSystem.IsMacOS())
        {
            return $"-f avfoundation -framerate {fps} -i \"1:0\"";
        }
        // Linux – X11
        var display = Environment.GetEnvironmentVariable("DISPLAY") ?? ":0.0";
        return $"-f x11grab -framerate {fps} -i {display} " +
               $"-f pulse -i default";
    }

    private static string BuildEncodeArgs(StreamEncoderSettings enc, bool isVertical)
    {
        var vf = isVertical
            ? "-vf \"crop=ih*9/16:ih,scale=1080:1920\""
            : string.Empty;

        return $"{vf} " +
               $"-c:v libx264 -preset {enc.Preset} -tune zerolatency " +
               $"-b:v {enc.VideoBitrateKbps}k -maxrate {enc.VideoBitrateKbps}k " +
               $"-bufsize {enc.VideoBitrateKbps * 2}k " +
               $"-g {enc.FrameRate * 2} -keyint_min {enc.FrameRate} -pix_fmt yuv420p " +
               $"-c:a aac -b:a {enc.AudioBitrateKbps}k -ar 44100";
    }

    // ── Process lifecycle helpers ─────────────────────────────────────────────

    private Process CreateProcess(string args)
    {
        var ffmpegPath = _locator.FindFfmpegPath();
        return new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = "-hide_banner -loglevel warning " + args,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            },
            EnableRaisingEvents = true,
        };
    }

    private void StartProcess(Process proc, string tag)
    {
        proc.ErrorDataReceived += (_, e) =>
        {
            if (e.Data == null) return;
            // "frame=" in stderr indicates encoding is active
            if (e.Data.Contains("frame="))
                StatusChanged?.Invoke(this, new StreamStatusEventArgs(tag, StreamStatus.Live));
        };

        proc.Exited += (_, _) =>
        {
            StatusChanged?.Invoke(this, new StreamStatusEventArgs(tag, StreamStatus.Idle));
            _processes.Remove(proc);
        };

        proc.Start();
        proc.BeginErrorReadLine();
        _processes.Add(proc);
        StatusChanged?.Invoke(this, new StreamStatusEventArgs(tag, StreamStatus.Connecting));
    }

    // ── IDisposable ───────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var p in _processes)
        {
            try { if (!p.HasExited) p.Kill(entireProcessTree: true); p.Dispose(); }
            catch { /* best effort */ }
        }
        _processes.Clear();
    }
}

// ── Supporting types ──────────────────────────────────────────────────────────

public enum StreamStatus { Idle, Connecting, Live, Error }

public class StreamStatusEventArgs(string platformTag, StreamStatus status) : EventArgs
{
    public string PlatformTag { get; } = platformTag;
    public StreamStatus Status { get; } = status;
}
