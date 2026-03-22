using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using RecordIt.Core.Services;
using RecordIt.Encoder.Models;
using RecordIt.Encoder.Services;
using Windows.Storage.Pickers;

namespace RecordIt.Pages;

public sealed partial class SettingsPage : Page
{
    private readonly SettingsService _settings = new();
    private IReadOnlyList<EncoderOption> _encoderOptions = [];
    private bool _suppressEncoderEvents;

    public SettingsPage()
    {
        this.InitializeComponent();
        LoadSettings();
        HookEvents();
        this.Loaded += async (_, _) => await LoadEncodersAsync();
    }

    // ─── Settings load / save ─────────────────────────────────────────────────

    private void LoadSettings()
    {
        var theme = _settings.Get("theme");
        if (theme != null)
            ThemeCombo.SelectedIndex = theme == "light" ? 1 : theme == "system" ? 2 : 0;

        var lang = _settings.Get("language");
        if (lang != null)
        {
            var idx = lang == "Deutsch" ? 1 : lang == "Français" ? 2 : lang == "Español" ? 3 : lang == "日本語" ? 4 : 0;
            LanguageCombo.SelectedIndex = idx;
        }

        var quality = _settings.Get("quality");
        if (quality != null) QualityCombo.SelectedIndex = quality == "1440p" ? 1 : quality == "4K" ? 2 : quality == "720p" ? 3 : 0;

        var format = _settings.Get("format");
        if (format != null) FormatCombo.SelectedIndex = format.Contains("WebM") ? 1 : format.Contains("MKV") ? 2 : 0;

        var countdown = _settings.Get("countdown");
        if (countdown != null) CountdownToggle.IsOn = countdown == "1";

        var notify = _settings.Get("notifications");
        if (notify != null) NotificationsToggle.IsOn = notify == "1";

        var startMin = _settings.Get("start_minimized");
        if (startMin != null) StartMinimizedToggle.IsOn = startMin == "1";

        // Hardware encoding toggle
        var hwEnabled = _settings.Get("hardware_encoding");
        HardwareEncodingToggle.IsOn = hwEnabled == "1";
    }

    private void HookEvents()
    {
        ThemeCombo.SelectionChanged     += (s, e) => _settings.Set("theme", ThemeCombo.SelectedIndex == 1 ? "light" : ThemeCombo.SelectedIndex == 2 ? "system" : "dark");
        LanguageCombo.SelectionChanged  += (s, e) => _settings.Set("language", ((ComboBoxItem)LanguageCombo.SelectedItem).Content.ToString() ?? "English");
        QualityCombo.SelectionChanged   += (s, e) => _settings.Set("quality", QualityCombo.SelectedIndex == 1 ? "1440p" : QualityCombo.SelectedIndex == 2 ? "4K" : QualityCombo.SelectedIndex == 3 ? "720p" : "1080p");
        FormatCombo.SelectionChanged    += (s, e) => _settings.Set("format", ((ComboBoxItem)FormatCombo.SelectedItem).Content.ToString() ?? "MP4");
        CountdownToggle.Toggled         += (s, e) => _settings.Set("countdown", CountdownToggle.IsOn ? "1" : "0");
        NotificationsToggle.Toggled     += (s, e) => _settings.Set("notifications", NotificationsToggle.IsOn ? "1" : "0");
        StartMinimizedToggle.Toggled    += (s, e) => _settings.Set("start_minimized", StartMinimizedToggle.IsOn ? "1" : "0");
    }

    // ─── Hardware encoding / GPU selector ─────────────────────────────────────

    private async Task LoadEncodersAsync()
    {
        EncoderStatusText.Text   = "Detecting encoders…";
        EncoderCombo.IsEnabled   = false;
        RefreshEncodersBtn.IsEnabled = false;

        var svc     = new HardwareEncoderService(FfmpegLocator.Executable);
        _encoderOptions = await svc.GetEncoderOptionsAsync();

        // Rebuild ComboBox items
        _suppressEncoderEvents = true;
        EncoderCombo.Items.Clear();
        foreach (var opt in _encoderOptions)
            EncoderCombo.Items.Add(new ComboBoxItem { Content = opt.DisplayName, Tag = opt });

        // Restore saved selection
        var savedCodec = _settings.Get("video_encoder") ?? "libx264";
        int savedIdx   = 0;
        for (int i = 0; i < _encoderOptions.Count; i++)
        {
            if (_encoderOptions[i].Id.Equals(savedCodec, StringComparison.OrdinalIgnoreCase))
            { savedIdx = i; break; }
        }
        EncoderCombo.SelectedIndex = savedIdx;
        _suppressEncoderEvents = false;

        ApplySelectedEncoder();

        EncoderStatusText.Text       = HardwareEncoderService.Summarise(_encoderOptions);
        EncoderCombo.IsEnabled       = HardwareEncodingToggle.IsOn;
        RefreshEncodersBtn.IsEnabled = true;
    }

    private void ApplySelectedEncoder()
    {
        if (_suppressEncoderEvents) return;
        if (EncoderCombo.SelectedItem is not ComboBoxItem { Tag: EncoderOption opt }) return;

        // When hardware encoding is toggled off always fall back to software
        var effective = HardwareEncodingToggle.IsOn ? opt : EncoderOption.SoftwareFallback;

        VideoEncoderSettings.Codec     = effective.FfmpegCodec;
        VideoEncoderSettings.ExtraArgs = effective.ExtraArgs;
        _settings.Set("video_encoder", effective.Id);
    }

    private void HardwareEncodingToggle_Toggled(object sender, RoutedEventArgs e)
    {
        _settings.Set("hardware_encoding", HardwareEncodingToggle.IsOn ? "1" : "0");
        EncoderCombo.IsEnabled = HardwareEncodingToggle.IsOn;
        ApplySelectedEncoder();
    }

    private void EncoderCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        ApplySelectedEncoder();

    private async void RefreshEncoders_Click(object sender, RoutedEventArgs e) =>
        await LoadEncodersAsync();

    // ─── FFmpeg path handlers ─────────────────────────────────────────────────

    private void FfmpegPath_LostFocus(object sender, RoutedEventArgs e)
    {
        var path = FfmpegPathBox.Text.Trim();
        _settings.Set("ffmpeg_path", path);
        FfmpegLocator.Executable = path;
        FfmpegStatusBadge.Visibility = Visibility.Collapsed;
    }

    private async void FfmpegBrowse_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".exe");
        picker.FileTypeFilter.Add("*");
        picker.SuggestedStartLocation = PickerLocationId.ComputerFolder;

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        var file = await picker.PickSingleFileAsync();
        if (file != null)
        {
            FfmpegPathBox.Text = file.Path;
            _settings.Set("ffmpeg_path", file.Path);
            FfmpegLocator.Executable = file.Path;
            FfmpegStatusBadge.Visibility = Visibility.Collapsed;
        }
    }

    private async void FfmpegVerify_Click(object sender, RoutedEventArgs e)
    {
        FfmpegVerifyBtn.IsEnabled = false;
        var exe = string.IsNullOrWhiteSpace(FfmpegPathBox.Text) ? "ffmpeg" : FfmpegPathBox.Text.Trim();

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

            bool ok          = p.ExitCode == 0;
            var versionLine  = ((output + err).Split('\n')[0]).Trim();
            FfmpegStatusText.Text        = ok ? $"✓  {versionLine}" : "✗  Not found or failed to run";
            FfmpegStatusBadge.Background = ok
                ? new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x16, 0xA3, 0x4A))
                : new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xDC, 0x26, 0x26));
            FfmpegStatusText.Foreground  = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF));
            FfmpegStatusBadge.Visibility = Visibility.Visible;
        }
        catch
        {
            FfmpegStatusText.Text        = "✗  Not found — check path or install FFmpeg";
            FfmpegStatusBadge.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xDC, 0x26, 0x26));
            FfmpegStatusText.Foreground  = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF));
            FfmpegStatusBadge.Visibility = Visibility.Visible;
        }
        finally
        {
            FfmpegVerifyBtn.IsEnabled = true;
        }
    }
}
