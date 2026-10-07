namespace PharmaBill.Data.Services;

public sealed record Gstr3bReferenceSummary(string Notice, decimal Taxable, decimal Cgst, decimal Sgst, decimal Igst);
