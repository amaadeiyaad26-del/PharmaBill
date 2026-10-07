using System;

namespace PharmaBill.Data.Services;

public sealed record LedgerRow(DateTime EntryAtUtc, string EntryType, string? ReferenceNo, decimal Debit, decimal Credit, decimal Balance, string? Notes);
