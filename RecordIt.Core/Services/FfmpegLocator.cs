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

    /// <summary>
    /// Ensure ffmpeg is available. If not found and <paramref name="installIfMissing"/> is true,
    /// attempt to download and install a bundled ffmpeg.exe into the app directory.
    /// Returns true if ffmpeg is usable after the call.
    /// </summary>
    public static async Task<bool> EnsureAvailableAsync(bool installIfMissing = true)
    {
        // Try to run current executable with -version
        if (IsExecutableWorking(_executable))
            return true;

        // If the current setting points to a non-existent bundled file, try to detect defaults
        _executable = DetectDefault();
        if (IsExecutableWorking(_executable))
            return true;

        if (!installIfMissing)
            return false;

        // Attempt to download and install into app directory
        var dest = AppContext.BaseDirectory;
        var ok = await FfmpegInstaller.InstallBundledAsync(dest);
        if (ok)
        {
            _executable = DetectDefault();
            return IsExecutableWorking(_executable);
        }

        return false;
    }

    private static bool IsExecutableWorking(string exe)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(exe)) return false;
            if (exe.Equals("ffmpeg", StringComparison.OrdinalIgnoreCase))
            {
                // rely on PATH resolution
            }
            // Try starting ffmpeg -version
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = exe,
                Arguments = "-version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var p = System.Diagnostics.Process.Start(psi);
            if (p == null) return false;
            p.WaitForExit(3000);
            return p.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    // ─── helpers ─────────────────────────────────────────────────────────────

    private static string DetectDefault()
    {
        // Prefer a bundled ffmpeg.exe shipped alongside the app (by the installer)
        var bundled = Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe");
        return File.Exists(bundled) ? bundled : "ffmpeg";
    }
}
