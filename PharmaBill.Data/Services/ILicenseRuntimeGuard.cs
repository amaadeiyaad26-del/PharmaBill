namespace PharmaBill.Data.Services;

/// <summary>
/// Optional hook for license clock / anti-tamper touch points after billing or stock commits.
/// </summary>
public interface ILicenseRuntimeGuard
{
	/// <summary>
	/// Records LastVerifiedTimestamp. Returns false when clock tampering is detected.
	/// </summary>
	bool OnTransactionCommitted();
}