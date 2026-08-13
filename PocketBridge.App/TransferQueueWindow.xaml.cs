using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PocketBridge.App.Localization;
using PocketBridge.Core.Models;
using PocketBridge.Core.Services;

namespace PocketBridge.App;

public partial class TransferQueueWindow : Window
{
    private readonly IFileTransferQueueService _queue;
    private readonly ObservableCollection<Row> _active = new();
    private readonly ObservableCollection<Row> _history = new();

    public TransferQueueWindow(IFileTransferQueueService queue)
    {
        _queue = queue;
        InitializeComponent();
        ActiveList.ItemsSource = _active;
        HistoryList.ItemsSource = _history;
        _queue.Changed += Queue_Changed;
        Closed += (_, _) => _queue.Changed -= Queue_Changed;
        Refresh();
    }

    private Row? Selected => (TransferTabs.SelectedIndex == 0 ? ActiveList.SelectedItem : HistoryList.SelectedItem) as Row;
    private void Queue_Changed(object? sender, EventArgs e) => _ = Dispatcher.BeginInvoke(Refresh);
    private void Refresh()
    {
        var selected = Selected?.Id;
        var query = FilterBox.Text.Trim();
        _active.Clear(); _history.Clear();
        foreach (var item in _queue.Items.Where(item => query.Length == 0 || item.Source.Contains(query, StringComparison.OrdinalIgnoreCase) || item.Destination.Contains(query, StringComparison.OrdinalIgnoreCase) || item.DeviceAlias.Contains(query, StringComparison.OrdinalIgnoreCase)))
            (item.State is FileTransferState.Waiting or FileTransferState.Transferring ? _active : _history).Add(new Row(item));
        if (selected is { } id)
        {
            ActiveList.SelectedItem = _active.FirstOrDefault(row => row.Id == id);
            HistoryList.SelectedItem = _history.FirstOrDefault(row => row.Id == id);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) { if (Selected is { } row) _queue.Cancel(row.Id); }
    private void Retry_Click(object sender, RoutedEventArgs e) { if (Selected is { } row) _queue.Retry(row.Id); }
    private void ClearCompleted_Click(object sender, RoutedEventArgs e) => _queue.ClearCompleted();
    private void ClearHistory_Click(object sender, RoutedEventArgs e) => _queue.ClearHistory();
    private void Filter_TextChanged(object sender, TextChangedEventArgs e) { if (IsInitialized) Refresh(); }
    private void TransferTabs_SelectionChanged(object sender, SelectionChangedEventArgs e) { }
    private void Reveal_Click(object sender, RoutedEventArgs e) { if (Selected is { } row && File.Exists(row.Source)) Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{row.Source}\"") { UseShellExecute = true }); }
    private void CopyDetails_Click(object sender, RoutedEventArgs e) { if (Selected is { } row) Clipboard.SetText(row.CopyText); }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private sealed record Row
    {
        public Row(FileTransferSnapshot item)
        {
            Id = item.Id; Source = item.Source; FileName = Path.GetFileName(item.Source);
            TargetLine = $"{item.DeviceAlias} ({Redact(item.Serial)})  →  {item.Destination}";
            DetailsLine = $"{item.Timestamp.LocalDateTime:g} · {FormatBytes(item.Bytes)} · {(item.Duration is null ? "—" : $"{item.Duration.Value.TotalSeconds:0.0}s")} · {item.Direction}";
            ProgressPercent = item.Progress * 100; ProgressLabel = $"{ProgressPercent:0}%"; Error = item.Error;
            StateLabel = LocalizationService.Current[$"Transfer{item.State}"];
            StateBrush = item.State switch { FileTransferState.Completed => Brush(88, 214, 168), FileTransferState.Failed => Brush(242, 120, 120), FileTransferState.Cancelled => Brush(170, 180, 197), _ => Brush(108, 140, 255) };
            CopyText = $"{FileName}\n{TargetLine}\n{DetailsLine}\n{StateLabel}\n{Error}";
        }
        public Guid Id { get; }
        public string Source { get; }
        public string FileName { get; }
        public string TargetLine { get; }
        public string DetailsLine { get; }
        public string CopyText { get; }
        public double ProgressPercent { get; }
        public string ProgressLabel { get; }
        public string? Error { get; }
        public string StateLabel { get; }
        public Brush StateBrush { get; }
        private static SolidColorBrush Brush(byte r, byte g, byte b) => new(Color.FromRgb(r, g, b));
        private static string Redact(string serial) => serial.Length <= 4 ? "••••" : $"••••{serial[^4..]}";
        private static string FormatBytes(long bytes) => bytes < 1024 ? $"{bytes} B" : bytes < 1024 * 1024 ? $"{bytes / 1024d:0.0} KB" : $"{bytes / 1024d / 1024d:0.0} MB";
    }
}
