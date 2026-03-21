using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RecordIt.Core.Services;
using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace RecordIt.Pages;

public sealed partial class RecordPage : Page
{
    private readonly ScreenRecordingService _recordingService;
    private string? _selectedSourceId;
    private bool _micEnabled = true;
    private bool _cameraEnabled = false;

        private readonly DockedPreviewManager? _previewManager;

    public ObservableCollection<CaptureSource> Sources { get; } = new();

    private readonly List<string> _audioTracks = new();
    private Process? _previewProcess;

    public RecordPage()
    {
        this.InitializeComponent();
        _recordingService = new ScreenRecordingService();
        SourcesGridView.ItemsSource = Sources;
        AudioTracksList.ItemsSource = _audioTracks;
        LoadSources();
        try
        {
            var hwnd = WindowNativeInterop.GetWindowHandle(App.MainWindow!);
            _previewManager = new DockedPreviewManager(hwnd);
        }
        catch { }
    }

    private async void LoadSources()
    {
        Sources.Clear();
        var sources = await _recordingService.GetCaptureSources();
        foreach (var src in sources) Sources.Add(src);

        EmptySourcesState.Visibility = Sources.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

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

        var webcamDevice = (WebcamDeviceCombo.SelectedItem as ComboBoxItem)?.Content?.ToString();
        var micDevice = (MicDeviceCombo.SelectedItem as ComboBoxItem)?.Content?.ToString();
        if (webcamDevice == "Default") webcamDevice = null;
        if (micDevice == "Default") micDevice = null;

        await _recordingService.StartRecording(_selectedSourceId, file.Path, quality, fps, _micEnabled, _cameraEnabled, webcamDevice, micDevice);

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

        try
        {
            var export = new ExportService();
            var caption = new CaptionService();

            var folderPicker = new FolderPicker();
            var hwnd = WindowNativeInterop.GetWindowHandle(App.MainWindow!);
            InitializeWithWindow.Initialize(folderPicker, hwnd);
            folderPicker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.VideosLibrary;
            var outFolder = await folderPicker.PickSingleFolderAsync();
            var exportDir = outFolder != null ? outFolder.Path : Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);

            var savedFile = await FindMostRecentRecordingAsync(exportDir);
            if (savedFile != null)
            {
                var outputs = await export.RenderPresetsAsync(savedFile, exportDir);
                var transcript = await caption.TranscribeAsync(savedFile);

                // If additional audio tracks attached, create merged output
                if (_audioTracks.Count > 0)
                {
                    var merged = Path.Combine(exportDir, Path.GetFileNameWithoutExtension(savedFile) + "_with_audio.mp4");
                    await export.MergeAudioTracksAsync(savedFile, _audioTracks.ToArray(), merged);
                }

                var content = $"Saved: {Path.GetFileName(savedFile)}\nExports:\n{string.Join("\n", outputs)}\n\nTranscript:\n{(string.IsNullOrWhiteSpace(transcript) ? "(no transcript)" : transcript)}";
                var dialog = new ContentDialog
                {
                    Title = "Recording Saved",
                    Content = content,
                    CloseButtonText = "OK",
                    XamlRoot = this.XamlRoot
                };
                await dialog.ShowAsync();
            }
        }
        catch (Exception ex)
        {
            var dialog = new ContentDialog
            {
                Title = "Recording Saved",
                Content = "Your recording has been saved successfully. Post-processing failed: " + ex.Message,
                CloseButtonText = "OK",
                XamlRoot = this.XamlRoot
            };
            await dialog.ShowAsync();
        }
    }

    private static async Task<string?> FindMostRecentRecordingAsync(string folderPath)
    {
        try
        {
            var dir = new DirectoryInfo(folderPath);
            var file = dir.GetFiles("*.mp4").OrderByDescending(f => f.CreationTimeUtc).FirstOrDefault();
            return file?.FullName;
        }
        catch { return null; }
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

    private async void ProbeDevicesBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var devices = await _recordingService.ProbeDshowDevicesAsync();
            WebcamDeviceCombo.Items.Clear();
            MicDeviceCombo.Items.Clear();
            WebcamDeviceCombo.Items.Add(new ComboBoxItem { Content = "Default" });
            MicDeviceCombo.Items.Add(new ComboBoxItem { Content = "Default" });
            foreach (var d in devices)
            {
                WebcamDeviceCombo.Items.Add(new ComboBoxItem { Content = d });
                MicDeviceCombo.Items.Add(new ComboBoxItem { Content = d });
            }
            WebcamDeviceCombo.SelectedIndex = 0;
            MicDeviceCombo.SelectedIndex = 0;
        }
        catch { }
    }

    private void StartPreviewBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_previewManager != null)
            {
                _previewManager.Width = (int)PreviewHost.Width;
                _previewManager.Height = (int)PreviewHost.Height;
                _previewManager.StartPreview("-f gdigrab -framerate 30 -i desktop");
            }
            else
            {
                if (_previewProcess != null && !_previewProcess.HasExited) return;
                var psi = new ProcessStartInfo
                {
                    FileName = "ffplay",
                    Arguments = "-f gdigrab -framerate 30 -i desktop",
                    UseShellExecute = false,
                    CreateNoWindow = false
                };
                _previewProcess = Process.Start(psi);
            }
        }
        catch { }
    }

    private void StopPreviewBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_previewManager != null)
            {
                _previewManager.StopPreview();
            }
            else
            {
                if (_previewProcess != null && !_previewProcess.HasExited)
                    _previewProcess.Kill(true);
            }
        }
        catch { }
        finally { _previewProcess = null; }
    }

    private async void AddAudioTrackBtn_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        var hwnd = WindowNativeInterop.GetWindowHandle(App.MainWindow!);
        InitializeWithWindow.Initialize(picker, hwnd);
        picker.FileTypeFilter.Add(".mp3");
        picker.FileTypeFilter.Add(".wav");
        picker.SuggestedStartLocation = PickerLocationId.MusicLibrary;
        var file = await picker.PickSingleFileAsync();
        if (file != null)
        {
            _audioTracks.Add(file.Path);
            AudioTracksList.ItemsSource = null;
            AudioTracksList.ItemsSource = _audioTracks;
        }
    }

    private void ClearAudioTracksBtn_Click(object sender, RoutedEventArgs e)
    {
        _audioTracks.Clear();
        AudioTracksList.ItemsSource = null;
        AudioTracksList.ItemsSource = _audioTracks;
    }

    private async void ExportBtn_Click(object sender, RoutedEventArgs e)
    {
        var exportDir = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
        var savedFile = await FindMostRecentRecordingAsync(exportDir);
        if (savedFile == null)
        {
            var dlg = new ContentDialog { Title = "No recording", Content = "No recent recording found in Videos.", CloseButtonText = "OK", XamlRoot = this.XamlRoot };
            await dlg.ShowAsync();
            return;
        }

        var dlgExport = new ExportDialog(savedFile) { XamlRoot = this.XamlRoot };
        await dlgExport.ShowAsync();
    }
}
