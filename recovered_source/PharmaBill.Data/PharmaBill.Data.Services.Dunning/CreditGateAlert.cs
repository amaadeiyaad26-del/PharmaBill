namespace PharmaBill.Data.Services.Dunning;

public sealed record CreditGateAlert(bool IsBlocked, bool ExceedsCreditLimit, bool HasActiveT4, string Message, CustomerCreditProfile? Profile);
