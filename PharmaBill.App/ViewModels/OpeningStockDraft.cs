
namespace PharmaBill.App.ViewModels;

/// <summary>A batch put into stock from the counter. No expiry only with <paramref name="NoExpiryPrinted"/> (or for an instrument).</summary>
public sealed record OpeningStockDraft(string BrandName, string BatchNo, DateOnly? Expiry, decimal Mrp, decimal PurchaseRate, decimal Quantity, bool NoExpiryPrinted = false);
