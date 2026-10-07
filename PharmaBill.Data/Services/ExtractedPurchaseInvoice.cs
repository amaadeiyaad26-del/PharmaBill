using System.Collections.Generic;

namespace PharmaBill.Data.Services;

public sealed record ExtractedPurchaseInvoice(string DocumentType, string Supplier, string InvoiceNo, string InvoiceDate, IReadOnlyList<ExtractedPurchaseLine> Items, decimal Subtotal, decimal GrandTotal);
