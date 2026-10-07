using PharmaBill.Data.Services;

namespace PharmaBill.App.ViewModels;

public sealed record ConfirmedPrescriptionItem(RetailStockChoice Choice, decimal Quantity);
