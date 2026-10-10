using System;

namespace PharmaBill.App.ViewModels;

public sealed record OpeningStockDraft(string BrandName, string BatchNo, DateOnly Expiry, decimal Mrp, decimal PurchaseRate, decimal Quantity);
