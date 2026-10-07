using System;

namespace PharmaBill.Data.Services.Dunning;

public sealed record CustomerCreditProfile(Guid CustomerId, string CustomerName, string? Phone, string? Gstin, double AveragePaymentDelayDays, DefaultRiskScore DefaultRiskScore, decimal CreditLimit, int CreditDays, decimal OutstandingAmount, decimal OverdueAmount, bool ExceedsCreditLimit, bool HasActiveT4);
