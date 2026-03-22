using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using RecordIt.Core.Services;

namespace RecordIt.Avalonia.Pages;

public partial class SettingsPage : UserControl
{
    private readonly SettingsService _settings = new();

    public SettingsPage()
    {
        InitializeComponent();

        // Output path default
        OutputPathBox.Text = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyVideos));

        // FFmpeg path
        FfmpegPathBox.Text = _settings.Get("ffmpeg_path") ?? "";
    }

    private async void BrowseOutputBtn_Click(object? sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Directory = OutputPathBox.Text };
        var path = await dialog.ShowAsync(VisualRoot as Window);
        if (!string.IsNullOrEmpty(path))
            OutputPathBox.Text = path;
    }

    private void SaveBtn_Click(object? sender, RoutedEventArgs e)
    {
        // Persist ffmpeg path (other settings can be added here)
        var path = FfmpegPathBox.Text?.Trim() ?? "";
        _settings.Set("ffmpeg_path", path);
        FfmpegLocator.Executable = path;
    }

    // ─── FFmpeg path handlers ─────────────────────────────────────────────────

    private void FfmpegPath_LostFocus(object? sender, RoutedEventArgs e)
    {
        var path = FfmpegPathBox.Text?.Trim() ?? "";
        _settings.Set("ffmpeg_path", path);
        FfmpegLocator.Executable = path;
        FfmpegStatusBadge.IsVisible = false;
    }

    private async void FfmpegBrowse_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select FFmpeg Executable",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Executable")
                {
                    Patterns = new[] { "ffmpeg.exe", "ffmpeg" }
                },
                new FilePickerFileType("All files") { Patterns = new[] { "*" } }
            }
        });

        if (files.Count > 0)
        {
            var path = files[0].Path.LocalPath;
            FfmpegPathBox.Text = path;
            _settings.Set("ffmpeg_path", path);
            FfmpegLocator.Executable = path;
            FfmpegStatusBadge.IsVisible = false;
        }
    }

    private async void FfmpegVerify_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn) btn.IsEnabled = false;
        var exe = string.IsNullOrWhiteSpace(FfmpegPathBox.Text) ? "ffmpeg" : FfmpegPathBox.Text.Trim();

        bool ok = false;
        string statusText;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName               = exe,
                Arguments              = "-version",
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
                CreateNoWindow         = true,
            };
            using var p = Process.Start(psi)!;
            var output = await p.StandardOutput.ReadToEndAsync();
            var err    = await p.StandardError.ReadToEndAsync();
            p.WaitForExit(4000);

            ok = p.ExitCode == 0;
            var versionLine = (output + err).Split('\n')[0].Trim();
            statusText = ok ? $"✓  {versionLine}" : "✗  Not found or failed to run";
        }
        catch
        {
            statusText = "✗  Not found — check path or install FFmpeg";
        }

        FfmpegStatusText.Text       = statusText;
        FfmpegStatusBadge.Background = ok
            ? new SolidColorBrush(Avalonia.Media.Color.FromRgb(0x16, 0xA3, 0x4A))
            : new SolidColorBrush(Avalonia.Media.Color.FromRgb(0xDC, 0x26, 0x26));
        FfmpegStatusText.Foreground  = Brushes.White;
        FfmpegStatusBadge.IsVisible  = true;

        if (sender is Button b) b.IsEnabled = true;
    }
}
