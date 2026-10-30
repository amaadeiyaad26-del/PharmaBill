namespace PharmaBill.Core.Entities;

/// <summary>Where an expiry return claim stands. Stored as its number.</summary>
public enum ExpiryClaimStatus
{
	/// <summary>Being prepared. Its quantities are held: not sellable, still the shop's stock.</summary>
	Draft = 0,
	/// <summary>Goods sent to the distributor; the claim's value is owed to the shop.</summary>
	Sent = 1,
	/// <summary>Some credit received; a balance is still owed.</summary>
	PartCredited = 2,
	/// <summary>Closed: everything is accounted for and at least some credit was received.</summary>
	Credited = 3,
	/// <summary>Closed: the distributor rejected everything and the goods came back.</summary>
	Rejected = 4,
	/// <summary>Closed: nothing was credited and the claim was written off.</summary>
	WrittenOff = 5,
	/// <summary>A draft that was cancelled before sending; its held stock went back to saleable.</summary>
	Voided = 6
}

/// <summary>Where one line's goods are.</summary>
public enum ExpiryClaimLineState
{
	/// <summary>On a draft claim: held in the shop, not sellable, still in stock valuation (marked held).</summary>
	Held = 0,
	/// <summary>Sent to the distributor: out of stock-in-hand, owed to the shop.</summary>
	Sent = 1,
	/// <summary>Rejected by the distributor and written off.</summary>
	RejectedWrittenOff = 2,
	/// <summary>Rejected by the distributor and back in the shop, held (not sellable) until released or written off.</summary>
	RejectedHeld = 3,
	/// <summary>Released back to saleable stock: a voided draft, or rejected goods released.</summary>
	Released = 4
}

/// <summary>How the distributor settled a credit note.</summary>
public enum ExpiryCreditSettlement
{
	/// <summary>Adjusted against purchases: reduces what the shop owes the distributor.</summary>
	AdjustedAgainstPurchases = 0,
	/// <summary>Paid in cash (or to the bank): the distributor's account is unchanged on balance.</summary>
	Cash = 1
}

/// <summary>
/// A claim to a distributor for expiring or expired goods sent back. Every value printed or reported is recorded here
/// when the claim is saved and never re-read from the supplier, product or pharmacy masters.
/// </summary>
public sealed class ExpiryClaim : EntityBase
{
	/// <summary>From the shop's number series, as other documents ("&lt;prefix&gt;-EXP/2026-27/000001").</summary>
	public string ClaimNo { get; set; } = string.Empty;

	/// <summary>The business date (Asia/Kolkata) the claim was made.</summary>
	public DateOnly ClaimDate { get; set; }

	public ExpiryClaimStatus Status { get; set; }

	public Guid SupplierId { get; set; }

	// The distributor, as recorded at save.
	public string SupplierName { get; set; } = string.Empty;

	public string SupplierAddress { get; set; } = string.Empty;

	public string SupplierGstin { get; set; } = string.Empty;

	public string SupplierPhone { get; set; } = string.Empty;

	public string SupplierStateCode { get; set; } = string.Empty;

	public string SupplierDrugLicence { get; set; } = string.Empty;

	// The pharmacy, as recorded at save.
	public string PharmacyName { get; set; } = string.Empty;

	public string PharmacyAddress { get; set; } = string.Empty;

	public string PharmacyGstin { get; set; } = string.Empty;

	public string PharmacyPhone { get; set; } = string.Empty;

	public string PharmacyDrugLicence { get; set; } = string.Empty;

	public string PharmacyStateCode { get; set; } = string.Empty;

	public bool IsInterState { get; set; }

	// The supplier's policy window applied when the claim was built.
	public int PolicyDaysBeforeExpiry { get; set; }

	public int PolicyDaysAfterExpiry { get; set; }

	public string PolicyNote { get; set; } = string.Empty;

	// Totals of the lines, as recorded.
	public decimal TaxableValue { get; set; }

	public decimal CgstAmount { get; set; }

	public decimal SgstAmount { get; set; }

	public decimal IgstAmount { get; set; }

	public decimal TotalValue { get; set; }

	public string? Notes { get; set; }

	public Guid? CreatedByUserId { get; set; }

	public string CreatedByName { get; set; } = string.Empty;

	// Sending.
	public DateTime? SentAtUtc { get; set; }

	public DateOnly? SentDate { get; set; }

	/// <summary>How the goods went: "By hand", "Courier", the distributor's salesman...</summary>
	public string? SentVia { get; set; }

	/// <summary>The person or desk at the distributor who received them.</summary>
	public string? SentTo { get; set; }

	public Guid? SentByUserId { get; set; }

	public string? SentByName { get; set; }

	// Voiding a draft.
	public DateTime? VoidedAtUtc { get; set; }

	public Guid? VoidedByUserId { get; set; }

	public string? VoidedByName { get; set; }

	public string? VoidReason { get; set; }

	/// <summary>When the claim reached a closed status.</summary>
	public DateTime? ClosedAtUtc { get; set; }
}

/// <summary>One batch on a claim, recorded at save time.</summary>
public sealed class ExpiryClaimLine : EntityBase
{
	public Guid ExpiryClaimId { get; set; }

	public Guid BatchId { get; set; }

	public Guid DrugId { get; set; }

	/// <summary>The storage location the held quantity was taken from (and goes back to).</summary>
	public Guid? LocationId { get; set; }

	/// <summary>The purchase line that brought the batch in; null when the batch has no traceable purchase.</summary>
	public Guid? PurchaseItemId { get; set; }

	public string ItemName { get; set; } = string.Empty;

	public string Pack { get; set; } = string.Empty;

	public string HsnCode { get; set; } = string.Empty;

	public string BatchNo { get; set; } = string.Empty;

	public DateOnly? BatchExpiry { get; set; }

	public decimal Quantity { get; set; }

	/// <summary>The rate the shop paid per unit for this batch (net of the purchase line's discount, before GST).</summary>
	public decimal PurchaseRate { get; set; }

	public decimal GstRate { get; set; }

	public decimal TaxableValue { get; set; }

	public decimal CgstAmount { get; set; }

	public decimal SgstAmount { get; set; }

	public decimal IgstAmount { get; set; }

	/// <summary>Taxable value plus GST: what is claimed for this line.</summary>
	public decimal LineValue { get; set; }

	public ExpiryClaimLineState State { get; set; }

	// Rejection by the distributor (the whole line or part of it).
	public decimal RejectedQuantity { get; set; }

	public decimal RejectedValue { get; set; }

	public string? RejectionReason { get; set; }

	public DateTime? RejectedAtUtc { get; set; }

	public Guid? RejectedByUserId { get; set; }

	public string? RejectedByName { get; set; }

	// Write-off of rejected goods.
	public decimal WrittenOffQuantity { get; set; }

	public DateTime? WrittenOffAtUtc { get; set; }

	public Guid? WrittenOffByUserId { get; set; }

	public string? WrittenOffByName { get; set; }

	/// <summary>Rejected goods put back on sale.</summary>
	public decimal ReleasedQuantity { get; set; }

	/// <summary>Credit received for this line, over every credit note.</summary>
	public decimal CreditedValue { get; set; }

	/// <summary>The part of the line the distributor will not pay, accepted by the pharmacist with a reason.</summary>
	public decimal ShortfallValue { get; set; }

	public string? ShortfallReason { get; set; }
}

/// <summary>A credit note received from the distributor against a claim, as printed on that credit note.</summary>
public sealed class ExpiryClaimCredit : EntityBase
{
	public Guid ExpiryClaimId { get; set; }

	public Guid SupplierId { get; set; }

	public string CreditNoteNo { get; set; } = string.Empty;

	public DateOnly CreditNoteDate { get; set; }

	/// <summary>The credit note's total.</summary>
	public decimal Amount { get; set; }

	// The GST the distributor shows on its credit note: recorded separately from the claim's own GST.
	public decimal TaxableValue { get; set; }

	public decimal CgstAmount { get; set; }

	public decimal SgstAmount { get; set; }

	public decimal IgstAmount { get; set; }

	public ExpiryCreditSettlement Settlement { get; set; }

	public string? Notes { get; set; }

	/// <summary>The supplier ledger row this credit note posted.</summary>
	public Guid? SupplierLedgerEntryId { get; set; }

	public Guid? RecordedByUserId { get; set; }

	public string RecordedByName { get; set; } = string.Empty;
}

/// <summary>How much of a credit note covers one claim line, and the shortfall the pharmacist accepted on it.</summary>
public sealed class ExpiryClaimCreditLine : EntityBase
{
	public Guid ExpiryClaimCreditId { get; set; }

	public Guid ExpiryClaimLineId { get; set; }

	public decimal Amount { get; set; }

	/// <summary>The rest of the line this credit note closes without payment (0 when the rest is still owed).</summary>
	public decimal ShortfallValue { get; set; }

	public string? ShortfallReason { get; set; }
}
