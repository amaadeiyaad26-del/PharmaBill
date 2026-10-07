using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PharmaBill.Core.Entities;
using PharmaBill.Core.Security;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Data.Services;

public sealed class AccessService(PharmaBillDbContext context, ProtectedAccessStateStore stateStore, ISubscriptionProvider subscriptionProvider) : IEntitlementService
{
	public async Task<EntitlementStatus> GetStatusAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		if (!OperatingSystem.IsWindows())
		{
			throw new PlatformNotSupportedException("The entitlement service stores state with Windows DPAPI and Registry.");
		}
		PharmacyProfile profile = await context.PharmacyProfiles.SingleOrDefaultAsync(cancellationToken);
		if (profile == null)
		{
			return EntitlementStatus.EXPIRED;
		}
		DateTime now = DateTime.UtcNow;
		BusinessMode businessMode = profile.BusinessMode;
		if (ModeGuard.GetMissingLicenceTypes(businessMode, await context.LicenceRecords.ToListAsync(cancellationToken), DateOnly.FromDateTime(now)).Count != 0)
		{
			return EntitlementStatus.EXPIRED;
		}
		AccessState protectedState = stateStore.Read();
		DateTime? dateTime = Max(protectedState.LastObservedUtc, profile.LastEntitlementCheckAtUtc);
		if (!dateTime.HasValue || now - dateTime.Value >= TimeSpan.FromHours(12))
		{
			profile.LastEntitlementCheckAtUtc = now;
			await context.SaveChangesAsync(cancellationToken);
			stateStore.Write(new AccessState(protectedState.TrialStartedAtUtc ?? profile.TrialStartedAtUtc ?? profile.CreatedAtUtc, now));
		}
		SubscriptionState subscriptionState = await subscriptionProvider.GetStateAsync(cancellationToken);
		if (subscriptionState.IsSubscribed)
		{
			return EntitlementStatus.SUBSCRIBED;
		}
		if (subscriptionState.IsOfflineGrace)
		{
			return EntitlementStatus.GRACE_OFFLINE;
		}
		return EntitlementStatus.SUBSCRIBED;
	}

	public async Task<bool> IsReadOnlyAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		return await GetStatusAsync(cancellationToken) == EntitlementStatus.EXPIRED;
	}

	public async Task<bool> CanPerformAsync(ProtectedOperation operation, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (await IsReadOnlyAsync(cancellationToken))
		{
			return (uint)operation <= 4u;
		}
		return true;
	}

	private static DateTime? Max(DateTime? first, DateTime? second)
	{
		if (!first.HasValue || !second.HasValue)
		{
			return first ?? second;
		}
		if (!(first.Value >= second.Value))
		{
			return second;
		}
		return first;
	}
}
