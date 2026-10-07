using System;

namespace PharmaBill.Core.Entities;

public sealed class Batch : EntityBase
{
	public Guid DrugId { get; set; }

	public Guid? SupplierId { get; set; }

	public string BatchNo { get; set; } = string.Empty;

	public DateOnly? ExpiryDate { get; set; }

	public decimal Quantity { get; set; }

	public decimal PurchasePrice { get; set; }

	public decimal? Ptr { get; set; }

	public decimal? Pts { get; set; }

	public decimal? SalePrice { get; set; }

	public decimal? Mrp { get; set; }

	public string? Rack { get; set; }

	public Guid? BranchId { get; set; }
}
