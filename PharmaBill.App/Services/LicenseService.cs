using System;

namespace PharmaBill.App.Services;

/// <summary>
/// DI-friendly facade over RSA offline licensing (public-key verify only in client builds).
/// </summary>
public sealed class LicenseService
{
	public string MachineId => LicenseManager.GetMachineId();

	public string StatusSummary => LicenseManager.GetStatusSummary();

	public bool HasFullAccess => LicenseManager.HasFullAccess();

	public bool IsActivated => LicenseManager.IsAppActivated();

	public bool IsClockTampered => LicenseManager.IsClockTampered();

	public bool IsHardwareMismatch => LicenseManager.IsHardwareMismatch();

	public DateTime? ValidUntilUtc => LicenseManager.GetLicensedUntilUtc();

	public int? TrialDaysRemaining => LicenseManager.GetTrialDaysRemaining();

	public int? DaysUntilExpiry => LicenseManager.GetDaysUntilExpiry();

	public bool IsWithinExpiryWarningWindow => LicenseManager.IsWithinExpiryWarningWindow();

	public string? ExpiryWarningMessage => LicenseManager.GetExpiryWarningMessage();

	public void InvalidateCache() => LicenseManager.InvalidateCache();

	public bool RecordRunTimestamp() => LicenseManager.RecordRunTimestamp();

	public bool TryActivate(string licenseKey, out string message)
		=> LicenseManager.ActivateLicense(licenseKey, out message);

	public string GenerateKey(string machineId, LicenseManager.LicenseDuration duration, DateTime? asOfUtc = null)
		=> LicenseManager.GenerateKey(machineId, duration, asOfUtc);

	public bool ValidateKey(string machineId, string licenseKey, out string message)
		=> LicenseManager.ValidateLicense(machineId, licenseKey, out message);

	public bool CanGenerateAdminKeys => Licensing.AdminKeyGenerator.CanSign;

	public LicenceUiState GetUiState()
	{
		string? warning = ExpiryWarningMessage;
		bool expired = !HasFullAccess && !LicenseManager.IsTrialActive(out _);
		bool warningOnly = IsWithinExpiryWarningWindow && IsActivated;
		return new LicenceUiState(
			StatusSummary: StatusSummary,
			MachineId: MachineId,
			ValidUntilUtc: ValidUntilUtc,
			BannerText: warning ?? string.Empty,
			ShowExpiryWarning: warningOnly,
			ShowExpiredBanner: expired || IsClockTampered || IsHardwareMismatch,
			ShowClockTamper: IsClockTampered,
			HasFullAccess: HasFullAccess,
			IsActivated: IsActivated,
			TrialDaysRemaining: TrialDaysRemaining);
	}
}

public sealed record LicenceUiState(
	string StatusSummary,
	string MachineId,
	DateTime? ValidUntilUtc,
	string BannerText,
	bool ShowExpiryWarning,
	bool ShowExpiredBanner,
	bool ShowClockTamper,
	bool HasFullAccess,
	bool IsActivated,
	int? TrialDaysRemaining);