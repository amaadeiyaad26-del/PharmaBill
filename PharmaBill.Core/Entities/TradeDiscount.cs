using System;

namespace PharmaBill.Core.Entities;

public sealed class TradeDiscount : EntityBase
{
	public Guid? DrugId { get; set; }

	public string PriceCategory { get; set; } = string.Empty;

	public decimal DiscountPercent { get; set; }

	public DateOnly? EffectiveFrom { get; set; }

	public DateOnly? EffectiveTo { get; set; }
}
