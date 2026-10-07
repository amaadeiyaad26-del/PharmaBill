using System;

namespace PharmaBill.Data.Services;

public sealed class BranchStockIndexItem
{
	public string MedicineName { get; set; } = string.Empty;

	public string? CompositionKey { get; set; }

	public Guid? DrugId { get; set; }

	public decimal Quantity { get; set; }
}
