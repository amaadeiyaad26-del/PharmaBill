using System.Windows;
using System.Windows.Input;
using PharmaBill.App.ViewModels;

namespace PharmaBill.App;

public partial class PurchaseInvoiceHistoryWindow : Window
{
	private readonly PurchaseInvoiceHistoryViewModel _viewModel;

	public PurchaseInvoiceHistoryWindow(PurchaseInvoiceHistoryViewModel viewModel)
	{
		InitializeComponent();
		_viewModel = viewModel;
		DataContext = viewModel;
		Loaded += async (_, _) => await _viewModel.LoadAsync();
	}

	private async void InvoicesGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
	{
		if (InvoicesGrid.SelectedItem is PurchaseInvoiceHistoryRow row)
		{
			await _viewModel.LoadDetailsAsync(row);
		}
	}
}
