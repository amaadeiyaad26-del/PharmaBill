namespace PharmaBill.Data.Services.Reconciliation;

public sealed record BatchSettleResult(int SettledCount, decimal TotalPosted, string Message);
