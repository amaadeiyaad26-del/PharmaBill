using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using Microsoft.Win32;

namespace PharmaBill.App;

public partial class RecoveryCodeWindow : Window
{
    private readonly string _code;

    public RecoveryCodeWindow(string code)
    {
        _code = code;
        InitializeComponent();
        RecoveryCodeText.Text = code;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Save owner recovery code",
            FileName = "PharmaBill-Recovery-Code.txt",
            Filter = "Text files (*.txt)|*.txt",
            AddExtension = true,
            DefaultExt = ".txt"
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        File.WriteAllText(dialog.FileName,
            $"PharmaBill owner recovery code{Environment.NewLine}{_code}{Environment.NewLine}" +
            "Store this code securely. Anyone with it can reset the owner PIN.");
        MessageBox.Show(this, "Recovery code saved. Store the file somewhere secure.",
            "PharmaBill", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void Print_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new PrintDialog();
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var document = new FlowDocument(new Paragraph(new Run(
            $"PharmaBill owner recovery code{Environment.NewLine}{Environment.NewLine}{_code}" +
            $"{Environment.NewLine}{Environment.NewLine}Store this code securely. Anyone with it can reset the owner PIN.")))
        {
            PagePadding = new Thickness(48)
        };
        dialog.PrintDocument(((IDocumentPaginatorSource)document).DocumentPaginator, "PharmaBill recovery code");
    }
}
