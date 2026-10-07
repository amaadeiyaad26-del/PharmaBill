namespace PharmaBill.Data.Services;

public sealed record InvoiceDetailLine(string Item, string Composition, string BatchNo, string Expiry, string Quantity, decimal UnitPrice, decimal TaxRate, decimal DiscountAmount, decimal LineTotal);
