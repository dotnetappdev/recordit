using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using RecordIt.Core.Services;

namespace RecordIt.Avalonia;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Apply persisted FFmpeg path before any service starts recording/probing
        ApplyFfmpegSetting();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void ApplyFfmpegSetting()
    {
        try
        {
            var path = new SettingsService().Get("ffmpeg_path");
            if (!string.IsNullOrWhiteSpace(path))
                FfmpegLocator.Executable = path;
        }
        catch { /* first run — settings DB not yet created; use default */ }
    }
}
