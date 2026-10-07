using System.Windows;
using System.Windows.Markup;
using PharmaBill.Data.Services;

namespace PharmaBill.App;

public partial class InvoiceDetailWindow : Window, IComponentConnector
{
	public InvoiceDetailWindow(InvoiceDetail detail)
	{
		InitializeComponent();
		DataContext = detail;
	}
}
