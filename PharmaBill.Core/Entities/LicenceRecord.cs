using System;

namespace PharmaBill.Core.Entities;

public sealed class LicenceRecord : EntityBase
{
	public string LicenceNumber { get; set; } = string.Empty;

	public string LicenceType { get; set; } = string.Empty;

	public string? IssuingAuthority { get; set; }

	public DateOnly? IssuedOn { get; set; }

	public DateOnly? ExpiresOn { get; set; }

	public string? Notes { get; set; }

	public string? DocumentPath { get; set; }
}
