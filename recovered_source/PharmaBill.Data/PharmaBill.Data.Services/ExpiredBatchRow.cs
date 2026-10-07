using System;

namespace PharmaBill.Data.Services;

public sealed record ExpiredBatchRow(Guid BatchId, string DrugName, string BatchNo, DateOnly ExpiryDate, decimal Quantity);
