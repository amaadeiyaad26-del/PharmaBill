using System;

namespace PharmaBill.Data.Services;

public sealed record ReorderSuggestion(Guid DrugId, string DrugName, decimal CurrentStock, decimal ReorderLevel, decimal SuggestedQuantity);
