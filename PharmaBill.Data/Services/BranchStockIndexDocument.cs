using System;
using System.Collections.Generic;

namespace PharmaBill.Data.Services;

public sealed class BranchStockIndexDocument
{
	public Guid BranchId { get; set; }

	public string BranchCode { get; set; } = string.Empty;

	public string BranchName { get; set; } = string.Empty;

	public DateTime GeneratedAtUtc { get; set; } = DateTime.UtcNow;

	public List<BranchStockIndexItem> Items { get; set; } = new List<BranchStockIndexItem>();
}
