using System;

namespace PharmaBill.Data.Services;

public sealed record ExpiryReturnTrackerRow(Guid BatchId, string DrugName, string BatchNo, DateOnly? ExpiryDate, decimal OnHand, decimal PurchaseRate, string Supplier);
