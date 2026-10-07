using System.Windows;
using PharmaBill.Data.Services;

namespace PharmaBill.App.Services;

public sealed class InvoiceDetailDialogService : IInvoiceDetailDialogService
{
	public void Show(InvoiceDetail detail)
	{
		InvoiceDetailWindow invoiceDetailWindow = new InvoiceDetailWindow(detail);
		invoiceDetailWindow.Owner = Application.Current?.MainWindow;
		invoiceDetailWindow.ShowDialog();
	}
}
