using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace RecordIt.Pages;

public sealed partial class LibraryPage : Page
{
    public ObservableCollection<LibraryItem> Items { get; } = new();
    private LibraryItem[] _allItems = Array.Empty<LibraryItem>();
    private string _activeFilter = "all";

    public LibraryPage()
    {
        this.InitializeComponent();
        RecordingsGrid.ItemsSource = Items;
        _ = LoadFilesAsync();
    }

    // ── Real file scanning ────────────────────────────────────────────────────

    private async System.Threading.Tasks.Task LoadFilesAsync()
    {
        var folders = GetScanFolders();
        var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { ".mp4", ".webm", ".mkv", ".mov", ".avi" };

        var found = new List<LibraryItem>();

        await System.Threading.Tasks.Task.Run(() =>
        {
            foreach (var folder in folders)
            {
                if (!Directory.Exists(folder)) continue;
                try
                {
                    foreach (var path in Directory.EnumerateFiles(folder, "*.*",
                        SearchOption.TopDirectoryOnly))
                    {
                        if (!extensions.Contains(Path.GetExtension(path))) continue;
                        var info = new FileInfo(path);
                        found.Add(new LibraryItem(
                            name:     Path.GetFileNameWithoutExtension(path),
                            type:     "video",
                            duration: null,          // would need MediaInfo to read this
                            size:     FormatSize(info.Length),
                            date:     FormatDate(info.LastWriteTime),
                            fullPath: path));
                    }
                }
                catch { /* skip inaccessible folder */ }

                // Also pick up .png whiteboards saved from the Whiteboard page
                try
                {
                    foreach (var path in Directory.EnumerateFiles(folder, "Whiteboard_*.png",
                        SearchOption.TopDirectoryOnly))
                    {
                        var info = new FileInfo(path);
                        found.Add(new LibraryItem(
                            name:     Path.GetFileNameWithoutExtension(path),
                            type:     "whiteboard",
                            duration: null,
                            size:     FormatSize(info.Length),
                            date:     FormatDate(info.LastWriteTime),
                            fullPath: path));
                    }
                }
                catch { }
            }
        });

        // Sort newest-first
        _allItems = found.OrderByDescending(i => i.LastWriteTime).ToArray();
        ApplyFilter(_activeFilter);
    }

    private static List<string> GetScanFolders()
    {
        var folders = new List<string>();

        // 1. Windows user Videos folder
        try { folders.Add(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)); } catch { }

        // 2. User desktop (some people save there)
        try { folders.Add(Environment.GetFolderPath(Environment.SpecialFolder.Desktop)); } catch { }

        // 3. App data folder where recordings are stored
        try
        {
            var appData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "RecordIt");
            if (Directory.Exists(appData)) folders.Add(appData);
        }
        catch { }

        return folders.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    // ── Filter ────────────────────────────────────────────────────────────────

    private void ApplyFilter(string filter)
    {
        _activeFilter = filter;
        Items.Clear();
        var filtered = filter == "all"
            ? _allItems
            : _allItems.Where(i => i.Type == filter);
        foreach (var item in filtered) Items.Add(item);
    }

    private void AllFilter_Click(object s, RoutedEventArgs e)        => ApplyFilter("all");
    private void VideoFilter_Click(object s, RoutedEventArgs e)      => ApplyFilter("video");
    private void WhiteboardFilter_Click(object s, RoutedEventArgs e) => ApplyFilter("whiteboard");
    private void FilterBtn_Click(object s, RoutedEventArgs e)        => _ = LoadFilesAsync();

    // ── Item click ────────────────────────────────────────────────────────────

    private void RecordingItem_Click(object s, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not LibraryItem item || string.IsNullOrEmpty(item.FullPath)) return;

        // Open the containing folder and select the file
        var dir = Path.GetDirectoryName(item.FullPath);
        if (Directory.Exists(dir))
        {
            try
            {
                Process.Start(new ProcessStartInfo(
                    "explorer.exe", $"/select,\"{item.FullPath}\"")
                    { UseShellExecute = true });
            }
            catch { }
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string FormatSize(long bytes) =>
        bytes switch
        {
            >= 1_073_741_824 => $"{bytes / 1_073_741_824.0:0.#} GB",
            >= 1_048_576     => $"{bytes / 1_048_576.0:0.#} MB",
            _                => $"{bytes / 1024.0:0.#} KB",
        };

    private static string FormatDate(DateTime dt)
    {
        var today = DateTime.Today;
        if (dt.Date == today)            return $"Today, {dt:h:mm tt}";
        if (dt.Date == today.AddDays(-1)) return $"Yesterday, {dt:h:mm tt}";
        return dt.ToString("MMM d, yyyy");
    }
}

public class LibraryItem
{
    public string  Name      { get; }
    public string  Type      { get; }
    public string? Duration  { get; }
    public string  Size      { get; }
    public string  Date      { get; }
    public string  FullPath  { get; }
    public DateTime LastWriteTime { get; }

    public string TypeIcon => Type == "video" ? "\uEA69" : "\uEB9F";

    public LibraryItem(string name, string type, string? duration,
                       string size, string date, string fullPath = "")
    {
        Name     = name;
        Type     = type;
        Duration = duration;
        Size     = size;
        Date     = date;
        FullPath = fullPath;
        LastWriteTime = string.IsNullOrEmpty(fullPath) || !File.Exists(fullPath)
            ? DateTime.MinValue
            : File.GetLastWriteTime(fullPath);
    }
}
