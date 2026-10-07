using System;
using System.Collections.Generic;

namespace PharmaBill.Data.Services;

public sealed record SaveStockTransferInput(Guid SourceLocationId, Guid DestinationLocationId, DateOnly TransferDate, IReadOnlyList<StockTransferLineInput> Items, string? Notes = null);
