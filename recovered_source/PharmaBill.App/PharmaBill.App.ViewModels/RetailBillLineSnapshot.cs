using PharmaBill.Data.Services;

namespace PharmaBill.App.ViewModels;

public sealed record RetailBillLineSnapshot(RetailStockChoice Choice, decimal Quantity, decimal DiscountAmount, decimal UnitPrice);
