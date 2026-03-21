using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace RecordIt.Avalonia.Pages;

public partial class SettingsPage : UserControl
{
    public SettingsPage()
    {
        InitializeComponent();
        OutputPathBox.Text = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyVideos));
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
        // Persist settings here (e.g. AppSettings singleton or file)
    }
}
