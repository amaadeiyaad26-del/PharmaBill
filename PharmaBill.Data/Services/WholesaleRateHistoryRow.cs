using System;

namespace PharmaBill.Data.Services;

public sealed record WholesaleRateHistoryRow(DateTime SoldAtUtc, string InvoiceNo, decimal UnitRate, decimal Quantity);
