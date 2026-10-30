
namespace PharmaBill.Core.Entities;

public sealed class WholesaleReturnItem : EntityBase
{
	public Guid ReturnNoteId { get; set; }

	public Guid WholesaleInvoiceItemId { get; set; }

	public Guid BatchId { get; set; }

	public Guid DrugId { get; set; }

	public decimal Quantity { get; set; }

	public decimal CreditAmount { get; set; }

	public bool Restocked { get; set; }

	public bool Quarantined { get; set; }

	/// <summary>Rate per unit as charged on the original invoice (before GST, as on WholesaleInvoiceItem).</summary>
	public decimal? UnitPrice { get; set; }

	// Recorded when the note is saved, and read back by every report: never re-resolved from the product master.
	// Names follow WholesaleInvoiceItem. Text is "" (never null) when the source genuinely had nothing.

	public string? ItemName { get; set; }

	public string? HsnCode { get; set; }

	/// <summary>Pack / unit description (Drug.Unit when the note was saved).</summary>
	public string? Unit { get; set; }

	public string? BatchNo { get; set; }

	public DateOnly? BatchExpiry { get; set; }

	public decimal? TaxRate { get; set; }

	public decimal? DiscountAmount { get; set; }

	public decimal? TaxableAmount { get; set; }

	public decimal? CgstAmount { get; set; }

	public decimal? SgstAmount { get; set; }

	public decimal? IgstAmount { get; set; }

	/// <summary>
	/// True when this row was filled after the fact from current master data (no original invoice line to copy from),
	/// so it is not what was recorded when the note was issued. False when recorded at save or copied from the invoice.
	/// </summary>
	public bool ValuesBackfilled { get; set; }
}
