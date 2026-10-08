namespace PharmaBill.App.Services;

/// <summary>
/// Snapshot of the Microsoft Store app licence for trial / full entitlement checks.
/// Does not persist anything to the local database.
/// </summary>
public sealed record StoreLicenseSnapshot(
	bool IsActive,
	bool IsTrial,
	int? TrialDaysRemaining,
	DateTimeOffset? ExpirationUtc,
	string Source);
