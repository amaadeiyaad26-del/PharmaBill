namespace PharmaBill.App.Services;

/// <summary>
/// A medicine the counter could not sell for want of stock, handed to Purchases so its inward entry starts with that
/// medicine already searched.
/// </summary>
public sealed class PurchaseEntryHandoff
{
	public string? MedicineName { get; set; }
}
