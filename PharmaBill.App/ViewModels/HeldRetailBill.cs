using System.Collections.Generic;

namespace PharmaBill.App.ViewModels;

public sealed record HeldRetailBill(string DisplayName, string PatientName, string PatientPhone, string PatientAddress, string DoctorName, string DoctorRegistrationNumber, string? PrescriptionDocumentPath, IReadOnlyList<RetailBillLineSnapshot> Items, string PatientMrdNumber = "")
{
	public override string ToString()
	{
		return DisplayName;
	}
}
