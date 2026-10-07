using System.Windows;

namespace PharmaBill.App.Services;

public sealed class PaymentQrDialogService : IPaymentQrDialogService
{
	public PaymentQrDialogResult Show(PaymentQrDialogRequest request)
	{
		Window owner = Application.Current?.MainWindow;
		PaymentQrWindow paymentQrWindow = new PaymentQrWindow(request)
		{
			Owner = owner
		};
		Window window = null;
		try
		{
			if (request.ShowOnCustomerDisplay && request.QrImage != null)
			{
				TryOpenCustomerDisplay(request, out window);
			}
			return new PaymentQrDialogResult((paymentQrWindow.ShowDialog() == true) ? ((!paymentQrWindow.ConfigureUpiRequested) ? PaymentQrDialogOutcome.Confirmed : PaymentQrDialogOutcome.ConfigureUpi) : PaymentQrDialogOutcome.Cancelled);
		}
		finally
		{
			window?.Close();
		}
	}

	private static bool TryOpenCustomerDisplay(PaymentQrDialogRequest request, out Window? window)
	{
		window = null;
		double virtualScreenWidth = SystemParameters.VirtualScreenWidth;
		double primaryScreenWidth = SystemParameters.PrimaryScreenWidth;
		if (virtualScreenWidth <= primaryScreenWidth + 80.0)
		{
			return false;
		}
		double left = SystemParameters.VirtualScreenLeft + primaryScreenWidth + 40.0;
		double top = SystemParameters.VirtualScreenTop + 40.0;
		window = new PaymentQrCustomerWindow(request)
		{
			WindowStartupLocation = WindowStartupLocation.Manual,
			Left = left,
			Top = top,
			Width = 480.0,
			Height = 620.0
		};
		window.Show();
		return true;
	}
}
