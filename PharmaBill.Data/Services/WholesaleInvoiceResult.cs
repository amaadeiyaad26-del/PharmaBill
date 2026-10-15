using System.Collections.Generic;
using PharmaBill.Core.Entities;
using PharmaBill.Core.Gst;

namespace PharmaBill.Data.Services;

public sealed record WholesaleInvoiceResult(WholesaleInvoice Invoice, decimal CgstAmount, decimal SgstAmount, decimal IgstAmount, decimal RoundOff, decimal OutstandingAfterPosting, bool HasOverdueBalance, IReadOnlyList<string>? StateNotes = null);

public sealed record WholesaleGstStateCheck(GstStateResolution Pharmacy, GstStateResolution Buyer)
{
	public string? RefusalMessage { get; init; }

	public IReadOnlyList<string> Notes
	{
		get
		{
			List<string> notes = new List<string>();
			if (Pharmacy.Note != null)
			{
				notes.Add("Pharmacy: " + Pharmacy.Note);
			}
			if (Buyer.Note != null)
			{
				notes.Add("Buyer: " + Buyer.Note);
			}
			return notes;
		}
	}
}
