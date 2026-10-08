using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PharmaBill.Data.Services;

namespace PharmaBill.App.Services;

/// <summary>
/// Maps RSA offline licence / trial (LicenseManager) into ISubscriptionProvider
/// without Microsoft Store APIs.
/// </summary>
public sealed class OfflineLicenseSubscriptionProvider(ILogger<OfflineLicenseSubscriptionProvider> logger) : ISubscriptionProvider
{
	public Task<SubscriptionState> GetStateAsync(CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();

		if (LicenseManager.IsClockTampered())
		{
			logger.LogWarning("Offline licence blocked: system clock tamper detected.");
			return Task.FromResult(new SubscriptionState(IsSubscribed: false, IsOfflineGrace: false));
		}

		if (LicenseManager.IsHardwareMismatch())
		{
			logger.LogWarning("Offline licence blocked: hardware fingerprint mismatch (anti-cloning).");
			return Task.FromResult(new SubscriptionState(IsSubscribed: false, IsOfflineGrace: false));
		}

		if (LicenseManager.IsAppActivated())
		{
			logger.LogDebug("Offline licence active: {Status}", LicenseManager.GetStatusSummary());
			return Task.FromResult(new SubscriptionState(IsSubscribed: true, IsOfflineGrace: false));
		}

		if (LicenseManager.IsTrialActive(out int daysRemaining))
		{
			logger.LogInformation("Offline trial active; {Days} day(s) remaining.", daysRemaining);
			return Task.FromResult(new SubscriptionState(IsSubscribed: true, IsOfflineGrace: false));
		}

		logger.LogInformation("Offline licence inactive / expired.");
		return Task.FromResult(new SubscriptionState(IsSubscribed: false, IsOfflineGrace: false));
	}
}