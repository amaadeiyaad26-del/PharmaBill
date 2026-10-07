using System.Windows;
using PharmaBill.App.Services;

namespace PharmaBill.App;

public partial class PaymentQrWindow : Window
{
	private readonly PaymentQrDialogRequest _request;

	public bool ConfigureUpiRequested { get; private set; }

	public PaymentQrWindow(PaymentQrDialogRequest request)
	{
		_request = request;
		InitializeComponent();
		PharmacyNameText.Text = request.PharmacyName;
		AmountText.Text = $"₹{request.Amount:N2}";
		InvoiceText.Text = (string.IsNullOrWhiteSpace(request.InvoiceNo) ? "Invoice: (new bill)" : ("Invoice: " + request.InvoiceNo));
		UpiIdText.Text = (string.IsNullOrWhiteSpace(request.UpiId) ? "(not set)" : request.UpiId);
		QrImage.Source = request.QrImage;
		bool flag = string.IsNullOrWhiteSpace(request.UpiId) || request.QrImage == null;
		MissingUpiText.Visibility = ((!flag) ? Visibility.Collapsed : Visibility.Visible);
		ConfirmButton.Visibility = (flag ? Visibility.Collapsed : Visibility.Visible);
		ConfigureButton.Visibility = ((!flag) ? Visibility.Collapsed : Visibility.Visible);
	}

	private void CopyUpi_Click(object sender, RoutedEventArgs e)
	{
		if (!string.IsNullOrWhiteSpace(_request.UpiId))
		{
			Clipboard.SetText(_request.UpiId.Trim());
		}
	}

	private void Confirm_Click(object sender, RoutedEventArgs e)
	{
		DialogResult = true;
		Close();
	}

	private void Cancel_Click(object sender, RoutedEventArgs e)
	{
		DialogResult = false;
		Close();
	}

	private void Configure_Click(object sender, RoutedEventArgs e)
	{
		ConfigureUpiRequested = true;
		DialogResult = true;
		Close();
	}
}
