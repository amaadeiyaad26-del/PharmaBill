namespace PharmaBill.Data.Services;

public sealed record GstRateSummary(string Hsn, decimal TaxRate, decimal Taxable, decimal Cgst, decimal Sgst, decimal Igst, decimal Total);
