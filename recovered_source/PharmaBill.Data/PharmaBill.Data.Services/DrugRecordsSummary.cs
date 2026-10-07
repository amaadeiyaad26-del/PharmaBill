namespace PharmaBill.Data.Services;

public sealed record DrugRecordsSummary(decimal OpeningBalance, decimal Purchased, decimal Sold, decimal ClosingBalance, decimal CurrentStock, int UniquePatients, bool ClosingMatchesCurrentStock);
