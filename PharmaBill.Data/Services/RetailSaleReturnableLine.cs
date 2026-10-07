using System;

namespace PharmaBill.Data.Services;

public sealed class RetailSaleReturnableLine
{
	public Guid SaleItemId { get; init; }

	public string DrugName { get; init; } = string.Empty;

	public string BatchNo { get; init; } = string.Empty;

	public DateOnly? ExpiryDate { get; init; }

	public decimal SoldQuantity { get; init; }

	public decimal ReturnedQuantity { get; init; }

	public decimal RemainingQuantity { get; init; }

	public decimal UnitPrice { get; init; }

	public decimal ReturnQuantity { get; set; }
}
