using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using PocketBridge.Core.Services;
using PocketBridge.Core.Models;
using PocketBridge.App.ViewModels;

namespace PocketBridge.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly DispatcherTimer _overlayTimer;
    private bool _fullscreen;
    private WindowStyle _previousStyle;
    private WindowState _previousState;

    public MainWindow(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
        _overlayTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _overlayTimer.Tick += (_, _) => FadeOverlay(0);
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

    private void GroupTarget_Changed(object sender, RoutedEventArgs e) => _viewModel.GroupSelectionChanged();
    private async void MappingProfile_Edited(object sender, PocketBridge.Core.Models.InputProfile profile) => await _viewModel.SaveMappingProfileAsync(profile);

    private void Fullscreen_Click(object sender, RoutedEventArgs e) => ToggleFullscreen();

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _fullscreen) { ToggleFullscreen(); e.Handled = true; }
        else
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            var modifiers = Keyboard.Modifiers;
            var gesture = ShortcutGestureFormatter.Format(key.ToString(),
                modifiers.HasFlag(ModifierKeys.Control), modifiers.HasFlag(ModifierKeys.Shift),
                modifiers.HasFlag(ModifierKeys.Alt), modifiers.HasFlag(ModifierKeys.Windows));
            if (gesture is null) return;
            var action = _viewModel.MatchShortcut(gesture);
            if (action == PocketBridge.Core.Models.ShortcutAction.ToggleFullscreen) ToggleFullscreen();
            else if (action is not null) _viewModel.ExecuteShortcut(action.Value);
            e.Handled = action is not null;
        }
    }

    private void Window_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_fullscreen) return;
        FadeOverlay(1); _overlayTimer.Stop(); _overlayTimer.Start();
    }

    private void ToggleFullscreen()
    {
        if (!_fullscreen && _viewModel.SelectedEmbeddedSession is not { IsRunning: true }) return;
        _fullscreen = !_fullscreen;
        if (_fullscreen)
        {
            _previousStyle = WindowStyle; _previousState = WindowState;
            WindowStyle = WindowStyle.None; WindowState = WindowState.Maximized;
        }
        else { WindowStyle = _previousStyle; WindowState = _previousState; }
        HeaderPanel.Visibility = StatusPanel.Visibility = DeviceListPanel.Visibility = DeviceOverviewPanel.Visibility = SessionButtonsPanel.Visibility = ControlPanel.Visibility = PreviewHeader.Visibility = ExternalPreviewButton.Visibility = _fullscreen ? Visibility.Collapsed : Visibility.Visible;
        RootGrid.RowDefinitions[0].Height = _fullscreen ? new GridLength(0) : new GridLength(100);
        RootGrid.RowDefinitions[2].Height = _fullscreen ? new GridLength(0) : new GridLength(54);
        MainContentGrid.Margin = _fullscreen ? new Thickness(0) : new Thickness(24, 8, 24, 12);
        MainContentGrid.ColumnDefinitions[0].Width = _fullscreen ? new GridLength(0) : new GridLength(300);
        MainContentGrid.ColumnDefinitions[1].Width = _fullscreen ? new GridLength(0) : new GridLength(12);
        SingleDeviceGrid.RowDefinitions[0].Height = _fullscreen ? new GridLength(0) : GridLength.Auto;
        SingleDeviceGrid.RowDefinitions[1].Height = _fullscreen ? new GridLength(0) : new GridLength(16);
        SingleDeviceGrid.RowDefinitions[2].Height = _fullscreen ? new GridLength(0) : GridLength.Auto;
        SingleDeviceGrid.RowDefinitions[3].Height = _fullscreen ? new GridLength(0) : new GridLength(16);
        ScreenGrid.ColumnDefinitions[0].Width = _fullscreen ? new GridLength(0) : new GridLength(270);
        ScreenGrid.ColumnDefinitions[1].Width = _fullscreen ? new GridLength(0) : new GridLength(10);
        MainPanel.Padding = PreviewPanel.Padding = _fullscreen ? new Thickness(0) : new Thickness(16);
        PreviewPanel.CornerRadius = _fullscreen ? new CornerRadius(0) : new CornerRadius(14);
        FullscreenOverlay.Opacity = 1;
        if (_fullscreen) _overlayTimer.Start(); else _overlayTimer.Stop();
    }

    private void FadeOverlay(double opacity) => FullscreenOverlay.BeginAnimation(OpacityProperty, new DoubleAnimation(opacity, TimeSpan.FromMilliseconds(160)));
}
