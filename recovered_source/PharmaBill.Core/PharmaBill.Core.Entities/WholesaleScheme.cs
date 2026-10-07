using System;

namespace PharmaBill.Core.Entities;

public sealed class WholesaleScheme : EntityBase
{
	public Guid? DrugId { get; set; }

	public string? PriceCategory { get; set; }

	public decimal BuyQuantity { get; set; }

	public decimal FreeQuantity { get; set; }

	public DateOnly StartsOn { get; set; }

	public DateOnly EndsOn { get; set; }
}
