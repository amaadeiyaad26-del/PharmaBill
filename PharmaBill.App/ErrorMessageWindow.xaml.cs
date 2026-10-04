using System.Diagnostics;
using System.IO;
using System.Windows;

namespace PharmaBill.App;

public partial class ErrorMessageWindow : Window
{
    private readonly string _logDirectory;

    public ErrorMessageWindow(string message, string details, string logDirectory)
    {
        _logDirectory = logDirectory;
        InitializeComponent();
        FriendlyMessage.Text = message;
        ErrorDetails.Text = details;
        DataContext = new { LogPath = logDirectory };
    }

    private void OpenLogFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(_logDirectory);
            Process.Start(new ProcessStartInfo("explorer.exe", _logDirectory) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                $"Could not open the log folder: {exception.Message}{Environment.NewLine}{_logDirectory}",
                "PharmaBill",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}
