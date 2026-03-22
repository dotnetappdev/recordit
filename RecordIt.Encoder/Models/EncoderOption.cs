namespace RecordIt.Encoder.Models;

/// <summary>
/// A selectable encoder shown in the GPU / Encoder dropdown.
/// Carries all the information needed to build the -c:v ffmpeg arguments.
/// </summary>
public sealed record EncoderOption(
    /// <summary>Stable key stored in Settings (e.g. "h264_nvenc").</summary>
    string Id,

    /// <summary>FFmpeg codec name passed to -c:v.</summary>
    string FfmpegCodec,

    /// <summary>Human-readable label shown in the ComboBox.</summary>
    string DisplayName,

    /// <summary>GPU adapter name, or null for software encoders.</summary>
    string? GpuName,

    /// <summary>True for NVENC / AMF / QSV etc., false for libx264.</summary>
    bool IsHardware,

    /// <summary>
    /// Extra codec flags inserted after -c:v {FfmpegCodec}.
    /// Example: "-preset p4 -rc vbr -cq 22 -b:v 0"
    /// </summary>
    string ExtraArgs)
{
    public override string ToString() => DisplayName;

    /// <summary>Builds the complete -c:v … -r … -pix_fmt … argument string.</summary>
    public string BuildVideoArgs(int fps)
    {
        var pix = FfmpegCodec.Contains("hevc", StringComparison.OrdinalIgnoreCase) &&
                  FfmpegCodec.Contains("nvenc", StringComparison.OrdinalIgnoreCase)
            ? "p010le"   // HEVC NVENC benefits from 10-bit
            : "yuv420p";

        return $"-c:v {FfmpegCodec} {ExtraArgs} -r {fps} -pix_fmt {pix}";
    }

    // ── Well-known software fallback ────────────────────────────────────────
    public static readonly EncoderOption SoftwareFallback = new(
        "libx264", "libx264",
        "Software (libx264) — CPU",
        null, false,
        "-preset veryfast -crf 22");
}
