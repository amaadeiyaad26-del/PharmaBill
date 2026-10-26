using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using PharmaBill.App.Services;
using PharmaBill.Data.Services;

namespace PharmaBill.App;

public partial class InvoiceDetailWindow : Window
{
	private readonly InvoiceDetail _detail;
	private readonly InvoiceDocumentService _documents;

	public InvoiceDetailWindow(InvoiceDetail detail, InvoiceDocumentService documents)
	{
		InitializeComponent();
		_detail = detail;
		_documents = documents;
		DataContext = detail;
	}

	/// <summary>Asks where to save the PDF; null when the user cancels. Replaceable so a test can answer it.</summary>
	public Func<string, string?> PickPdfDestination { get; set; } = AskWhereToSave;

	/// <summary>Shows the Windows print dialog and prints the pages; false when the user cancels. Replaceable for tests.</summary>
	public Func<IReadOnlyList<BitmapSource>, string, bool> PrintPages { get; set; } = PrintWithDialog;

	private async void Print_Click(object sender, RoutedEventArgs e) => await RunAsync(PrintButton, "Preparing the invoice…", async () =>
	{
		IReadOnlyList<BitmapSource> pages = await _documents.RenderPagesAsync(_detail);
		return PrintPages(pages, InvoiceDocumentService.FileName(_detail)) ? "Sent to the printer." : string.Empty;
	});

	private async void SavePdf_Click(object sender, RoutedEventArgs e) => await RunAsync(SavePdfButton, "Saving…", async () =>
	{
		string? destination = PickPdfDestination(InvoiceDocumentService.FileName(_detail));
		if (destination == null)
		{
			return string.Empty;
		}
		await _documents.WritePdfAsync(_detail, destination);
		return "Saved: " + destination;
	});

	private async void Export_Click(object sender, RoutedEventArgs e) => await RunAsync(ExportButton, "Saving to Downloads…", async () =>
		"Saved to Downloads: " + Path.GetFileName(await _documents.ExportToDownloadsAsync(_detail)));

	private async Task RunAsync(Button button, string working, Func<Task<string>> action)
	{
		ActionStatus.Foreground = (Brush)FindResource("SecondaryTextBrush");
		ActionStatus.Text = working;
		button.IsEnabled = false;
		try
		{
			ActionStatus.Text = await action();
		}
		catch (Exception exception)
		{
			ActionStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xB4, 0x23, 0x18));
			ActionStatus.Text = PharmaBill.Core.UserMessage.Of(exception);
		}
		finally
		{
			button.IsEnabled = true;
		}
	}

	private static string? AskWhereToSave(string fileName)
	{
		var dialog = new SaveFileDialog
		{
			Title = "Save invoice as PDF",
			Filter = "PDF files (*.pdf)|*.pdf",
			DefaultExt = ".pdf",
			AddExtension = true,
			OverwritePrompt = true,
			FileName = fileName,
			InitialDirectory = FolderBackupService.DownloadsFolder() ?? string.Empty
		};
		return dialog.ShowDialog() == true ? dialog.FileName : null;
	}

	// Each PDF page is placed on a printer page, scaled to the printable area and centred, so the paper matches the PDF.
	private static bool PrintWithDialog(IReadOnlyList<BitmapSource> pages, string title)
	{
		var dialog = new PrintDialog { UserPageRangeEnabled = false };
		if (dialog.ShowDialog() != true)
		{
			return false;
		}
		var document = new FixedDocument();
		var size = new Size(dialog.PrintableAreaWidth, dialog.PrintableAreaHeight);
		document.DocumentPaginator.PageSize = size;
		foreach (BitmapSource page in pages)
		{
			var fixedPage = new FixedPage { Width = size.Width, Height = size.Height };
			var image = new Image { Source = page, Stretch = Stretch.Uniform, Width = size.Width, Height = size.Height };
			fixedPage.Children.Add(image);
			fixedPage.Measure(size);
			fixedPage.Arrange(new Rect(size));
			var content = new PageContent();
			((System.Windows.Markup.IAddChild)content).AddChild(fixedPage);
			document.Pages.Add(content);
		}
		dialog.PrintDocument(document.DocumentPaginator, title);
		return true;
	}
}
