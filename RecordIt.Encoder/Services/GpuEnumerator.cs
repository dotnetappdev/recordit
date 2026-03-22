using System.Diagnostics;
using RecordIt.Encoder.Models;

namespace RecordIt.Encoder.Services;

/// <summary>
/// Enumerates GPU adapters via WMI (Win32_VideoController).
/// Falls back gracefully to an empty list if WMIC is unavailable or times out.
/// </summary>
public static class GpuEnumerator
{
    public static async Task<IReadOnlyList<GpuInfo>> EnumerateAsync()
    {
        var gpus = new List<GpuInfo>();

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName               = "wmic",
                Arguments              = "path win32_VideoController get Name,AdapterCompatibility /format:csv",
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
                CreateNoWindow         = true,
            };

            using var p = Process.Start(psi);
            if (p == null) return gpus;

            var output = await p.StandardOutput.ReadToEndAsync();
            p.WaitForExit(5000);

            // CSV format — first non-empty line is the header, subsequent lines are data.
            // The column order can vary, so parse by header name.
            var lines = output.Split('\n',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            if (lines.Length < 2) return gpus;

            // Find header row (first line that contains "Name")
            int headerIdx = Array.FindIndex(lines, l => l.Contains("Name"));
            if (headerIdx < 0) return gpus;

            var headers = lines[headerIdx].Split(',');
            int nameIdx   = Array.IndexOf(headers, "Name");
            int compatIdx = Array.IndexOf(headers, "AdapterCompatibility");

            for (int i = headerIdx + 1; i < lines.Length; i++)
            {
                var cols = lines[i].Split(',');
                var maxIdx = Math.Max(nameIdx, compatIdx);
                if (cols.Length <= maxIdx) continue;

                var name   = nameIdx   >= 0 ? cols[nameIdx].Trim()   : "";
                var compat = compatIdx >= 0 ? cols[compatIdx].Trim() : "";

                if (string.IsNullOrWhiteSpace(name)) continue;

                var vendor = DetectVendor(compat, name);
                gpus.Add(new GpuInfo(name, vendor));
            }
        }
        catch { /* WMIC not available — return empty list */ }

        return gpus;
    }

    private static GpuVendor DetectVendor(string compat, string name)
    {
        // Try AdapterCompatibility string first (more reliable)
        if (Contains(compat, "NVIDIA"))                        return GpuVendor.Nvidia;
        if (Contains(compat, "AMD") || Contains(compat, "Advanced Micro")) return GpuVendor.Amd;
        if (Contains(compat, "Intel"))                         return GpuVendor.Intel;

        // Fall back to GPU name
        if (Contains(name, "NVIDIA") || Contains(name, "GeForce") ||
            Contains(name, "Quadro") || Contains(name, "Tesla"))   return GpuVendor.Nvidia;
        if (Contains(name, "AMD")    || Contains(name, "Radeon"))  return GpuVendor.Amd;
        if (Contains(name, "Intel")  || Contains(name, "UHD") ||
            Contains(name, "Iris"))                                 return GpuVendor.Intel;

        return GpuVendor.Unknown;
    }

    private static bool Contains(string haystack, string needle) =>
        haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);
}
