using System;
using PharmaBill.Core.Inventory;

namespace PharmaBill.Data.Services;

public sealed record StockBatchRow(Guid BatchId, string BatchNo, DateOnly? ExpiryDate, decimal Mrp, decimal? Ptr, decimal PurchaseRate, decimal Quantity, string? Rack, bool IsBanned = false, string? BanReason = null)
{
	public BatchExpiryStatus ExpiryBand => BatchExpiryStatuses.Evaluate(ExpiryDate, DateOnly.FromDateTime(DateTime.Today));

	public string ExpiryStatus => ExpiryBand switch
	{
		BatchExpiryStatus.Critical => "Critical",
		BatchExpiryStatus.Warning => "Warning",
		_ => "Good",
	};

	public bool IsNearExpiry => BatchExpiryStatuses.IsNearExpiry(ExpiryDate, DateOnly.FromDateTime(DateTime.Today));
}
