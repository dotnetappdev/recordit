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

        // Docking settings
        var modTab = _settings.Get("docking.mod_tab") ?? "Control";
        DockTabCombo.SelectedIndex = modTab == "Control" ? 0 : modTab == "Shift" ? 1 : modTab == "Alt" ? 2 : 3;

        var modFloat = _settings.Get("docking.mod_float") ?? "Shift";
        DockFloatCombo.SelectedIndex = modFloat == "Control" ? 0 : modFloat == "Shift" ? 1 : modFloat == "Alt" ? 2 : 3;

        var hold = _settings.Get("docking.hold_ms") ?? "500";
        HoldMsBox.Text = hold;
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

        DockTabCombo.SelectionChanged   += (s, e) => _settings.Set("docking.mod_tab", ((ComboBoxItem)DockTabCombo.SelectedItem).Content.ToString() ?? "Control");
        DockFloatCombo.SelectionChanged += (s, e) => _settings.Set("docking.mod_float", ((ComboBoxItem)DockFloatCombo.SelectedItem).Content.ToString() ?? "Shift");
        HoldMsBox.LostFocus             += (s, e) =>
        {
            if (int.TryParse(HoldMsBox.Text.Trim(), out var v) && v > 0)
                _settings.Set("docking.hold_ms", v.ToString());
            else
                HoldMsBox.Text = _settings.Get("docking.hold_ms") ?? "500";
        };
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
        FfmpegStatusBadge.Visibility = Visibility.Collapsed;

        // Apply any typed path first
        if (!string.IsNullOrWhiteSpace(FfmpegPathBox.Text))
            FfmpegLocator.Executable = FfmpegPathBox.Text.Trim();

        var ok = await FfmpegLocator.EnsureAvailableAsync(installIfMissing: true);

        if (ok)
        {
            // Re-run ffmpeg -version to get a version line
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = FfmpegLocator.Executable,
                    Arguments = "-version",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using var p = Process.Start(psi)!;
                var output = await p.StandardOutput.ReadToEndAsync();
                var err = await p.StandardError.ReadToEndAsync();
                p.WaitForExit(3000);
                var versionLine = ((output + err).Split('\n')[0]).Trim();
                FfmpegStatusText.Text = $"✓  {versionLine}";
                FfmpegStatusBadge.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x16, 0xA3, 0x4A));
            }
            catch
            {
                FfmpegStatusText.Text = "✓  FFmpeg available";
                FfmpegStatusBadge.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x16, 0xA3, 0x4A));
            }
        }
        else
        {
            FfmpegStatusText.Text = "✗  Not found — installation failed";
            FfmpegStatusBadge.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xDC, 0x26, 0x26));
        }

        FfmpegStatusText.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF));
        FfmpegStatusBadge.Visibility = Visibility.Visible;
        FfmpegVerifyBtn.IsEnabled = true;
    }

    // ─── Action buttons ───────────────────────────────────────────────────────

    private void ApplyBtn_Click(object sender, RoutedEventArgs e)
    {
        // Settings are auto-saved on change, so just show confirmation
        ShowStatus("Settings applied successfully");
    }

    private void CancelBtn_Click(object sender, RoutedEventArgs e)
    {
        // Navigate back without applying (settings already auto-saved though)
        App.MainWindow?.NavigateTo("record");
    }

    private void CloseBtn_Click(object sender, RoutedEventArgs e)
    {
        // Apply and close
        ShowStatus("Settings saved");
        App.MainWindow?.NavigateTo("record");
    }

    private void ShowStatus(string message)
    {
        // Optional: Show a brief status message
        // Could use InfoBar or transient notification
    }
}
