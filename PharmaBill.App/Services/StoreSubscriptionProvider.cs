using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PharmaBill.Data.Services;

namespace PharmaBill.App.Services;

/// <summary>
/// Maps Microsoft Store licence state into the existing <see cref="ISubscriptionProvider"/> contract
/// used by <c>AccessService</c> (no database / migration changes).
/// </summary>
public sealed class StoreSubscriptionProvider(
	IStoreLicenseService storeLicenseService,
	ILogger<StoreSubscriptionProvider> logger) : ISubscriptionProvider
{
	public async Task<SubscriptionState> GetStateAsync(CancellationToken cancellationToken = default)
	{
		StoreLicenseSnapshot license = await storeLicenseService.GetLicenseAsync(cancellationToken)
			.ConfigureAwait(continueOnCapturedContext: false);

		// Active Store licence (full purchase or still-valid trial) unlocks the workstation.
		if (license.IsActive)
		{
			if (license.IsTrial)
			{
				logger.LogInformation(
					"Store trial active; {Days} day(s) remaining (expires {Expiration}).",
					license.TrialDaysRemaining,
					license.ExpirationUtc);
			}

			return new SubscriptionState(IsSubscribed: true, IsOfflineGrace: false);
		}

		// Outside Store packaging (sideload / debug), Store APIs often report inactive — keep portable builds usable.
		if (license.Source is "StoreUnavailable" or "Error")
		{
			logger.LogDebug("Store licence unavailable ({Source}); treating install as fully licensed for non-Store builds.", license.Source);
			return new SubscriptionState(IsSubscribed: true, IsOfflineGrace: false);
		}

		logger.LogInformation("Store licence inactive and not in trial.");
		return new SubscriptionState(IsSubscribed: false, IsOfflineGrace: false);
	}
}
