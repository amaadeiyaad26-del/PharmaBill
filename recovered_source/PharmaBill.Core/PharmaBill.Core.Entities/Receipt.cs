using System;

namespace PharmaBill.Core.Entities;

public sealed class Receipt : EntityBase
{
	public Guid? CustomerId { get; set; }

	public Guid? SupplierId { get; set; }

	public Guid? WholesaleInvoiceId { get; set; }

	public string ReceiptNo { get; set; } = string.Empty;

	public DateTime ReceiptAtUtc { get; set; } = DateTime.UtcNow;

	public decimal Amount { get; set; }

	public string PaymentMethod { get; set; } = string.Empty;

	public string? ChequeNumber { get; set; }

	public DateOnly? ChequeDate { get; set; }

	public string? BankName { get; set; }

	public string? ReferenceNumber { get; set; }

	public string? Notes { get; set; }
}
