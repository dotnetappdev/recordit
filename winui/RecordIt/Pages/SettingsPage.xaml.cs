using Microsoft.UI.Xaml.Controls;
using RecordIt.Core.Services;
using Microsoft.UI.Xaml;

namespace RecordIt.Pages;

public sealed partial class SettingsPage : Page
{
    private readonly SettingsService _settings = new();

    public SettingsPage()
    {
        this.InitializeComponent();
        LoadSettings();
        HookEvents();
    }

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

        var hw = _settings.Get("hardware_accel");
        if (hw != null) HardwareAccelToggle.IsOn = hw == "1";

        var startMin = _settings.Get("start_minimized");
        if (startMin != null) StartMinimizedToggle.IsOn = startMin == "1";
    }

    private void HookEvents()
    {
        ThemeCombo.SelectionChanged += (s, e) => _settings.Set("theme", ThemeCombo.SelectedIndex == 1 ? "light" : ThemeCombo.SelectedIndex == 2 ? "system" : "dark");
        LanguageCombo.SelectionChanged += (s, e) => _settings.Set("language", ((ComboBoxItem)LanguageCombo.SelectedItem).Content.ToString() ?? "English");
        QualityCombo.SelectionChanged += (s, e) => _settings.Set("quality", QualityCombo.SelectedIndex == 1 ? "1440p" : QualityCombo.SelectedIndex == 2 ? "4K" : QualityCombo.SelectedIndex == 3 ? "720p" : "1080p");
        FormatCombo.SelectionChanged += (s, e) => _settings.Set("format", ((ComboBoxItem)FormatCombo.SelectedItem).Content.ToString() ?? "MP4");
        CountdownToggle.Toggled += (s, e) => _settings.Set("countdown", CountdownToggle.IsOn ? "1" : "0");
        NotificationsToggle.Toggled += (s, e) => _settings.Set("notifications", NotificationsToggle.IsOn ? "1" : "0");
        HardwareAccelToggle.Toggled += (s, e) => _settings.Set("hardware_accel", HardwareAccelToggle.IsOn ? "1" : "0");
        StartMinimizedToggle.Toggled += (s, e) => _settings.Set("start_minimized", StartMinimizedToggle.IsOn ? "1" : "0");
    }
}


