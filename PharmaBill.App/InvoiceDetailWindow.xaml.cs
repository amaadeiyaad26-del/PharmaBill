using System.Windows;
using PharmaBill.Data.Services;

namespace PharmaBill.App;

public partial class InvoiceDetailWindow : Window
{
	public InvoiceDetailWindow(InvoiceDetail detail)
	{
		InitializeComponent();
		DataContext = detail;
	}
}
