namespace PharmaBill.Data.Services;

public sealed record GstReturnDashboardSummary(decimal TotalTaxableTurnover, decimal OutputCgst, decimal OutputSgst, decimal OutputIgst, decimal EligibleItcFromPurchases, decimal NetGstPayable);
