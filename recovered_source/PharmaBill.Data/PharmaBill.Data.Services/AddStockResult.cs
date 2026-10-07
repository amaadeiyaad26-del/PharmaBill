using System;

namespace PharmaBill.Data.Services;

public sealed record AddStockResult(Guid DrugId, Guid BatchId, decimal TotalStock, bool MergedIntoExistingBatch);
