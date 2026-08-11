using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using PocketBridge.App.Localization;
using PocketBridge.App.Services;
using PocketBridge.Core.Models;
using PocketBridge.Core.Services;

namespace PocketBridge.App;

public partial class FileManagerWindow : Window
{
    private const string StorageRoot = "/storage/emulated/0";
    private readonly AndroidDevice _device;
    private readonly IAdbFileService _files;
    private readonly IConfirmationService _confirmation;
    private readonly ObservableCollection<FileRow> _entries = new();
    private string _currentPath = StorageRoot;
    private bool _busy;

    public FileManagerWindow(AndroidDevice device, IAdbFileService files, IConfirmationService confirmation)
    {
        _device = device;
        _files = files;
        _confirmation = confirmation;
        InitializeComponent();
        DeviceText.Text = $"{device.FriendlyName} — {device.Serial}";
        EntriesList.ItemsSource = _entries;
        Loaded += async (_, _) => await RefreshAsync();
    }

    private FileRow? Selected => EntriesList.SelectedItem as FileRow;

    private async Task RefreshAsync()
    {
        await RunAsync(async () =>
        {
            var entries = await _files.ListAsync(_device.Serial, _currentPath);
            _entries.Clear();
            foreach (var entry in entries) _entries.Add(new FileRow(entry));
            PathText.Text = _currentPath;
            return LocalizationService.Current.Format("EntriesFound", entries.Count);
        });
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async void Up_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPath == StorageRoot || _busy) return;
        var separator = _currentPath.LastIndexOf('/');
        _currentPath = separator <= StorageRoot.Length ? StorageRoot : _currentPath[..separator];
        await RefreshAsync();
    }

    private async void QuickFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string folder } || _busy) return;
        _currentPath = $"{StorageRoot}/{folder}";
        await RefreshAsync();
    }

    private async void Entries_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Selected?.Entry.IsDirectory != true || _busy) return;
        _currentPath = Selected.Entry.FullPath;
        await RefreshAsync();
    }

    private async void Upload_Click(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFileDialog { Title = LocalizationService.Current["UploadFile"], Multiselect = false };
        if (picker.ShowDialog(this) != true) return;
        await RunAndRefreshAsync(async () =>
        {
            await _files.UploadAsync(_device.Serial, picker.FileName, _currentPath);
            return LocalizationService.Current["FileUploaded"];
        });
    }

    private async void Download_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is null || Selected.Entry.IsDirectory) return;
        var picker = new SaveFileDialog { Title = LocalizationService.Current["DownloadFile"], FileName = Selected.Entry.Name };
        if (picker.ShowDialog(this) != true) return;
        await RunAsync(async () =>
        {
            await _files.DownloadAsync(_device.Serial, Selected.Entry.FullPath, picker.FileName);
            return LocalizationService.Current["FileDownloaded"];
        });
    }

    private async void NewFolder_Click(object sender, RoutedEventArgs e)
    {
        var prompt = new TextPromptWindow(LocalizationService.Current["FolderNamePrompt"]) { Owner = this };
        if (prompt.ShowDialog() != true) return;
        await RunAndRefreshAsync(async () =>
        {
            await _files.CreateDirectoryAsync(_device.Serial, _currentPath, prompt.Value);
            return LocalizationService.Current["FolderCreated"];
        });
    }

    private async void Rename_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is null) return;
        var selected = Selected;
        var prompt = new TextPromptWindow(LocalizationService.Current["NewNamePrompt"], selected.Entry.Name) { Owner = this };
        if (prompt.ShowDialog() != true) return;
        await RunAndRefreshAsync(async () =>
        {
            await _files.RenameAsync(_device.Serial, selected.Entry.FullPath, prompt.Value);
            return LocalizationService.Current["EntryRenamed"];
        });
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is null) return;
        var selected = Selected;
        if (!_confirmation.Confirm(LocalizationService.Current["Delete"], LocalizationService.Current.Format("ConfirmDelete", selected.Entry.Name))) return;
        await RunAndRefreshAsync(async () =>
        {
            await _files.DeleteAsync(_device.Serial, selected.Entry.FullPath);
            return LocalizationService.Current["EntryDeleted"];
        });
    }

    private async Task RunAndRefreshAsync(Func<Task<string>> action)
    {
        var completed = false;
        await RunAsync(async () => { var message = await action(); completed = true; return message; });
        if (completed) await RefreshAsync();
    }

    private async Task RunAsync(Func<Task<string>> action)
    {
        if (_busy) return;
        _busy = true;
        IsEnabled = false;
        StatusText.Text = LocalizationService.Current["Working"];
        try
        {
            StatusText.Text = await action();
            StatusText.Foreground = (System.Windows.Media.Brush)FindResource("SuccessBrush");
        }
        catch (Exception exception)
        {
            StatusText.Text = LocalizationService.Current.Format("FileOperationFailed", exception.Message);
            StatusText.Foreground = (System.Windows.Media.Brush)FindResource("DangerBrush");
        }
        finally
        {
            IsEnabled = true;
            _busy = false;
        }
    }

    private sealed record FileRow(RemoteFileEntry Entry)
    {
        public string Icon => Entry.IsDirectory ? "📁" : "📄";
    }
}
