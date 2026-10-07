using System;

namespace PharmaBill.Data.Services;

public sealed record WholesalePurchaseRegisterRow(string InvoiceNo, DateOnly InvoiceDate, string SupplierName, string? Gstin, string Hsn, decimal TaxableAmount, decimal TaxAmount, decimal Total);
