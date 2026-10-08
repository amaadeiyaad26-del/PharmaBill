using Microsoft.Extensions.Logging;
using Windows.Services.Store;

namespace PharmaBill.App.Services;

/// <summary>
/// Reads Microsoft Store licence state via <see cref="StoreContext"/> without touching EF migrations or local DB.
/// </summary>
public sealed class StoreLicenseService(ILogger<StoreLicenseService> logger) : IStoreLicenseService
{
	public async Task<StoreLicenseSnapshot> GetLicenseAsync(CancellationToken cancellationToken = default)
	{
		try
		{
			StoreContext context = StoreContext.GetDefault();
			StoreAppLicense license = await context.GetAppLicenseAsync().AsTask(cancellationToken)
				.ConfigureAwait(continueOnCapturedContext: false);

			if (license is null)
			{
				logger.LogDebug("Store returned no app licence; treating as inactive non-trial.");
				return new StoreLicenseSnapshot(
					IsActive: false,
					IsTrial: false,
					TrialDaysRemaining: null,
					ExpirationUtc: null,
					Source: "StoreUnavailable");
			}

			int? trialDaysRemaining = null;
			DateTimeOffset? expiration = null;
			if (license.IsTrial)
			{
				expiration = license.ExpirationDate;
				TimeSpan remaining = license.ExpirationDate - DateTimeOffset.UtcNow;
				trialDaysRemaining = remaining.TotalDays <= 0
					? 0
					: (int)Math.Ceiling(remaining.TotalDays);
			}

			logger.LogInformation(
				"Store licence IsActive={IsActive} IsTrial={IsTrial} TrialDaysRemaining={TrialDays}",
				license.IsActive,
				license.IsTrial,
				trialDaysRemaining);

			return new StoreLicenseSnapshot(
				IsActive: license.IsActive,
				IsTrial: license.IsTrial,
				TrialDaysRemaining: trialDaysRemaining,
				ExpirationUtc: expiration,
				Source: "MicrosoftStore");
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			logger.LogDebug(exception, "Store licence query failed (expected outside Store packaging).");
			return new StoreLicenseSnapshot(
				IsActive: false,
				IsTrial: false,
				TrialDaysRemaining: null,
				ExpirationUtc: null,
				Source: "Error");
		}
	}
}
