using System;

namespace PharmaBill.Core.Entities;

public sealed class CustomerLedgerEntry : EntityBase
{
	public Guid CustomerId { get; set; }

	public DateTime EntryAtUtc { get; set; } = DateTime.UtcNow;

	public string EntryType { get; set; } = string.Empty;

	public Guid? ReferenceId { get; set; }

	public string? ReferenceNo { get; set; }

	public decimal Debit { get; set; }

	public decimal Credit { get; set; }

	public string? Notes { get; set; }
}
