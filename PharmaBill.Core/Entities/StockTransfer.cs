using System;

namespace PharmaBill.Core.Entities;

public sealed class StockTransfer : EntityBase
{
	public string TransferNumber { get; set; } = string.Empty;

	public Guid SourceLocationId { get; set; }

	public Guid DestinationLocationId { get; set; }

	public DateOnly TransferDate { get; set; }

	public string? Notes { get; set; }

	public string Status { get; set; } = "Posted";
}
