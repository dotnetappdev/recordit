using System.IO;

namespace RecordIt.Core.Services;

/// <summary>
/// Single source of truth for the ffmpeg executable path used by all services.
///
/// Resolution order:
///   1. User-configured path stored in Settings (applied at app startup via
///      <c>FfmpegLocator.Executable = savedPath</c>).
///   2. <c>ffmpeg.exe</c> bundled next to the app binary together with the
///      shared FFmpeg DLLs (avcodec-*.dll, avformat-*.dll, avutil-*.dll …)
///      that are placed there by the installer / CI build step.
///   3. <c>"ffmpeg"</c> — resolved from the system PATH as a last resort.
///
/// Shared-library DLL bundling:
///   The build downloads the GyanD shared-lib FFmpeg build which ships
///   avcodec-*.dll, avformat-*.dll, avutil-*.dll, avdevice-*.dll,
///   avfilter-*.dll, swscale-*.dll and swresample-*.dll alongside ffmpeg.exe.
///   All of these files are copied into tools/ffmpeg/ during CI and included
///   in every installer so that no separate FFmpeg installation is required.
/// </summary>
public static class FfmpegLocator
{
    /// <summary>
    /// Core DLL name patterns bundled alongside ffmpeg.exe.
    /// These are the shared-library DLLs from the GyanD full_build-shared release.
    /// </summary>
    public static readonly string[] BundledDllPatterns =
    [
        "avcodec-*.dll",
        "avformat-*.dll",
        "avutil-*.dll",
        "avdevice-*.dll",
        "avfilter-*.dll",
        "swscale-*.dll",
        "swresample-*.dll",
    ];

    private static string _executable;

    /// <summary>
    /// The ffplay executable to invoke for live preview.
    /// </summary>
    public static string FfplayExecutable
    {
        get
        {
            var dir = BundledDirectory;
            if (dir is null) return "ffplay";
            var p = Path.Combine(dir, "ffplay.exe");
            return File.Exists(p) ? p : "ffplay";
        }
    }

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
    /// Returns the directory that contains the bundled ffmpeg.exe (and its DLLs),
    /// or <c>null</c> if no bundled binary is found.
    /// </summary>
    public static string? BundledDirectory
    {
        get
        {
            var dir = AppContext.BaseDirectory;
            var exe = Path.Combine(dir, "ffmpeg.exe");
            return File.Exists(exe) ? dir : null;
        }
    }

    /// <summary>
    /// Returns the list of bundled DLL files that are present alongside the
    /// bundled ffmpeg.exe.  Returns an empty array when using a PATH-resolved ffmpeg.
    /// </summary>
    public static string[] GetBundledDlls()
    {
        var dir = BundledDirectory;
        if (dir is null) return [];

        var found = new System.Collections.Generic.List<string>();
        foreach (var pattern in BundledDllPatterns)
            found.AddRange(Directory.GetFiles(dir, pattern));

        return [.. found];
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
        // 1. Prefer a bundled ffmpeg.exe shipped alongside the app (by the installer).
        //    The bundled build also ships avcodec-*.dll, avformat-*.dll, avutil-*.dll
        //    etc. next to the exe so no system-wide FFmpeg install is needed.
        var bundled = Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe");
        if (File.Exists(bundled)) return bundled;

        // 2. Fall back to system PATH.
        return "ffmpeg";
    }
}
