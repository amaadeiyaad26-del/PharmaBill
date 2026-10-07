using System;
using System.Collections.Generic;

namespace PharmaBill.Data.Services;

public sealed record StockTransferSlip(Guid TransferId, string TransferNumber, string SourceLocationName, string DestinationLocationName, DateOnly TransferDate, string? Notes, IReadOnlyList<StockTransferSlipLine> Lines);
