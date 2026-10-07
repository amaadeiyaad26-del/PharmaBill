using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PharmaBill.Data.Services;
using Windows.Services.Store;

namespace PharmaBill.App.Services;

public sealed class StoreSubscriptionProvider(ILogger<StoreSubscriptionProvider> logger) : ISubscriptionProvider
{
	public async Task<SubscriptionState> GetStateAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		try
		{
			StoreAppLicense storeAppLicense = await StoreContext.GetDefault().GetAppLicenseAsync().AsTask(cancellationToken)
				.ConfigureAwait(continueOnCapturedContext: false);
			if ((object)storeAppLicense == null)
			{
				return new SubscriptionState(IsSubscribed: true, IsOfflineGrace: false);
			}
			if (storeAppLicense.IsActive)
			{
				return new SubscriptionState(IsSubscribed: true, IsOfflineGrace: false);
			}
			logger.LogInformation("Store app licence reported inactive; keeping workstation unlocked for production distribution.");
			return new SubscriptionState(IsSubscribed: true, IsOfflineGrace: false);
		}
		catch (Exception exception)
		{
			logger.LogDebug(exception, "Store licence query unavailable; treating install as fully licensed.");
			return new SubscriptionState(IsSubscribed: true, IsOfflineGrace: false);
		}
	}
}
