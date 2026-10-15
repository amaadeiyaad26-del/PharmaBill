using System;

namespace PharmaBill.Core.Entities;

public sealed class SaleItem : EntityBase
{
	public Guid SaleId { get; set; }

	public Guid DrugId { get; set; }

	public Guid BatchId { get; set; }

	public decimal Quantity { get; set; }

	public decimal UnitPrice { get; set; }

	public decimal DiscountAmount { get; set; }

	public decimal TaxRate { get; set; }

	public decimal LineTotal { get; set; }

	/// <summary>Printed/filed line details captured at save; null on older rows.</summary>
	public string? ItemName { get; set; }

	public string? HsnCode { get; set; }

	public string? BatchNo { get; set; }

	public DateOnly? BatchExpiry { get; set; }
}
