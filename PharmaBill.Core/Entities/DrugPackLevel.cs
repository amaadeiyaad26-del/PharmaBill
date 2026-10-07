using System;

namespace PharmaBill.Core.Entities;

public sealed class DrugPackLevel : EntityBase
{
	public Guid DrugId { get; set; }

	public PackLevel Level { get; set; }

	public string Label { get; set; } = string.Empty;

	public decimal UnitsPerPack { get; set; }
}
