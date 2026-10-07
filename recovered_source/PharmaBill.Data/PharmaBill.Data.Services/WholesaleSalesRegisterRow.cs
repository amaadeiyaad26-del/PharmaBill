using System;

namespace PharmaBill.Data.Services;

public sealed record WholesaleSalesRegisterRow(string InvoiceNo, DateTime InvoiceAtUtc, string CustomerName, string? Gstin, string Hsn, decimal TaxableAmount, decimal Cgst, decimal Sgst, decimal Igst, decimal Total);
