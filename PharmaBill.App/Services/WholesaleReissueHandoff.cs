using PharmaBill.Data.Services;

namespace PharmaBill.App.Services;

/// <summary>The lines of a wholesale invoice that was cancelled so it can be issued again.</summary>
public sealed record WholesaleReissueRequest(Guid CustomerId, string OriginalInvoiceNo, IReadOnlyList<WholesaleInvoiceLineInput> Lines);

/// <summary>
/// Hands a cancelled invoice's lines from the Returns page to Wholesale Billing, which loads them as a new
/// draft. The new bill gets its own invoice number when the user posts it.
/// </summary>
public sealed class WholesaleReissueHandoff
{
	public WholesaleReissueRequest? Pending { get; set; }
}
