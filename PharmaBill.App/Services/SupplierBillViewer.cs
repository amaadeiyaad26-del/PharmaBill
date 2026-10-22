using System.Windows;
using PharmaBill.Data.Services;

namespace PharmaBill.App.Services;

public interface ISupplierBillViewer
{
	/// <summary>Shows an attached supplier bill whose file exists at <paramref name="path"/>.</summary>
	void Show(PurchaseBillAttachment bill, string path);
}

public sealed class SupplierBillViewer : ISupplierBillViewer
{
	public void Show(PurchaseBillAttachment bill, string path)
	{
		var window = new SupplierBillViewerWindow(bill, path) { Owner = Application.Current?.MainWindow };
		window.ShowDialog();
	}
}
