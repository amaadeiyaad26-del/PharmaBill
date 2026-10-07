using System;

namespace PharmaBill.Data.Services;

public sealed record StockInHandRow(Guid DrugId, string DrugName, string BatchNo, DateOnly? ExpiryDate, decimal Opening, decimal InQuantity, decimal OutQuantity, decimal Closing, decimal Mrp, decimal Ptr, decimal PurchaseRate, decimal StockValue, string Alert);
