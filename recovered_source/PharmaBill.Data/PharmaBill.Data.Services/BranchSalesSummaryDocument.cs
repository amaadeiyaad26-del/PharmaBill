using System;

namespace PharmaBill.Data.Services;

public sealed class BranchSalesSummaryDocument
{
	public Guid BranchId { get; set; }

	public string BranchCode { get; set; } = string.Empty;

	public string BranchName { get; set; } = string.Empty;

	public DateTime GeneratedAtUtc { get; set; } = DateTime.UtcNow;

	public decimal SalesTotal { get; set; }

	public int InvoiceCount { get; set; }

	public DateOnly? FromDate { get; set; }

	public DateOnly? ToDate { get; set; }
}
