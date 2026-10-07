using System;
using System.Threading;
using System.Threading.Tasks;

namespace PharmaBill.App.Services;

public interface IInvoicePrintService
{
	Task<string> PrintAsync(Guid saleId, InvoiceTemplateType? templateOverride = null, bool? silentOverride = null, CancellationToken cancellationToken = default(CancellationToken));

	Task<string> PreviewAsync(Guid saleId, InvoiceTemplateType template = InvoiceTemplateType.StandardA4, CancellationToken cancellationToken = default(CancellationToken));

	Task<string?> PickFormatAndPrintAsync(Guid saleId, CancellationToken cancellationToken = default(CancellationToken));
}
