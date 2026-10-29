namespace PharmaBill.Data.Services;

/// <summary>Subtotal is the sum of the lines read; the figures printed on the bill are in <see cref="Printed"/>.</summary>
public sealed record ExtractedPurchaseInvoice(string DocumentType, string Supplier, string InvoiceNo, string InvoiceDate, IReadOnlyList<ExtractedPurchaseLine> Items, decimal Subtotal, decimal GrandTotal)
{
	/// <summary>The totals as printed on the bill, for checking the lines against. Null when none were read.</summary>
	public InvoicePrintedTotals? Printed { get; init; }

	/// <summary>Which reader produced this, and why that one: shown to the user with the result.</summary>
	public string ReadBy { get; init; } = string.Empty;
}

/// <summary>Totals printed at the foot of a bill. Each is null when the bill does not show it or it could not be read.</summary>
public sealed record InvoicePrintedTotals(decimal? Taxable, decimal? Cgst, decimal? Sgst, decimal? Igst, decimal? RoundOff, decimal? GrandTotal)
{
	public bool HasAny => Taxable.HasValue || GrandTotal.HasValue;

	/// <summary>CGST + SGST + IGST as printed; null when no tax amount was printed.</summary>
	public decimal? Tax => Cgst.HasValue || Sgst.HasValue || Igst.HasValue ? (Cgst ?? 0m) + (Sgst ?? 0m) + (Igst ?? 0m) : null;
}
