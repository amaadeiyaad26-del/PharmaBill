
namespace PharmaBill.Core.Entities;

/// <summary>
/// A quotation for retail or wholesale. It never touches stock or ledgers; it becomes a bill only when
/// converted and posted from the billing page.
/// </summary>
public sealed class Quotation : EntityBase
{
	public const string OpenStatus = "Open";

	public const string ConvertedStatus = "Converted";

	public const string CancelledStatus = "Cancelled";

	/// <summary>"Retail" or "Wholesale".</summary>
	public string Mode { get; set; } = string.Empty;

	public string QuoteNo { get; set; } = string.Empty;

	public DateOnly QuoteDate { get; set; }

	public DateOnly? ValidUntil { get; set; }

	/// <summary>Wholesale buyer, or a retail credit customer when one is linked.</summary>
	public Guid? CustomerId { get; set; }

	/// <summary>Name shown on the quotation (retail patient or wholesale buyer name at the time of quoting).</summary>
	public string PartyName { get; set; } = string.Empty;

	public string? PartyPhone { get; set; }

	public decimal EstimatedTotal { get; set; }

	public string Status { get; set; } = OpenStatus;

	/// <summary>Invoice number of the bill this quotation became, once posted.</summary>
	public string? ConvertedInvoiceNo { get; set; }

	public string? Notes { get; set; }
}
