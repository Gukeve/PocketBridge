using System.Diagnostics;
using System.Windows;
using System.Windows.Media.Imaging;

namespace PocketBridge.App;

public partial class MediaResultWindow : Window
{
    private readonly string _filePath;
    public MediaResultWindow(string filePath, bool canCopy)
    {
        _filePath = filePath;
        InitializeComponent();
        PathText.Text = filePath;
        CopyButton.Visibility = canCopy ? Visibility.Visible : Visibility.Collapsed;
    }
    private void Open_Click(object sender, RoutedEventArgs e) => Process.Start(new ProcessStartInfo(_filePath) { UseShellExecute = true });
    private void Explorer_Click(object sender, RoutedEventArgs e) => Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { $"/select,{_filePath}" }, UseShellExecute = false });
    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.UriSource = new Uri(_filePath); image.EndInit(); image.Freeze(); Clipboard.SetImage(image);
    }
}
