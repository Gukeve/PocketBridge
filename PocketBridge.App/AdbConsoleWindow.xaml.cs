using System.Windows;
using System.Windows.Input;
using PocketBridge.App.Localization;
using PocketBridge.Core.Models;
using PocketBridge.Core.Services;

namespace PocketBridge.App;
public partial class AdbConsoleWindow : Window
{
    private readonly AndroidDevice _device; private readonly IAdbConsoleService _console; private readonly List<string> _history = new(); private int _historyIndex; private CancellationTokenSource? _running;
    public AdbConsoleWindow(AndroidDevice device, IAdbConsoleService console) { _device = device; _console = console; InitializeComponent(); TargetText.Text = LocalizationService.Current.Format("AdbConsoleTarget", device.FriendlyName, device.Serial); }
    private async void Run_Click(object sender, RoutedEventArgs e) => await RunAsync();
    private async Task RunAsync() { if (_running is not null || string.IsNullOrWhiteSpace(CommandBox.Text)) return; var command = CommandBox.Text.Trim(); _history.Add(command); _historyIndex = _history.Count; Append($"> adb -s {_device.Serial} {command}", false); _running = new CancellationTokenSource(); try { var exit = await _console.ExecuteAsync(_device.Serial, command, (line, error) => Dispatcher.Invoke(() => Append(line, error)), _running.Token); Append($"[exit {exit}]", exit != 0); } catch (OperationCanceledException) { Append(LocalizationService.Current["CommandCancelled"], true); } catch (Exception ex) { Append(ex.Message, true); } finally { _running.Dispose(); _running = null; } }
    private void Append(string text, bool error) { var prefix = TimestampBox.IsChecked == true ? $"[{DateTime.Now:HH:mm:ss}] " : string.Empty; OutputBox.AppendText(prefix + (error ? "! " : string.Empty) + text + Environment.NewLine); OutputBox.ScrollToEnd(); }
    private void CommandBox_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { _ = RunAsync(); e.Handled = true; } else if (e.Key == Key.Up) { Navigate(-1); e.Handled = true; } else if (e.Key == Key.Down) { Navigate(1); e.Handled = true; } }
    private void Window_PreviewKeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Escape && _running is not null) { _running.Cancel(); e.Handled = true; } }
    private void Navigate(int delta) { if (_history.Count == 0) return; _historyIndex = Math.Clamp(_historyIndex + delta, 0, _history.Count); CommandBox.Text = _historyIndex == _history.Count ? string.Empty : _history[_historyIndex]; CommandBox.CaretIndex = CommandBox.Text.Length; }
    private void Cancel_Click(object sender, RoutedEventArgs e) => _running?.Cancel(); private void Clear_Click(object sender, RoutedEventArgs e) => OutputBox.Clear(); private void Copy_Click(object sender, RoutedEventArgs e) { if (OutputBox.Text.Length > 0) Clipboard.SetText(OutputBox.Text); }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
