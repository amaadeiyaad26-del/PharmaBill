using System;

namespace PharmaBill.Data.Services.Dunning;

public sealed record DunningReceivableRow(Guid CustomerId, string CustomerName, string? Phone, Guid InvoiceId, string InvoiceNo, DateOnly InvoiceDate, DateOnly DueDate, decimal OutstandingAmount, int DaysPastDue, DunningTier Tier, DateOnly OptimalReminderDate, DefaultRiskScore RiskScore, decimal CreditLimit, int CreditDays, double AveragePaymentDelayDays);
