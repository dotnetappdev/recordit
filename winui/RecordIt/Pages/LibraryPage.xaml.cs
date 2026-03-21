using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Collections.ObjectModel;
using System.Linq;

namespace RecordIt.Pages;

public sealed partial class LibraryPage : Page
{
    public ObservableCollection<LibraryItem> Items { get; } = new();
    private readonly LibraryItem[] _allItems;

    public LibraryPage()
    {
        this.InitializeComponent();
        _allItems = new[]
        {
            new LibraryItem("Product Demo Recording",   "video",      "5:32",  "124 MB", "Today, 2:30 PM"),
            new LibraryItem("Architecture Whiteboard",  "whiteboard", null,    "2.1 MB", "Today, 11:15 AM"),
            new LibraryItem("Team Standup - March 21",  "video",      "12:04", "287 MB", "Yesterday"),
            new LibraryItem("UI Design Whiteboard",     "whiteboard", null,    "1.8 MB", "Mar 20"),
            new LibraryItem("Bug Reproduction Steps",   "video",      "3:17",  "78 MB",  "Mar 19"),
            new LibraryItem("System Architecture Board","whiteboard", null,    "3.2 MB", "Mar 18"),
        };
        foreach (var item in _allItems) Items.Add(item);
        RecordingsGrid.ItemsSource = Items;
    }

    private void ApplyFilter(string filter)
    {
        Items.Clear();
        var filtered = filter == "all" ? _allItems : _allItems.Where(i => i.Type == filter);
        foreach (var item in filtered) Items.Add(item);
    }

    private void AllFilter_Click(object s, RoutedEventArgs e) => ApplyFilter("all");
    private void VideoFilter_Click(object s, RoutedEventArgs e) => ApplyFilter("video");
    private void WhiteboardFilter_Click(object s, RoutedEventArgs e) => ApplyFilter("whiteboard");
    private void FilterBtn_Click(object s, RoutedEventArgs e) { }
    private void RecordingItem_Click(object s, ItemClickEventArgs e) { }
}

public class LibraryItem
{
    public string Name { get; }
    public string Type { get; }
    public string? Duration { get; }
    public string Size { get; }
    public string Date { get; }
    public string TypeIcon => Type == "video" ? "\uEA69" : "\uEB9F";

    public LibraryItem(string name, string type, string? duration, string size, string date)
    {
        Name = name; Type = type; Duration = duration; Size = size; Date = date;
    }
}
