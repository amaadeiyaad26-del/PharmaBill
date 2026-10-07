using System;

namespace PharmaBill.Core.Ai;

public sealed record StockAdvisoryItemDto(Guid DrugId, string Name, decimal CurrentStock, decimal AverageDailySales, decimal? DaysOfCover, decimal SuggestedReorderQuantity, decimal NearExpiryQuantity, StockAdvisoryStatus Status, string Advice);
