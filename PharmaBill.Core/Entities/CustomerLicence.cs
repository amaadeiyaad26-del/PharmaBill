using System;

namespace PharmaBill.Core.Entities;

public sealed class CustomerLicence : EntityBase
{
	public Guid CustomerId { get; set; }

	public string LicenceNumber { get; set; } = string.Empty;

	public string LicenceType { get; set; } = string.Empty;

	public DateOnly? IssuedOn { get; set; }

	public DateOnly? ExpiresOn { get; set; }

	public string? IssuingAuthority { get; set; }

	public string? DocumentPath { get; set; }

	public string? Authorisation { get; set; }

	public string? Notes { get; set; }
}
