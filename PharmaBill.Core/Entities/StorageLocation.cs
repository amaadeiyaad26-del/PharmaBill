namespace PharmaBill.Core.Entities;

public sealed class StorageLocation : EntityBase
{
	public string Name { get; set; } = string.Empty;

	public string Code { get; set; } = string.Empty;

	public bool IsDefaultRetailLocation { get; set; }

	public bool IsActive { get; set; } = true;
}
