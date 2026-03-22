using System.IO;

namespace RecordIt.Core.Services;

/// <summary>
/// Single source of truth for the ffmpeg executable path used by all services.
///
/// Resolution order:
///   1. User-configured path stored in Settings (applied at app startup via
///      <c>FfmpegLocator.Executable = savedPath</c>).
///   2. <c>ffmpeg.exe</c> bundled next to the app binary (placed there by the
///      installer).
///   3. <c>"ffmpeg"</c> — resolved from the system PATH as a last resort.
/// </summary>
public static class FfmpegLocator
{
    private static string _executable;

    static FfmpegLocator()
    {
        _executable = DetectDefault();
    }

    /// <summary>
    /// The ffmpeg executable to invoke.
    /// Setting to <c>null</c> or whitespace resets to automatic detection
    /// (bundled binary → PATH).
    /// </summary>
    public static string Executable
    {
        get => _executable;
        set => _executable = string.IsNullOrWhiteSpace(value) ? DetectDefault() : value.Trim();
    }

    // ─── helpers ─────────────────────────────────────────────────────────────

    private static string DetectDefault()
    {
        // Prefer a bundled ffmpeg.exe shipped alongside the app (by the installer)
        var bundled = Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe");
        return File.Exists(bundled) ? bundled : "ffmpeg";
    }
}
