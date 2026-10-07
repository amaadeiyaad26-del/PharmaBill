using System.Windows;
using System.Windows.Markup;
using PharmaBill.App.Services;

namespace PharmaBill.App;

public partial class PaymentQrCustomerWindow : Window, IComponentConnector
{
	public PaymentQrCustomerWindow(PaymentQrDialogRequest request)
	{
		InitializeComponent();
		PharmacyNameText.Text = request.PharmacyName;
		AmountText.Text = $"₹{request.Amount:N2}";
		InvoiceText.Text = (string.IsNullOrWhiteSpace(request.InvoiceNo) ? string.Empty : ("Invoice " + request.InvoiceNo));
		QrImage.Source = request.QrImage;
	}
}
