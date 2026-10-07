using System;

namespace PharmaBill.Data.Services;

public sealed record WholesaleInvoiceLineInput(Guid DrugId, Guid BatchId, decimal Quantity, decimal FreeQuantity, decimal UnitPrice, decimal DiscountAmount = 0m);
