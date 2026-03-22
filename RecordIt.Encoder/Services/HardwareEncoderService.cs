using RecordIt.Encoder.Models;

namespace RecordIt.Encoder.Services;

/// <summary>
/// Combines GPU enumeration (via <see cref="GpuEnumerator"/>) and ffmpeg encoder
/// probing (via <see cref="EncoderProber"/>) to produce the list of encoder
/// options shown in the Settings GPU / Encoder dropdown.
///
/// Hardware encoders that ffmpeg cannot use are silently omitted.
/// The software (libx264) fallback is always appended last.
/// </summary>
public sealed class HardwareEncoderService
{
    private readonly string _ffmpegExe;

    public HardwareEncoderService(string ffmpegExe) => _ffmpegExe = ffmpegExe;

    // ── Codec → (vendor, label, codec-family, extra ffmpeg args) ────────────

    private static readonly (string Codec, GpuVendor Vendor, string VendorLabel, string CodecLabel, string ExtraArgs)[]
    KnownHardwareEncoders =
    [
        ("h264_nvenc", GpuVendor.Nvidia, "NVIDIA NVENC", "H.264",
         "-preset p4 -rc vbr -cq 22 -b:v 0"),
        ("hevc_nvenc", GpuVendor.Nvidia, "NVIDIA NVENC", "H.265 (HEVC)",
         "-preset p4 -rc vbr -cq 24 -b:v 0"),
        ("h264_amf",   GpuVendor.Amd,    "AMD AMF",      "H.264",
         "-quality speed -rc 1 -qp_i 22 -qp_p 22"),
        ("hevc_amf",   GpuVendor.Amd,    "AMD AMF",      "H.265 (HEVC)",
         "-quality speed -rc 1 -qp_i 24 -qp_p 24"),
        ("h264_qsv",   GpuVendor.Intel,  "Intel QSV",    "H.264",
         "-preset veryfast -global_quality 22"),
        ("hevc_qsv",   GpuVendor.Intel,  "Intel QSV",    "H.265 (HEVC)",
         "-preset veryfast -global_quality 24"),
        ("h264_videotoolbox", GpuVendor.Unknown, "Apple VideoToolbox", "H.264",
         "-allow_sw 1"),
        ("hevc_videotoolbox", GpuVendor.Unknown, "Apple VideoToolbox", "H.265 (HEVC)",
         "-allow_sw 1"),
    ];

    // ── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Probes available encoders and GPUs concurrently and returns the full
    /// list of selectable options, hardware-first then software fallback.
    /// </summary>
    public async Task<IReadOnlyList<EncoderOption>> GetEncoderOptionsAsync()
    {
        // Run both probes in parallel
        var codecsTask = EncoderProber.GetAvailableCodecsAsync(_ffmpegExe);
        var gpusTask   = GpuEnumerator.EnumerateAsync();
        await Task.WhenAll(codecsTask, gpusTask);

        var codecs  = codecsTask.Result;
        var gpus    = gpusTask.Result;
        var options = new List<EncoderOption>();

        foreach (var (codec, vendor, vendorLabel, codecLabel, extraArgs) in KnownHardwareEncoders)
        {
            if (!codecs.Contains(codec)) continue;

            var gpu     = gpus.FirstOrDefault(g => g.Vendor == vendor || vendor == GpuVendor.Unknown);
            var gpuName = gpu?.Name;
            var display = gpuName is not null
                ? $"{vendorLabel} ({codecLabel}) — {gpuName}"
                : $"{vendorLabel} ({codecLabel})";

            options.Add(new EncoderOption(codec, codec, display, gpuName, true, extraArgs));
        }

        // Software fallback is always available
        options.Add(EncoderOption.SoftwareFallback);
        return options;
    }

    /// <summary>
    /// Quick summary string for display: e.g. "3 hardware, 1 software encoder(s) found".
    /// </summary>
    public static string Summarise(IReadOnlyList<EncoderOption> options)
    {
        int hw = options.Count(o => o.IsHardware);
        int sw = options.Count(o => !o.IsHardware);
        return hw > 0
            ? $"{hw} hardware + {sw} software encoder(s) detected"
            : "No hardware encoders detected — software (libx264) only";
    }
}
