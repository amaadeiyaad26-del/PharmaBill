namespace PharmaBill.Data.Services;

public sealed record PurchaseLineInput(Guid DrugId, string BatchNo, DateOnly? ExpiryDate, decimal Quantity, decimal FreeQuantity, decimal Mrp, decimal Rate, decimal GstRate, decimal DiscountAmount, decimal Amount, string? Rack)
{
	/// <summary>The user ticked "No expiry printed on pack" for this line (see ExpiryRule). Ignored when a date is given.</summary>
	public bool NoExpiryPrinted { get; init; }
}
