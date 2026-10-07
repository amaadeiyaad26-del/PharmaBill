using System;

namespace PharmaBill.Core.Entities;

public sealed class CustomerCategoryPrice : EntityBase
{
	public Guid? DrugId { get; set; }

	public string PriceCategory { get; set; } = string.Empty;

	public decimal UnitPrice { get; set; }

	public DateOnly? EffectiveFrom { get; set; }

	public DateOnly? EffectiveTo { get; set; }
}
