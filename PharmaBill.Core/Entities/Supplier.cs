namespace PharmaBill.Core.Entities;

public sealed class Supplier : EntityBase
{
	public string Name { get; set; } = string.Empty;

	public string? Phone { get; set; }

	public string? Email { get; set; }

	public string? Address { get; set; }

	public string? Gstin { get; set; }

	public string? DrugLicenceNumber { get; set; }

	public bool IsActive { get; set; } = true;
}
