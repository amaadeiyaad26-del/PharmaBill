using System;

namespace PharmaBill.App.ViewModels;

public sealed record WholesaleReturnInvoiceChoice(Guid Id, string InvoiceNo, string CustomerName, DateTime InvoiceAtUtc)
{
	public string Label => InvoiceNo + " — " + CustomerName;
}
