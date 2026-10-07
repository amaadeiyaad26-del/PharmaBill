namespace PharmaBill.Data.Services;

public sealed record RetailPaymentInput(string Method, decimal AppliedAmount, decimal TenderedAmount = 0m);
