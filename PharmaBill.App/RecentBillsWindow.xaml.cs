using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using PharmaBill.App.Services;
using PharmaBill.App.ViewModels;
using PharmaBill.Data.Services;

namespace PharmaBill.App;

public partial class RecentBillsWindow : Window
{
	private readonly RecentBillsViewModel _viewModel;

	public RecentBillsWindow(RecentBillsViewModel viewModel)
	{
		_viewModel = viewModel;
		InitializeComponent();
		DataContext = viewModel;
		Loaded += async (object _, RoutedEventArgs _) =>
		{
			await _viewModel.LoadAsync();
		};
	}

	private async void Refresh_Click(object sender, RoutedEventArgs e)
	{
		await _viewModel.LoadAsync();
	}

	private async void Bills_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		await _viewModel.LoadReturnableLinesAsync();
	}

	private async void SavePdf_Click(object sender, RoutedEventArgs e)
	{
		SaveFileDialog dialog = new SaveFileDialog
		{
			Title = "Save or share bill PDF",
			Filter = "PDF files (*.pdf)|*.pdf",
			DefaultExt = ".pdf",
			FileName = (_viewModel.SelectedBill?.InvoiceNo ?? "retail-bill") + ".pdf",
			AddExtension = true
		};
		if (dialog.ShowDialog(this) != true)
		{
			return;
		}
		try
		{
			await _viewModel.ExportPdfAsync(dialog.FileName);
			_viewModel.StatusMessage = "Bill PDF saved to " + dialog.FileName + ".";
		}
		catch (Exception ex)
		{
			_viewModel.ErrorMessage = ex.Message;
		}
	}

	private async void Print_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			await _viewModel.PrintSelectedAsync();
			_viewModel.StatusMessage = "Bill sent to the configured document printer.";
		}
		catch (Exception ex)
		{
			_viewModel.ErrorMessage = ex.Message;
		}
	}

	private async void Preview_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			await _viewModel.PreviewSelectedAsync();
		}
		catch (Exception ex)
		{
			_viewModel.ErrorMessage = ex.Message;
		}
	}

	private async void WhatsApp_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			await _viewModel.ShareSelectedToWhatsAppAsync();
			_viewModel.StatusMessage = "WhatsApp opened. Attach the saved PDF shown in Explorer; files cannot be attached through the link.";
		}
		catch (Exception ex)
		{
			_viewModel.ErrorMessage = ex.Message;
		}
	}

	private async void Email_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			await _viewModel.EmailSelectedAsync();
			_viewModel.StatusMessage = "Email message opened or sent with the bill PDF.";
		}
		catch (Exception ex)
		{
			_viewModel.ErrorMessage = ex.Message;
		}
	}

	private async void CopyPdfPath_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			RecentRetailBill recentRetailBill = _viewModel.SelectedBill ?? throw new InvalidOperationException("Select a bill first.");
			string path = RetailBillPdfService.GetSharedPdfPath(recentRetailBill.SaleId, _viewModel.DocumentType);
			Directory.CreateDirectory(Path.GetDirectoryName(path));
			await _viewModel.ExportPdfAsync(path);
			Clipboard.SetText(path);
			_viewModel.StatusMessage = "PDF path copied: " + path;
		}
		catch (Exception ex)
		{
			_viewModel.ErrorMessage = ex.Message;
		}
	}

	private async void ShowPdf_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			RecentRetailBill recentRetailBill = _viewModel.SelectedBill ?? throw new InvalidOperationException("Select a bill first.");
			string path = RetailBillPdfService.GetSharedPdfPath(recentRetailBill.SaleId, _viewModel.DocumentType);
			Directory.CreateDirectory(Path.GetDirectoryName(path));
			await _viewModel.ExportPdfAsync(path);
			RetailBillPdfService.ShowInExplorer(path);
		}
		catch (Exception ex)
		{
			_viewModel.ErrorMessage = ex.Message;
		}
	}
}
