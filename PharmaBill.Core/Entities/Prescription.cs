using System;

namespace PharmaBill.Core.Entities;

public sealed class Prescription : EntityBase
{
	public Guid? PatientId { get; set; }

	public string? PrescriberName { get; set; }

	public string? PrescriberRegistrationNumber { get; set; }

	public DateOnly? PrescriptionDate { get; set; }

	public string? ReferenceNumber { get; set; }

	public string? Notes { get; set; }
}
