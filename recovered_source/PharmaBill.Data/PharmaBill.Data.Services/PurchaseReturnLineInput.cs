using System;

namespace PharmaBill.Data.Services;

public sealed record PurchaseReturnLineInput(Guid BatchId, decimal Quantity);
