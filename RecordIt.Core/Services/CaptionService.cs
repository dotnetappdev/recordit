using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace RecordIt.Core.Services;

public class CaptionService
{
    /// <summary>
    /// Transcribes audio using an available backend.
    /// Prefer `whisper`/`whisper.cpp` on PATH (external). Returns transcript text.
    /// Falls back to empty string if no backend is available.
    /// </summary>
    public async Task<string> TranscribeAsync(string inputVideoPath)
    {
        // Extract audio to a temporary WAV
        var tmpAudio = Path.GetTempFileName() + ".wav";
        try
        {
            await RunFFmpegAsync($"-y -i \"{inputVideoPath}\" -vn -acodec pcm_s16le -ar 16000 -ac 1 \"{tmpAudio}\"");

            // Try whisper (python/whisper) or whisper.cpp (main)
            var whisperCmd = FindOnPath(new[] { "whisper", "main.exe", "whisper.exe", "whisper_cpp" });
            if (whisperCmd != null)
            {
                // If 'whisper' (python) is installed, call: whisper <file> --model small --output_format txt --output_dir tmpdir
                var isPythonWhisper = Path.GetFileName(whisperCmd).Equals("whisper", StringComparison.OrdinalIgnoreCase);
                if (isPythonWhisper)
                {
                    var outDir = Path.Combine(Path.GetTempPath(), "recordit_transcribe");
                    Directory.CreateDirectory(outDir);
                    await RunProcessAsync(whisperCmd, $"\"{tmpAudio}\" --model small --output_format txt --output_dir \"{outDir}\"");
                    var txt = Directory.EnumerateFiles(outDir, "*.txt").FirstOrDefault();
                    if (txt != null) return await File.ReadAllTextAsync(txt);
                }
                else
                {
                    // whisper.cpp or other native port might accept: main.exe -m model.bin -f file.wav -otxt
                    var outFile = tmpAudio + ".txt";
                    await RunProcessAsync(whisperCmd, $"-f \"{tmpAudio}\" -otxt -of \"{outFile}\"");
                    if (File.Exists(outFile)) return await File.ReadAllTextAsync(outFile);
                }
            }

            // No backend found: return empty
            return string.Empty;
        }
        finally
        {
            try { if (File.Exists(tmpAudio)) File.Delete(tmpAudio); } catch { }
        }
    }

    private static string? FindOnPath(string[] candidates)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var parts = path.Split(Path.PathSeparator);
        foreach (var c in candidates)
        {
            foreach (var p in parts)
            {
                try
                {
                    var full = Path.Combine(p, c);
                    if (File.Exists(full)) return full;
                }
                catch { }
            }
        }
        return null;
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
            CreateNoWindow = true
        };

        var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
        p.Exited += (_, __) => tcs.TrySetResult(p.ExitCode);
        p.Start();
        _ = p.StandardError.ReadToEndAsync();
        return tcs.Task;
    }

    private Task RunProcessAsync(string file, string args)
    {
        var tcs = new TaskCompletionSource<int>();
        var psi = new ProcessStartInfo
        {
            FileName = file,
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        };

        var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
        p.Exited += (_, __) => tcs.TrySetResult(p.ExitCode);
        p.Start();
        _ = p.StandardError.ReadToEndAsync();
        _ = p.StandardOutput.ReadToEndAsync();
        return tcs.Task;
    }
}
