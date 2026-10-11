using System.Collections.Generic;

namespace PharmaBill.Data.Services;

public sealed record SaveRetailSaleInput(string PatientName, string PatientPhone, string? PatientAddress, string? PrescriberName, string? PrescriberRegistrationNumber, string? PrescriptionDocumentPath, IReadOnlyList<RetailSaleLineInput> Items, IReadOnlyList<RetailPaymentInput> Payments, bool UpiPaymentReceived, string? Notes = null, string? MrdNumber = null, bool PrescriptionAdminOverride = false);
