using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.AppLifecycle;
using RecordIt.Core.Services;
using System;

namespace RecordIt;

public partial class App : Application
{
    private Window? _mainWindow;

    public App()
    {
        this.InitializeComponent();
    }

    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        ApplyPersistedSettings();
        _mainWindow = new MainWindow();
        _mainWindow.Activate();
    }

    public static MainWindow? MainWindow => (Current as App)?._mainWindow as MainWindow;

    /// <summary>
    /// Restore user settings that must be active before any window opens:
    ///   • FFmpeg executable path (FfmpegLocator)
    ///   • Video encoder selection (VideoEncoderSettings) — only when hardware
    ///     encoding is enabled; otherwise the default libx264 is used.
    /// </summary>
    private static void ApplyPersistedSettings()
    {
        try
        {
            var db = new SettingsService();

            var ffmpegPath = db.Get("ffmpeg_path");
            if (!string.IsNullOrWhiteSpace(ffmpegPath))
                FfmpegLocator.Executable = ffmpegPath;

            if (db.Get("hardware_encoding") == "1")
            {
                // Encoder codec + args are stored by HardwareEncoderService via Settings
                // but we restore the codec name here so ScreenRecordingService uses it
                // immediately (the full ExtraArgs will be re-applied when SettingsPage loads).
                var codec = db.Get("video_encoder");
                if (!string.IsNullOrWhiteSpace(codec))
                    VideoEncoderSettings.Codec = codec;
            }
        }
        catch { /* first run — settings DB not yet created */ }
    }
}
