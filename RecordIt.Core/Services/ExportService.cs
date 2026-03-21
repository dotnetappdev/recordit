using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace RecordIt.Core.Services;

public class ExportService
{
    /// <summary>
    /// Progress callback: reports 0..100 for percent or -1 for indeterminate.
    /// </summary>
    public event Action<double>? ProgressChanged;

    /// <summary>
    /// Render common presets (TikTok vertical, YouTube, Twitch) using ffmpeg.
    /// Requires ffmpeg on PATH.
    /// Returns paths to generated files.
    /// </summary>
    public async Task<string[]> RenderPresetsAsync(string inputPath, string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        var outputs = new[]
        {
            Path.Combine(outputDir, Path.GetFileNameWithoutExtension(inputPath) + "_tiktok.mp4"), // 1080x1920
            Path.Combine(outputDir, Path.GetFileNameWithoutExtension(inputPath) + "_youtube.mp4"), // 1920x1080
            Path.Combine(outputDir, Path.GetFileNameWithoutExtension(inputPath) + "_twitch.mp4")  // 1280x720
        };

        // TikTok: vertical 1080x1920 (scale and crop/rotate if needed)
        await RunFFmpegAsync($"-y -i \"{inputPath}\" -vf \"scale=1080:1920:force_original_aspect_ratio=decrease,pad=1080:1920:(ow-iw)/2:(oh-ih)/2\" -c:v libx264 -preset medium -crf 23 -c:a copy \"{outputs[0]}\"");

        // YouTube: 1920x1080
        await RunFFmpegAsync($"-y -i \"{inputPath}\" -vf \"scale=1920:1080:force_original_aspect_ratio=decrease,pad=1920:1080:(ow-iw)/2:(oh-ih)/2\" -c:v libx264 -preset medium -crf 20 -c:a copy \"{outputs[1]}\"");

        // Twitch: 1280x720
        await RunFFmpegAsync($"-y -i \"{inputPath}\" -vf \"scale=1280:720:force_original_aspect_ratio=decrease,pad=1280:720:(ow-iw)/2:(oh-ih)/2\" -c:v libx264 -preset veryfast -crf 23 -c:a copy \"{outputs[2]}\"");

        return outputs;
    }

    /// <summary>
    /// Merge additional audio tracks (SFX, music) into the input video and write to outputPath.
    /// </summary>
    public Task MergeAudioTracksAsync(string inputVideoPath, string[] audioTracks, string outputPath)
    {
        if (audioTracks == null || audioTracks.Length == 0) throw new ArgumentException("No audio tracks provided", nameof(audioTracks));

        // Build ffmpeg inputs
        var inputs = $"-i \"{inputVideoPath}\"";
        for (int i = 0; i < audioTracks.Length; i++) inputs += $" -i \"{audioTracks[i]}\"";

        // Build amix filter
        var inputsCount = audioTracks.Length + 1; // original audio + provided tracks
        var amixInputs = string.Join(string.Empty, Enumerable.Range(0, inputsCount).Select(i => $"[{i}:a]"));
        var filter = $"-filter_complex \"{amixInputs}amix=inputs={inputsCount}:duration=longest:dropout_transition=2[aout]\" -map 0:v -map [aout] -c:v copy -c:a aac -b:a 192k";

        var args = $"-y {inputs} {filter} \"{outputPath}\"";
        return RunFFmpegAsync(args);
    }

    private Task RunFFmpegAsync(string args)
    {
        var tcs = new TaskCompletionSource<int>();
        var psi = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        };

        var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
        p.Exited += (_, __) => tcs.TrySetResult(p.ExitCode);
        p.Start();
        // Read streams but don't block; emit minimal progress when possible
        _ = Task.Run(async () =>
        {
            try
            {
                var outText = await p.StandardOutput.ReadToEndAsync();
                var errText = await p.StandardError.ReadToEndAsync();
            }
            catch { }
        });
        return tcs.Task;
    }

    private Task RunFFmpegWithProgressAsync(string args, double durationSeconds = 0)
    {
        var tcs = new TaskCompletionSource<int>();
        var psi = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            Arguments = args + " -progress pipe:1 -nostats",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
        p.Exited += (_, __) => tcs.TrySetResult(p.ExitCode);
        p.Start();

        _ = Task.Run(async () =>
        {
            try
            {
                while (!p.HasExited)
                {
                    var line = await p.StandardOutput.ReadLineAsync();
                    if (line == null) break;
                    // progress lines include out_time_ms=..., parse if available
                    if (line.StartsWith("out_time_ms="))
                    {
                        if (long.TryParse(line.Substring("out_time_ms=".Length), out var ms) && durationSeconds > 0)
                        {
                            var percent = (ms / 1000.0) / durationSeconds * 100.0;
                            ProgressChanged?.Invoke(Math.Max(0, Math.Min(100, percent)));
                        }
                        else
                        {
                            ProgressChanged?.Invoke(-1);
                        }
                    }
                }
            }
            catch { }
        });

        return tcs.Task;
    }
}
