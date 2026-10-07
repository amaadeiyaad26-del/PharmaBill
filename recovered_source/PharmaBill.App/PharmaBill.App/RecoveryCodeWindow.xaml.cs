using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using Microsoft.Win32;

namespace PharmaBill.App;

public partial class RecoveryCodeWindow : Window, IComponentConnector
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
		SaveFileDialog saveFileDialog = new SaveFileDialog
		{
			Title = "Save owner recovery code",
			FileName = "PharmaBill-Recovery-Code.txt",
			Filter = "Text files (*.txt)|*.txt",
			AddExtension = true,
			DefaultExt = ".txt"
		};
		if (saveFileDialog.ShowDialog(this) == true)
		{
			File.WriteAllText(saveFileDialog.FileName, "PharmaBill owner recovery code" + Environment.NewLine + _code + Environment.NewLine + "Store this code securely. Anyone with it can reset the owner PIN.");
			MessageBox.Show(this, "Recovery code saved. Store the file somewhere secure.", "PharmaBill", MessageBoxButton.OK, MessageBoxImage.Asterisk);
		}
	}

	private void Print_Click(object sender, RoutedEventArgs e)
	{
		PrintDialog printDialog = new PrintDialog();
		if (printDialog.ShowDialog() == true)
		{
			FlowDocument flowDocument = new FlowDocument(new Paragraph(new Run($"PharmaBill owner recovery code{Environment.NewLine}{Environment.NewLine}{_code}{Environment.NewLine}{Environment.NewLine}Store this code securely. Anyone with it can reset the owner PIN.")))
			{
				PagePadding = new Thickness(48.0)
			};
			printDialog.PrintDocument(((IDocumentPaginatorSource)flowDocument).DocumentPaginator, "PharmaBill recovery code");
		}
	}
}
