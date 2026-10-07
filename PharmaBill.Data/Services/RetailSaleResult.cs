using PharmaBill.Core.Entities;

namespace PharmaBill.Data.Services;

public sealed record RetailSaleResult(Sale Sale, decimal Subtotal, decimal TaxAmount, decimal DiscountAmount, decimal TotalAmount, decimal PaidAmount, decimal ChangeDue, string InvoiceNo);
