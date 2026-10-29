namespace PharmaBill.Data.Services;

public sealed record ExtractedPurchaseLine(string ItemName, string Batch, string Expiry, decimal Quantity, decimal Free, decimal Mrp, decimal Rate, decimal Gst, decimal Amount)
{
	public string? ValidationWarning { get; init; }

	/// <summary>The HSN code printed on this invoice line, when the scan read one.</summary>
	public string? HsnCode { get; init; }

	/// <summary>The manufacturer printed with this line, when the bill shows one.</summary>
	public string? Manufacturer { get; init; }

	/// <summary>The pack printed on this line (e.g. 10x10), when the bill shows one.</summary>
	public string? Pack { get; init; }
}
