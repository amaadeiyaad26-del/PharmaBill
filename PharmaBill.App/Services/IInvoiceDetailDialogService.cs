using PharmaBill.Data.Services;

namespace PharmaBill.App.Services;

public interface IInvoiceDetailDialogService
{
	void Show(InvoiceDetail detail);
}
