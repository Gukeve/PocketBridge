using System.Windows;
using System.Windows.Controls;
using PocketBridge.Core.Services;

namespace PocketBridge.App;

public partial class AuditLogWindow : Window
{
    private readonly IAuditLogService _audit;
    public AuditLogWindow(IAuditLogService audit) { _audit = audit; InitializeComponent(); _audit.Changed += Audit_Changed; Closed += (_, _) => _audit.Changed -= Audit_Changed; Refresh(); }
    private void Audit_Changed(object? sender, EventArgs e) => Dispatcher.Invoke(Refresh);
    private void Filter_Changed(object sender, EventArgs e) { if (IsLoaded) Refresh(); }
    private void Refresh() { var actor = ActorFilter?.Text.Trim() ?? string.Empty; var device = DeviceFilter?.Text.Trim() ?? string.Empty; var result = (ResultFilter?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All results"; RecordsGrid.ItemsSource = _audit.Items.Where(item => item.Actor.Contains(actor, StringComparison.OrdinalIgnoreCase) && item.DeviceAlias.Contains(device, StringComparison.OrdinalIgnoreCase) && (result == "All results" || item.Result.Contains(result, StringComparison.OrdinalIgnoreCase))).ToArray(); }
    private void Clear_Click(object sender, RoutedEventArgs e) { if (MessageBox.Show("Clear the local audit history?", "Audit log", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes) _audit.Clear(); }
}
