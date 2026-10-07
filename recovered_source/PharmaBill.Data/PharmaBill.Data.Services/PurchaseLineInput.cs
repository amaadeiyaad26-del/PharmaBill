using System;

namespace PharmaBill.Data.Services;

public sealed record PurchaseLineInput(Guid DrugId, string BatchNo, DateOnly? ExpiryDate, decimal Quantity, decimal FreeQuantity, decimal Mrp, decimal Rate, decimal GstRate, decimal DiscountAmount, decimal Amount, string? Rack);
