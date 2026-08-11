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
        var apk = files?.FirstOrDefault(path => Path.GetExtension(path).Equals(".apk", StringComparison.OrdinalIgnoreCase));
        if (apk is not null) await _viewModel.InstallDroppedApkAsync(apk);
    }

    private void MultiViewTile_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount < 2 || (sender as FrameworkElement)?.DataContext is not IEmbeddedDisplaySession session) return;
        _viewModel.FocusSession(session.Serial);
        e.Handled = true;
    }
}
