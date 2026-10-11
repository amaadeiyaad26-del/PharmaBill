using System.Collections.Generic;

namespace PharmaBill.Sync;

public sealed class PhonePurchaseLineUpload
{
	public string Name { get; init; } = string.Empty;

	public string Batch { get; init; } = string.Empty;

	public string Expiry { get; init; } = string.Empty;

	public decimal Quantity { get; init; }

	public decimal Free { get; init; }

	public decimal Mrp { get; init; }

	public decimal Rate { get; init; }

	public decimal Gst { get; init; }
}

public sealed class PhonePurchaseUpload
{
	public string? Supplier { get; init; }

	public string? InvoiceNo { get; init; }

	public string? InvoiceDate { get; init; }

	public string? FileName { get; init; }

	public string? SavedFilePath { get; init; }

	public IReadOnlyList<PhonePurchaseLineUpload> Items { get; init; } = [];
}
