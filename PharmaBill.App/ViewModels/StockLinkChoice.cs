using System;
using PharmaBill.Data.Services;

namespace PharmaBill.App.ViewModels;

public sealed class StockLinkChoice
{
	public string Title { get; init; } = string.Empty;

	public string Detail { get; init; } = string.Empty;

	public RetailStockChoice? Stock { get; init; }

	public Guid? DrugId { get; init; }

	public Guid? CatalogMedicineId { get; init; }

	public string Name { get; init; } = string.Empty;
}
