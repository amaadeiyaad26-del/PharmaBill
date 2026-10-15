using System;
using PharmaBill.Core.Gst;

namespace PharmaBill.Data.Services;

/// <param name="Tax">HSN and GST rate entered at billing for a product that has none recorded. Ignored when the product has its own.</param>
public sealed record WholesaleInvoiceLineInput(Guid DrugId, Guid BatchId, decimal Quantity, decimal FreeQuantity, decimal UnitPrice, decimal DiscountAmount = 0m, ProductTaxDetails? Tax = null);
