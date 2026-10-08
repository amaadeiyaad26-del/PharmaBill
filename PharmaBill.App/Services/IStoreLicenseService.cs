namespace PharmaBill.App.Services;

public interface IStoreLicenseService
{
	Task<StoreLicenseSnapshot> GetLicenseAsync(CancellationToken cancellationToken = default);
}
