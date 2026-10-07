namespace PharmaBill.App.Services;

public sealed record LicenceSummary(string Status, string Headline, string Detail, int TrialDaysTotal, int? TrialDaysLeft, string? TrialEndsOn);
