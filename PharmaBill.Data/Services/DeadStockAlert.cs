using System;

namespace PharmaBill.Data.Services;

public sealed record DeadStockAlert(Guid DrugId, string DrugName, string BatchNo, decimal Quantity, DateTime LastMovementAtUtc);
