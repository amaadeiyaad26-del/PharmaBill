
namespace PharmaBill.Core.Entities;

public sealed class Sale : EntityBase
{
	public Guid? PatientId { get; set; }

	public Guid? PrescriptionId { get; set; }

	public string? PrescriptionFilePath { get; set; }

	public string InvoiceNo { get; set; } = string.Empty;

	public DateTime SaleAtUtc { get; set; } = BusinessClock.UtcNow;

	public decimal Subtotal { get; set; }

	public decimal TaxAmount { get; set; }

	public decimal DiscountAmount { get; set; }

	public decimal TotalAmount { get; set; }

	public decimal PaidAmount { get; set; }

	public string? PaymentStatus { get; set; }

	public string? Notes { get; set; }

	public string? MrdNumber { get; set; }

	public bool IsLocked { get; set; } = true;

	public Guid? BilledByUserId { get; set; }

	public Guid? BranchId { get; set; }

	/// <summary>Pharmacy GST state code resolved at save (place of supply for counter sales). Null when not recorded.</summary>
	public string? PharmacyStateCode { get; set; }

	/// <summary>JSON <see cref="SellerSnapshot"/> captured at save. Null on bills saved before snapshots were recorded.</summary>
	public string? SellerSnapshotJson { get; set; }

	/// <summary>JSON <see cref="BuyerSnapshot"/> (patient and prescriber) captured at save.</summary>
	public string? BuyerSnapshotJson { get; set; }

	/// <summary>Set when a billing terminal saved the bill: the terminal's ID for that save request, so a retry after a
	/// lost reply returns this bill instead of billing twice. Unique when present.</summary>
	public Guid? ClientRequestId { get; set; }
}
