using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RecordIt.Services;
using System;
using System.Collections.ObjectModel;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace RecordIt.Pages;

public sealed partial class RecordPage : Page
{
    private readonly ScreenRecordingService _recordingService;
    private string? _selectedSourceId;
    private bool _micEnabled = true;
    private bool _cameraEnabled = false;

    public ObservableCollection<CaptureSource> Sources { get; } = new();

    public RecordPage()
    {
        this.InitializeComponent();
        _recordingService = new ScreenRecordingService();
        SourcesGridView.ItemsSource = Sources;
        LoadSources();
    }

    private async void LoadSources()
    {
        Sources.Clear();
        var sources = await _recordingService.GetCaptureSources();
        foreach (var src in sources)
            Sources.Add(src);

        EmptySourcesState.Visibility = Sources.Count == 0
            ? Visibility.Visible : Visibility.Collapsed;

        if (Sources.Count > 0)
        {
            _selectedSourceId = Sources[0].Id;
            RecordHintText.Text = "Ready to record";
        }
    }

    private void SourceItem_Click(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is CaptureSource src)
        {
            _selectedSourceId = src.Id;
            RecordHintText.Text = $"Ready to record: {src.Name}";
        }
    }

    private async void StartRecordBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedSourceId == null) return;

        var quality = QualityCombo.SelectedIndex switch
        {
            0 => "1920x1080",
            1 => "2560x1440",
            2 => "3840x2160",
            3 => "1280x720",
            4 => "854x480",
            _ => "1920x1080"
        };

        var fps = FpsCombo.SelectedIndex switch
        {
            0 => 60,
            1 => 30,
            2 => 24,
            3 => 15,
            _ => 30
        };

        var savePicker = new FileSavePicker();
        var hwnd = WindowNativeInterop.GetWindowHandle(App.MainWindow!);
        InitializeWithWindow.Initialize(savePicker, hwnd);
        savePicker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.VideosLibrary;
        savePicker.SuggestedFileName = $"RecordIt-{DateTime.Now:yyyyMMdd-HHmmss}";
        savePicker.FileTypeChoices.Add("MP4 Video", new[] { ".mp4" });
        savePicker.FileTypeChoices.Add("WebM Video", new[] { ".webm" });

        var file = await savePicker.PickSaveFileAsync();
        if (file == null) return;

        await _recordingService.StartRecording(_selectedSourceId, file.Path, quality, fps, _micEnabled);

        StartRecordBtn.Visibility = Visibility.Collapsed;
        StopRecordBtn.Visibility = Visibility.Visible;
        App.MainWindow?.StartRecordingIndicator();
    }

    private async void StopRecordBtn_Click(object sender, RoutedEventArgs e)
    {
        await _recordingService.StopRecording();
        StopRecordBtn.Visibility = Visibility.Collapsed;
        StartRecordBtn.Visibility = Visibility.Visible;
        App.MainWindow?.StopRecordingIndicator();

        var dialog = new ContentDialog
        {
            Title = "Recording Saved",
            Content = "Your recording has been saved successfully.",
            CloseButtonText = "OK",
            XamlRoot = this.XamlRoot
        };
        await dialog.ShowAsync();
    }

    private void RefreshSourcesBtn_Click(object sender, RoutedEventArgs e) => LoadSources();

    private void MicBtn_Click(object sender, RoutedEventArgs e)
    {
        _micEnabled = !_micEnabled;
        MicIcon.Glyph = _micEnabled ? "\uE720" : "\uE74F";
    }

    private void CamBtn_Click(object sender, RoutedEventArgs e)
    {
        _cameraEnabled = !_cameraEnabled;
        CamIcon.Glyph = _cameraEnabled ? "\uE714" : "\uE8AA";
    }
}
