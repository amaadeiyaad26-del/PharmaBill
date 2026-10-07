using System;

namespace PharmaBill.Core.Entities;

public sealed class StockTransferItem : EntityBase
{
	public Guid StockTransferId { get; set; }

	public Guid BatchId { get; set; }

	public Guid DrugId { get; set; }

	public decimal QuantityTransferred { get; set; }
}
