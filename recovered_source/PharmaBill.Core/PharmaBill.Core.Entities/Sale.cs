using System;

namespace PharmaBill.Core.Entities;

public sealed class Sale : EntityBase
{
	public Guid? PatientId { get; set; }

	public Guid? PrescriptionId { get; set; }

	public string InvoiceNo { get; set; } = string.Empty;

	public DateTime SaleAtUtc { get; set; } = DateTime.UtcNow;

	public decimal Subtotal { get; set; }

	public decimal TaxAmount { get; set; }

	public decimal DiscountAmount { get; set; }

	public decimal TotalAmount { get; set; }

	public decimal PaidAmount { get; set; }

	public string? PaymentStatus { get; set; }

	public string? Notes { get; set; }

	public bool IsLocked { get; set; } = true;

	public Guid? BilledByUserId { get; set; }

	public Guid? BranchId { get; set; }
}
