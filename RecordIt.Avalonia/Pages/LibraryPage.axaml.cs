using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace RecordIt.Avalonia.Pages;

public class LibraryItem
{
    public string Name      { get; set; } = string.Empty;
    public string Meta      { get; set; } = string.Empty;
    public string ThumbIcon { get; set; } = "▶";
    public bool   IsVideo   { get; set; } = true;
    public string FilePath  { get; set; } = string.Empty;
}

public partial class LibraryPage : UserControl
{
    private readonly ObservableCollection<LibraryItem> _allItems = new();
    private readonly ObservableCollection<LibraryItem> _filtered = new();
    private string _filter = "all";

    public LibraryPage()
    {
        InitializeComponent();
        RecordingGrid.ItemsSource = _filtered;
        LoadLibrary();
    }

    private void LoadLibrary()
    {
        _allItems.Clear();
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos));
        if (Directory.Exists(dir))
        {
            foreach (var f in Directory.GetFiles(dir, "*.mp4").Concat(Directory.GetFiles(dir, "*.mkv")))
            {
                var info = new FileInfo(f);
                _allItems.Add(new LibraryItem
                {
                    Name      = info.Name,
                    Meta      = $"{info.LastWriteTime:yyyy-MM-dd} · {info.Length / 1024 / 1024} MB",
                    ThumbIcon = "▶",
                    IsVideo   = true,
                    FilePath  = f,
                });
            }
        }

        UpdateStats();
        ApplyFilter();
    }

    private void UpdateStats()
    {
        var videos     = _allItems.Count(i => i.IsVideo);
        var boards     = _allItems.Count(i => !i.IsVideo);
        var totalBytes = _allItems
            .Where(i => File.Exists(i.FilePath))
            .Sum(i => new FileInfo(i.FilePath).Length);

        VideoCountLabel.Text     = videos.ToString();
        WhiteboardCountLabel.Text = boards.ToString();
        StorageLabel.Text        = totalBytes > 1_073_741_824
            ? $"{totalBytes / 1_073_741_824.0:0.0} GB"
            : $"{totalBytes / 1_048_576.0:0} MB";
    }

    private void ApplyFilter()
    {
        var query = SearchBox?.Text ?? string.Empty;
        _filtered.Clear();
        foreach (var item in _allItems)
        {
            bool matchFilter = _filter switch
            {
                "video" => item.IsVideo,
                "board" => !item.IsVideo,
                _       => true,
            };
            bool matchSearch = string.IsNullOrEmpty(query) ||
                               item.Name.Contains(query, StringComparison.OrdinalIgnoreCase);
            if (matchFilter && matchSearch)
                _filtered.Add(item);
        }

        EmptyState.IsVisible = _filtered.Count == 0;
    }

    private void SearchBox_TextChanged(object? sender, TextChangedEventArgs e) => ApplyFilter();

    private void FilterAll_Click(object? sender, RoutedEventArgs e)   { _filter = "all";   ApplyFilter(); }
    private void FilterVideo_Click(object? sender, RoutedEventArgs e) { _filter = "video";  ApplyFilter(); }
    private void FilterBoard_Click(object? sender, RoutedEventArgs e) { _filter = "board";  ApplyFilter(); }

    private void ImportBtn_Click(object? sender, RoutedEventArgs e) => LoadLibrary();
}
