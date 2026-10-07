using System;

namespace PharmaBill.Data.Services;

public sealed record CashBankBookRow(DateTime AtUtc, string ReceiptNo, string Party, string Direction, string Method, string? Reference, decimal Amount);
