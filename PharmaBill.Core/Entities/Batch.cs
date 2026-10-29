
namespace PharmaBill.Core.Entities;

public sealed class Batch : EntityBase
{
	public Guid DrugId { get; set; }

	public Guid? SupplierId { get; set; }

	public string BatchNo { get; set; } = string.Empty;

	public DateOnly? ExpiryDate { get; set; }

	/// <summary>Why there is no expiry date; set whenever <see cref="ExpiryDate"/> is null (see ExpiryRule).</summary>
	public PharmaBill.Core.Inventory.NoExpiryReason? NoExpiryReason { get; set; }

	public decimal Quantity { get; set; }

	public decimal PurchasePrice { get; set; }

	public decimal? Ptr { get; set; }

	public decimal? Pts { get; set; }

	public decimal? SalePrice { get; set; }

	public decimal? Mrp { get; set; }

	public string? Rack { get; set; }

	public Guid? BranchId { get; set; }

	public bool IsBanned { get; set; }

	public string? BanReason { get; set; }
}
