using System;

namespace PharmaBill.Core.Entities;

public sealed class ScheduleOverride : EntityBase
{
	public Guid DrugId { get; set; }

	public string Schedule { get; set; } = string.Empty;

	public string? Reason { get; set; }

	public DateTime? EffectiveFromUtc { get; set; }

	public DateTime? EffectiveToUtc { get; set; }
}
