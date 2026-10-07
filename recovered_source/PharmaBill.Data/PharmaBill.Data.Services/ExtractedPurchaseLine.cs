namespace PharmaBill.Data.Services;

public sealed record ExtractedPurchaseLine(string ItemName, string Batch, string Expiry, decimal Quantity, decimal Free, decimal Mrp, decimal Rate, decimal Gst, decimal Amount);
