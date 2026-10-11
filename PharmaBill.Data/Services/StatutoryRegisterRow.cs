using System;

namespace PharmaBill.Data.Services;

public sealed record StatutoryRegisterRow(Guid EntryId, DateTime EntryAtUtc, string RegisterType, string DocumentNo, string PartyName, string Address, string Phone, string BuyerLicenceNo, string DoctorName, string DoctorRegistrationNo, string DrugName, string BatchNo, decimal Quantity, decimal Balance, string Reason)
{
	public string Composition { get; init; } = string.Empty;

	public string ExpiryLabel { get; init; } = string.Empty;

	public string PrescriptionFilePath { get; init; } = string.Empty;
}
