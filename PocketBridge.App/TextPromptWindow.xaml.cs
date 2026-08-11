using System.Windows;
namespace PocketBridge.App;
public partial class TextPromptWindow : Window
{
    public TextPromptWindow(string prompt, string initial = "") { InitializeComponent(); PromptText.Text = prompt; ValueBox.Text = initial; Loaded += (_, _) => { ValueBox.Focus(); ValueBox.SelectAll(); }; }
    public string Value => ValueBox.Text.Trim();
    private void Accept_Click(object sender, RoutedEventArgs e) { if (Value.Length > 0) DialogResult = true; }
}
