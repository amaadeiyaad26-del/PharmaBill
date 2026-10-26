
namespace PharmaBill.Data.Services;

/// <summary>DetailsRecorded: party/print details come from what was captured at save, not from current settings or master data.</summary>
public sealed record RetailBillPrintData(string PharmacyName, string? PharmacyAddress, string? PharmacyPhone, string? Gstin, IReadOnlyList<string> LicenceNumbers, string? PharmacistName, string? PharmacistQualification, string? PharmacistRegistrationNumber, string InvoiceNo, DateTime SaleAtUtc, string PatientName, string? PatientPhone, string? PatientAddress, decimal Subtotal, decimal TaxAmount, decimal DiscountAmount, decimal TotalAmount, decimal PaidAmount, IReadOnlyList<RetailBillPrintLine> Items, string? PrescriberName = null, string? PrescriberRegistrationNumber = null, string? FssaiNumber = null, string? AddressLine2 = null, string? TermsText = null, decimal CgstAmount = 0m, decimal SgstAmount = 0m, string? MrdNumber = null, bool DetailsRecorded = false);
