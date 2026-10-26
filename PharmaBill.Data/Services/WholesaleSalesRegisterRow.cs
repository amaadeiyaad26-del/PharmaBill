
namespace PharmaBill.Data.Services;

/// <summary>DetailsVerified is false when the invoice predates recorded tax details and name/GSTIN/HSN were re-read from master data.</summary>
public sealed record WholesaleSalesRegisterRow(string InvoiceNo, DateTime InvoiceAtUtc, string CustomerName, string? Gstin, string Hsn, decimal TaxableAmount, decimal Cgst, decimal Sgst, decimal Igst, decimal Total, bool DetailsVerified = true);
