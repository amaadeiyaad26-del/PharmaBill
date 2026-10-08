using PharmaBill.Data.Services;

namespace PharmaBill.App.Services;

public sealed class LicenseRuntimeGuard : ILicenseRuntimeGuard
{
	public bool OnTransactionCommitted() => LicenseManager.RecordRunTimestamp();
}