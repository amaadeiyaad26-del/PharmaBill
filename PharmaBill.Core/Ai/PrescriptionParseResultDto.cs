using System;
using System.Collections.Generic;

namespace PharmaBill.Core.Ai;

public sealed record PrescriptionParseResultDto
{
	public string DoctorName { get; init; } = string.Empty;

	public string DoctorRegistrationNo { get; init; } = string.Empty;

	public string PatientName { get; init; } = string.Empty;

	public DateTime? PrescriptionDate { get; init; }

	public List<PrescribedItemDto> DetectedMedicines { get; init; } = new List<PrescribedItemDto>();

	public string RawExtractedText { get; init; } = string.Empty;

	public string Source { get; init; } = "None";

	public string Message { get; init; } = string.Empty;

	public bool ApiKeyMissing { get; init; }

	public bool HasContent
	{
		get
		{
			if (DetectedMedicines.Count <= 0 && string.IsNullOrWhiteSpace(DoctorName))
			{
				return !string.IsNullOrWhiteSpace(PatientName);
			}
			return true;
		}
	}
}
