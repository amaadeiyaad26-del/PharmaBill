
namespace PharmaBill.Core.Entities;

public sealed class ReturnNote : EntityBase
{
	public string ReturnNo { get; set; } = string.Empty;

	public string SourceType { get; set; } = string.Empty;

	public Guid SourceId { get; set; }

	public DateTime ReturnAtUtc { get; set; } = BusinessClock.UtcNow;

	public decimal TotalAmount { get; set; }

	public string Reason { get; set; } = string.Empty;

	public string? Notes { get; set; }

	/// <summary>Set when a billing terminal issued the return: its ID for that request, so a retry never credits twice. Unique when present.</summary>
	public Guid? ClientRequestId { get; set; }

	/// <summary>The document date as the shop sees it (Asia/Kolkata).</summary>
	public DateOnly? ReturnDate { get; set; }

	// Recorded when the note is saved, and read back by every report: never re-resolved from the party master or
	// the pharmacy profile. Names follow WholesaleInvoice; the note keeps the roles of the invoice it adjusts.

	public string? PharmacyStateCode { get; set; }

	public string? BuyerStateCode { get; set; }

	public string? BuyerGstin { get; set; }

	public string? BuyerLegalName { get; set; }

	public string? BuyerAddress { get; set; }

	public string? SellerSnapshotJson { get; set; }

	public string? BuyerSnapshotJson { get; set; }

	/// <summary>The supply type applied: true inter-state (IGST), false intra-state (CGST + SGST).</summary>
	public bool? IsInterState { get; set; }

	/// <summary>True when the party and pharmacy details were filled after the fact from current master data.</summary>
	public bool ValuesBackfilled { get; set; }
}
