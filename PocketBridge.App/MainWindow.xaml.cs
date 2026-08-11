using System.IO;
using System.Windows;
using System.Windows.Input;
using PocketBridge.Core.Services;
using PocketBridge.App.ViewModels;

namespace PocketBridge.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
        Closed += (_, _) => _viewModel.Dispose();
    }

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        var files = e.Data.GetData(DataFormats.FileDrop) as string[];
        if (files is { Length: > 0 }) await _viewModel.HandleDroppedFilesAsync(files);
        e.Handled = true;
    }

    private async void DeviceCard_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop) || (sender as FrameworkElement)?.DataContext is not DeviceItemViewModel target) return;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files) await _viewModel.HandleDroppedFilesAsync(files, target);
        e.Handled = true;
    }

    private void MultiViewTile_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount < 2 || (sender as FrameworkElement)?.DataContext is not IEmbeddedDisplaySession session) return;
        _viewModel.FocusSession(session.Serial);
        e.Handled = true;
    }
}
