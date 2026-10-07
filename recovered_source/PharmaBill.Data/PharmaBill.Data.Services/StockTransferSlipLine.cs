using System;

namespace PharmaBill.Data.Services;

public sealed record StockTransferSlipLine(string DrugName, string BatchNo, DateOnly? ExpiryDate, decimal Quantity, string? Rack);
