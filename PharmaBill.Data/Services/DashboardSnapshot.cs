namespace PharmaBill.Data.Services;

public sealed record DashboardSnapshot(decimal TodaySales, decimal MonthSales, int ExpiringSoonBatches, int LowStockDrugs, decimal WholesaleOutstanding, int RegisterAlerts, int TodayInvoiceCount = 0);
