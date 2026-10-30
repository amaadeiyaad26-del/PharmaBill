
namespace PharmaBill.Core.Entities;

public sealed class PurchaseReturn : EntityBase
{
	public Guid SupplierId { get; set; }

	public string ReturnNo { get; set; } = string.Empty;

	public DateOnly ReturnDate { get; set; }

	public decimal TotalAmount { get; set; }

	public string Reason { get; set; } = string.Empty;

	// Recorded when the note is saved, and read back by every report: never re-resolved from the supplier master or
	// the pharmacy profile. It adjusts a purchase, so (as on that purchase) the supplier is the seller and the pharmacy
	// the buyer; names follow WholesaleInvoice with those roles.

	public string? PharmacyStateCode { get; set; }

	public string? SellerStateCode { get; set; }

	public string? SellerGstin { get; set; }

	public string? SellerLegalName { get; set; }

	public string? SellerAddress { get; set; }

	public string? SellerSnapshotJson { get; set; }

	public string? BuyerSnapshotJson { get; set; }

	/// <summary>The supply type applied: true inter-state (IGST), false intra-state (CGST + SGST).</summary>
	public bool? IsInterState { get; set; }

	/// <summary>True when the party and pharmacy details were filled after the fact from current master data.</summary>
	public bool ValuesBackfilled { get; set; }
}
