using System.Windows;
using System.Windows.Controls;
using System.Text.Json;
using System.IO;
using Microsoft.Win32;
using PocketBridge.Core.Models;
using PocketBridge.Core.Services;

namespace PocketBridge.App;

public partial class AuditLogWindow : Window
{
    private readonly IAuditLogService _audit;
    private IReadOnlyList<AuditRecord> _visible = Array.Empty<AuditRecord>();
    public AuditLogWindow(IAuditLogService audit) { _audit = audit; InitializeComponent(); _audit.Changed += Audit_Changed; Closed += (_, _) => _audit.Changed -= Audit_Changed; Refresh(); }
    private void Audit_Changed(object? sender, EventArgs e) => Dispatcher.Invoke(Refresh);
    private void Filter_Changed(object sender, EventArgs e) { if (IsLoaded) Refresh(); }
    private void Refresh() { var actor = ActorFilter?.Text.Trim() ?? string.Empty; var device = DeviceFilter?.Text.Trim() ?? string.Empty; var category = CategoryFilter?.Text.Trim() ?? string.Empty; var severityIndex = SeverityFilter?.SelectedIndex ?? 0; var resultIndex = ResultFilter?.SelectedIndex ?? 0; var outcomes = new[] { "", "Success", "Accepted", "Denied", "Failed" }; var from = FromDate?.SelectedDate; _visible = _audit.Items.Where(item => item.Actor.Contains(actor, StringComparison.OrdinalIgnoreCase) && item.DeviceAlias.Contains(device, StringComparison.OrdinalIgnoreCase) && item.Category.Contains(category, StringComparison.OrdinalIgnoreCase) && (severityIndex == 0 || (int)item.Severity == severityIndex - 1) && (resultIndex == 0 || item.Result.Contains(outcomes[resultIndex], StringComparison.OrdinalIgnoreCase)) && (from is null || item.Timestamp.LocalDateTime.Date >= from.Value.Date)).ToArray(); RecordsGrid.ItemsSource = _visible; }
    private void Clear_Click(object sender, RoutedEventArgs e) { if (MessageBox.Show(Localization.LocalizationService.Current["P3_ClearAuditConfirm"], Localization.LocalizationService.Current["P3_AuditLog"], MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes) _audit.Clear(); }
    private async void Export_Click(object sender, RoutedEventArgs e) { var picker = new SaveFileDialog { Filter = "JSON (*.json)|*.json", FileName = $"PocketBridge-audit-redacted-{DateTime.Now:yyyyMMdd}.json" }; if (picker.ShowDialog(this) == true) await File.WriteAllTextAsync(picker.FileName, JsonSerializer.Serialize(_visible, new JsonSerializerOptions { WriteIndented = true })); }
}
