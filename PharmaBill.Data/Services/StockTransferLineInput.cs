using System;

namespace PharmaBill.Data.Services;

public sealed record StockTransferLineInput(Guid BatchId, decimal Quantity);
