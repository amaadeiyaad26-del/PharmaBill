using System;

namespace PharmaBill.Core.Entities;

public sealed class CatalogInfo : EntityBase
{
	public string NameKey { get; set; } = string.Empty;

	public Guid? CatalogMedicineId { get; set; }

	public string? Schedule { get; set; }

	public bool IsHabitForming { get; set; }

	public bool RequiresPrescription { get; set; }

	public string? RegisterType { get; set; }

	public string? Notes { get; set; }

	public string? TherapeuticClass { get; set; }

	public string? ChemicalClass { get; set; }

	public string? ActionClass { get; set; }

	public string? Use { get; set; }
}
