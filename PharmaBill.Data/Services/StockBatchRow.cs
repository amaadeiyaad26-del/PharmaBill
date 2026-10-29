using PharmaBill.Core;
using PharmaBill.Core.Inventory;

namespace PharmaBill.Data.Services;

public sealed record StockBatchRow(Guid BatchId, string BatchNo, DateOnly? ExpiryDate, decimal Mrp, decimal? Ptr, decimal PurchaseRate, decimal Quantity, string? Rack, bool IsBanned = false, string? BanReason = null, NoExpiryReason? NoExpiryReason = null)
{
	/// <summary>No expiry date and no recorded reason: FEFO and expiry alerts cannot place this batch until a date is set.</summary>
	public bool NeedsExpiry => ExpiryDate == null && NoExpiryReason == null;

	public BatchExpiryStatus ExpiryBand => BatchExpiryStatuses.Evaluate(ExpiryDate, BusinessClock.Today);

	public string ExpiryStatus => ExpiryBand switch
	{
		BatchExpiryStatus.Critical => "Critical",
		BatchExpiryStatus.Warning => "Warning",
		_ => "Good",
	};

	public bool IsNearExpiry => BatchExpiryStatuses.IsNearExpiry(ExpiryDate, BusinessClock.Today);
}
