using System;

namespace PharmaBill.Core.Entities;

public sealed class PharmacyProfile : EntityBase
{
	public string Name { get; set; } = string.Empty;

	public string? LegalName { get; set; }

	public string? Address { get; set; }

	public string? State { get; set; }

	public string? Phone { get; set; }

	public string? Email { get; set; }

	public string? Gstin { get; set; }

	public string? Pan { get; set; }

	public BusinessMode BusinessMode { get; set; }

	public string? CompetentPersonName { get; set; }

	public string? CompetentPersonQualification { get; set; }

	public string? CompetentPersonRegistrationNumber { get; set; }

	public string? BankName { get; set; }

	public string? BankAccountName { get; set; }

	public string? BankAccountNumber { get; set; }

	public string? BankIfsc { get; set; }

	public string? UpiId { get; set; }

	public string InvoicePrefix { get; set; } = "WIN1";

	public string WholesaleLicenceTypesJson { get; set; } = "[]";

	public string WholesaleBuyerLicenceRulesJson { get; set; } = "{}";

	public DateTime? TrialStartedAtUtc { get; set; }

	public DateTime? LastEntitlementCheckAtUtc { get; set; }

	public bool ClockRollbackDetected { get; set; }

	public string? CurrencyCode { get; set; }

	public string? TimeZoneId { get; set; }

	public bool UserManualEmailSent { get; set; }

	public DateTime? UserManualSentAtUtc { get; set; }
}
