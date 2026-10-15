using System.Collections.Generic;

namespace PharmaBill.Data.Services;

public sealed record SaveRetailSaleInput(string PatientName, string PatientPhone, string? PatientAddress, string? PrescriberName, string? PrescriberRegistrationNumber, string? PrescriptionDocumentPath, IReadOnlyList<RetailSaleLineInput> Items, IReadOnlyList<RetailPaymentInput> Payments, bool UpiPaymentReceived, string? Notes = null, string? MrdNumber = null, bool PrescriptionAdminOverride = false, bool AddToCustomers = false, RetailPrintBranding? PrintBranding = null);

/// <summary>Print-settings content that appears on the retail tax invoice; recorded on the sale at save.</summary>
public sealed record RetailPrintBranding(IReadOnlyList<string> ExtraLicenceLines, string? FssaiNumber, string? AddressLine2, string? TermsText);
