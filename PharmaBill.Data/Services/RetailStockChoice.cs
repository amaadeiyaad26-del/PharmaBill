using System;

namespace PharmaBill.Data.Services;

public sealed record RetailStockChoice(Guid DrugId, string DrugName, string? Barcode, Guid BatchId, string BatchNo, DateOnly? ExpiryDate, decimal Mrp, decimal? SalePrice, decimal AvailableQuantity, decimal GstRate, string? Schedule, bool IsHabitForming, string? RegisterType, bool IsBanned = false);
