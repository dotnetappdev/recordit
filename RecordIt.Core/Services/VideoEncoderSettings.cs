namespace RecordIt.Core.Services;

/// <summary>
/// Runtime video encoder selection — set from the app layer when the user
/// picks an encoder in Settings, then read by <see cref="ScreenRecordingService"/>.
///
/// Defaults to <c>libx264</c> so the app is always functional without GPU support.
/// </summary>
public static class VideoEncoderSettings
{
    private static string _codec    = "libx264";
    private static string _extraArgs = "-preset veryfast -crf 22";

    public static string Codec
    {
        get => _codec;
        set => _codec = string.IsNullOrWhiteSpace(value) ? "libx264" : value.Trim();
    }

    public static string ExtraArgs
    {
        get => _extraArgs;
        set => _extraArgs = value ?? "-preset veryfast -crf 22";
    }

    /// <summary>Builds the complete -c:v … -r … -pix_fmt … argument string.</summary>
    public static string BuildVideoArgs(int fps)
    {
        // HEVC + NVENC benefits from 10-bit (p010le); everything else stays yuv420p
        var pix = Codec.Contains("hevc", StringComparison.OrdinalIgnoreCase) &&
                  Codec.Contains("nvenc", StringComparison.OrdinalIgnoreCase)
            ? "p010le"
            : "yuv420p";

        return $"-c:v {Codec} {ExtraArgs} -r {fps} -pix_fmt {pix}";
    }
}
