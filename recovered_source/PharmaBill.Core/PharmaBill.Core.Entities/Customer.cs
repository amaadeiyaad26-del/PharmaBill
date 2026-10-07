using System;

namespace PharmaBill.Core.Entities;

public sealed class Customer : EntityBase
{
	public string Name { get; set; } = string.Empty;

	public string? Phone { get; set; }

	public string? Email { get; set; }

	public string? Address { get; set; }

	public string? Gstin { get; set; }

	public string? BuyerType { get; set; }

	public bool IsActive { get; set; } = true;

	public decimal CreditLimit { get; set; }

	public int CreditDays { get; set; }

	public string? PriceCategory { get; set; }

	public string? Route { get; set; }

	public string? Salesman { get; set; }

	public decimal OpeningBalance { get; set; }

	public string? State { get; set; }

	public string? StateDrugControlPortalUrl { get; set; }

	public string? VerifiedBy { get; set; }

	public DateTime? VerifiedOnUtc { get; set; }

	public string? VerificationMethod { get; set; }
}
