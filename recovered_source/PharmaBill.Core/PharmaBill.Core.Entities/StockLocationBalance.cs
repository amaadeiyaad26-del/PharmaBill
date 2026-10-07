using System;

namespace PharmaBill.Core.Entities;

public sealed class StockLocationBalance : EntityBase
{
	public Guid LocationId { get; set; }

	public Guid BatchId { get; set; }

	public Guid DrugId { get; set; }

	public decimal Quantity { get; set; }
}
