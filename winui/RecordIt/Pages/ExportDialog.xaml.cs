using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RecordIt.Core.Services;
using System;
using System.IO;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace RecordIt.Pages;

public sealed partial class ExportDialog : ContentDialog
{
    private readonly string _inputFile;
    private readonly ExportService _export = new();

    public ExportDialog(string inputFile)
    {
        this.InitializeComponent();
        _inputFile = inputFile;
        OutputPathBox.Text = Path.GetDirectoryName(inputFile) ?? Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
        _export.ProgressChanged += p => DispatcherQueue.TryEnqueue(() =>
        {
            if (p >= 0)
            {
                ProgressBar.IsIndeterminate = false;
                ProgressBar.Value = p;
                ProgressText.Text = $"{p:F0}%";
            }
            else
            {
                ProgressBar.IsIndeterminate = true;
                ProgressText.Text = "Rendering...";
            }
        });
    }

    private async void BrowseBtn_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker();
        var hwnd = WindowNativeInterop.GetWindowHandle(App.MainWindow!);
        InitializeWithWindow.Initialize(picker, hwnd);
        var folder = await picker.PickSingleFolderAsync();
        if (folder != null) OutputPathBox.Text = folder.Path;
    }

    private async void ContentDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        try
        {
            ProgressBar.IsIndeterminate = true;
            ProgressText.Text = "Starting...";
            var outputs = await _export.RenderPresetsAsync(_inputFile, OutputPathBox.Text);
            ProgressBar.IsIndeterminate = false;
            ProgressBar.Value = 100;
            ProgressText.Text = "Done";
        }
        catch (Exception ex)
        {
            ProgressText.Text = "Failed: " + ex.Message;
        }
    }
}
