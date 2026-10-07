using System;

namespace PharmaBill.Core.Entities;

public sealed class Patient : EntityBase
{
	public string Name { get; set; } = string.Empty;

	public string? Phone { get; set; }

	public string? Address { get; set; }

	public DateOnly? DateOfBirth { get; set; }

	public string? Sex { get; set; }
}
