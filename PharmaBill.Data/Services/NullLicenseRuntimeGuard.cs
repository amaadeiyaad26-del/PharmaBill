namespace PharmaBill.Data.Services;

public sealed class NullLicenseRuntimeGuard : ILicenseRuntimeGuard
{
	public bool OnTransactionCommitted() => true;
}