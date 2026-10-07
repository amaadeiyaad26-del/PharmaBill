using System;
using System.Collections.Generic;

namespace PharmaBill.Core.Ai;

public sealed record InventoryForecastReportDto(DateTime GeneratedAtUtc, string Source, int LookbackDays, int CoverDays, IReadOnlyList<StockAdvisoryItemDto> Items);
