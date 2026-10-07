using System;

namespace PharmaBill.Data.Services.Reconciliation;

public sealed record BankStatementEntry(DateOnly Date, string? ReferenceOrUtr, string Narration, decimal DepositAmount, int LineIndex = 0);
