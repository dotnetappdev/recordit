using System.Diagnostics;
using System.Text.RegularExpressions;

namespace RecordIt.Encoder.Services;

/// <summary>
/// Queries ffmpeg for the set of available video encoder codec names by parsing
/// the output of <c>ffmpeg -hide_banner -encoders</c>.
/// </summary>
public static class EncoderProber
{
    // Encoders we care about — hardware first, then software fallback
    private static readonly string[] HardwareTargets =
    [
        "h264_nvenc", "hevc_nvenc",
        "h264_amf",   "hevc_amf",
        "h264_qsv",   "hevc_qsv",
        "h264_videotoolbox", "hevc_videotoolbox",
    ];

    /// <summary>
    /// Returns the set of codec names that ffmpeg reports as available.
    /// Each entry matches what you'd pass to <c>-c:v</c>.
    /// </summary>
    public static async Task<HashSet<string>> GetAvailableCodecsAsync(string ffmpegExe)
    {
        var available = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName               = ffmpegExe,
                Arguments              = "-hide_banner -encoders",
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
                CreateNoWindow         = true,
            };

            using var p = Process.Start(psi);
            if (p == null) return available;

            var output = await p.StandardOutput.ReadToEndAsync();
            p.WaitForExit(5000);

            // Each line looks like:  " V..... codec_name   Description ..."
            foreach (var line in output.Split('\n'))
            {
                var m = Regex.Match(line.TrimStart(), @"^[VAS][\.\S]{5}\s+(\S+)");
                if (m.Success)
                    available.Add(m.Groups[1].Value);
            }
        }
        catch { /* ffmpeg not found — return empty set */ }

        return available;
    }

    /// <summary>
    /// Returns only the hardware codec names (subset of <see cref="HardwareTargets"/>)
    /// that are present in <paramref name="available"/>.
    /// </summary>
    public static IEnumerable<string> FilterHardwareCodecs(HashSet<string> available) =>
        HardwareTargets.Where(available.Contains);
}
