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

	/// <summary>
	/// How many days before expiry this supplier accepts expiry returns. Null follows the pharmacy's default
	/// (<see cref="PharmacyProfile.ExpiryReturnDaysBefore"/>) until it is set for this supplier.
	/// </summary>
	public int? ExpiryReturnDaysBefore { get; set; }

	/// <summary>How many days after expiry this supplier still accepts returns. Null follows the pharmacy's default.</summary>
	public int? ExpiryReturnDaysAfter { get; set; }

	/// <summary>The supplier's stated return policy, in their words; printed on every claim.</summary>
	public string? ExpiryReturnPolicyNote { get; set; }
}
