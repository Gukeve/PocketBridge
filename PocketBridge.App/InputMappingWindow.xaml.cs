using System.Collections.ObjectModel;
using System.Windows;
using Microsoft.Win32;
using PocketBridge.Core.Models;
using PocketBridge.Core.Services;

namespace PocketBridge.App;

public partial class InputMappingWindow : Window
{
    private readonly IInputMappingService _mappings; private readonly ObservableCollection<InputProfile> _profiles = new(); private readonly ObservableCollection<BindingRow> _bindings = new(); private Guid _id;
    public static Array SourceKinds { get; } = Enum.GetValues<InputSourceKind>(); public static Array ActionKinds { get; } = Enum.GetValues<InputActionKind>();
    public InputMappingWindow(IInputMappingService mappings) { _mappings = mappings; InitializeComponent(); ProfilesBox.ItemsSource = _profiles; BindingsGrid.ItemsSource = _bindings; Reload(); }
    private void Reload(Guid? select = null) { _profiles.Clear(); foreach (var item in _mappings.Profiles) _profiles.Add(item); ProfilesBox.SelectedItem = _profiles.FirstOrDefault(item => item.Id == select) ?? _profiles.FirstOrDefault(); }
    private void Profile_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e) { if (ProfilesBox.SelectedItem is not InputProfile profile) return; _id = profile.Id; NameBox.Text = profile.Name; TargetBox.Text = profile.TargetAlias ?? string.Empty; _bindings.Clear(); foreach (var item in profile.Bindings) _bindings.Add(BindingRow.From(item)); }
    private void New_Click(object sender, RoutedEventArgs e) { _id = Guid.NewGuid(); NameBox.Text = "Custom"; TargetBox.Clear(); _bindings.Clear(); }
    private void Duplicate_Click(object sender, RoutedEventArgs e) { _id = Guid.NewGuid(); NameBox.Text += " copy"; }
    private async void Delete_Click(object sender, RoutedEventArgs e) { if (_id != Guid.Empty) await _mappings.DeleteAsync(_id); Reload(); }
    private async void Save_Click(object sender, RoutedEventArgs e) { var profile = new InputProfile(_id == Guid.Empty ? Guid.NewGuid() : _id, string.IsNullOrWhiteSpace(NameBox.Text) ? "Custom" : NameBox.Text.Trim(), _bindings.Where(item => !string.IsNullOrWhiteSpace(item.Input)).Select(item => item.ToBinding()).ToArray(), string.IsNullOrWhiteSpace(TargetBox.Text) ? null : TargetBox.Text.Trim()); await _mappings.SaveAsync(profile); Reload(profile.Id); }
    private async void Export_Click(object sender, RoutedEventArgs e) { var picker = new SaveFileDialog { Filter = "PocketBridge input profile (*.pbinput.json)|*.pbinput.json" }; if (picker.ShowDialog(this) == true) await _mappings.ExportAsync(_id, picker.FileName); }
    private async void Import_Click(object sender, RoutedEventArgs e) { var picker = new OpenFileDialog { Filter = "PocketBridge input profile (*.pbinput.json)|*.pbinput.json" }; if (picker.ShowDialog(this) == true) { var profile = await _mappings.ImportAsync(picker.FileName); Reload(profile.Id); } }
    public sealed class BindingRow { public InputSourceKind SourceKind { get; set; } public string Input { get; set; } = string.Empty; public InputActionKind ActionKind { get; set; } public string Value { get; set; } = string.Empty; public double X { get; set; } = .5; public double Y { get; set; } = .5; public double DeadZone { get; set; } = .18; public KeyBinding ToBinding() => new(SourceKind, Input, new(ActionKind, Value, ActionKind is InputActionKind.TouchPoint or InputActionKind.VirtualJoystick ? new(X, Y) : null), DeadZone); public static BindingRow From(KeyBinding value) => new() { SourceKind = value.SourceKind, Input = value.Input, ActionKind = value.Action.Kind, Value = value.Action.Value, X = value.Action.Point?.X ?? .5, Y = value.Action.Point?.Y ?? .5, DeadZone = value.DeadZone }; }
}
