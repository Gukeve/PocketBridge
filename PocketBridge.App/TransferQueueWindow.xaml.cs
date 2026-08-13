using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using PocketBridge.App.Localization;
using PocketBridge.Core.Models;
using PocketBridge.Core.Services;

namespace PocketBridge.App;

public partial class TransferQueueWindow : Window
{
    private readonly IFileTransferQueueService _queue;
    private readonly ObservableCollection<Row> _rows = new();

    public TransferQueueWindow(IFileTransferQueueService queue)
    {
        _queue = queue;
        InitializeComponent();
        TransfersList.ItemsSource = _rows;
        _queue.Changed += Queue_Changed;
        Closed += (_, _) => _queue.Changed -= Queue_Changed;
        Refresh();
    }

    private Row? Selected => TransfersList.SelectedItem as Row;
    private void Queue_Changed(object? sender, EventArgs e) => _ = Dispatcher.BeginInvoke(Refresh);
    private void Refresh()
    {
        var selected = Selected?.Id;
        _rows.Clear();
        foreach (var item in _queue.Items) _rows.Add(new Row(item));
        if (selected is { } id) TransfersList.SelectedItem = _rows.FirstOrDefault(row => row.Id == id);
    }
    private void Cancel_Click(object sender, RoutedEventArgs e) { if (Selected is { } row) _queue.Cancel(row.Id); }
    private void Retry_Click(object sender, RoutedEventArgs e) { if (Selected is { } row) _queue.Retry(row.Id); }
    private void ClearCompleted_Click(object sender, RoutedEventArgs e) => _queue.ClearCompleted();
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private sealed record Row
    {
        public Row(FileTransferSnapshot item) { Id = item.Id; FileName = Path.GetFileName(item.Source); TargetLine = $"{item.Serial}  →  {item.Destination}"; ProgressPercent = item.Progress * 100; ProgressLabel = $"{ProgressPercent:0}%"; Error = item.Error; StateLabel = LocalizationService.Current[$"Transfer{item.State}"]; StateBrush = item.State switch { FileTransferState.Completed => Brush(88,214,168), FileTransferState.Failed => Brush(242,120,120), FileTransferState.Cancelled => Brush(170,180,197), _ => Brush(108,140,255) }; }
        public Guid Id { get; }
        public string FileName { get; }
        public string TargetLine { get; }
        public double ProgressPercent { get; }
        public string ProgressLabel { get; }
        public string? Error { get; }
        public string StateLabel { get; }
        public Brush StateBrush { get; }
        private static SolidColorBrush Brush(byte r, byte g, byte b) => new(Color.FromRgb(r,g,b));
    }
}
