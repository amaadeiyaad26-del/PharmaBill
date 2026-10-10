using System;
using System.Collections.Generic;

namespace PharmaBill.Data.Services;

public sealed record SavePurchaseInput(Guid SupplierId, string InvoiceNo, DateOnly InvoiceDate, decimal Subtotal, decimal DiscountAmount, decimal TaxAmount, decimal GrandTotal, IReadOnlyList<PurchaseLineInput> Items, string? Notes = null, Guid? StorageLocationId = null, string? AttachedInvoicePath = null, string? OriginalFileName = null);
