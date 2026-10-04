using Microsoft.EntityFrameworkCore;
using PharmaBill.Core.Entities;
using PharmaBill.Core.Security;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Data.Services;

public sealed class AccessService(
    PharmaBillDbContext context,
    ProtectedAccessStateStore stateStore,
    ISubscriptionProvider subscriptionProvider) : IEntitlementService
{
    public const int TRIAL_DAYS = TrialClock.TRIAL_DAYS;

    public async Task<EntitlementStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("The entitlement service stores state with Windows DPAPI and Registry.");
        }

        var subscription = await subscriptionProvider.GetStateAsync(cancellationToken);
        var profile = await context.PharmacyProfiles.SingleOrDefaultAsync(cancellationToken);
        if (profile is null)
        {
            return EntitlementStatus.EXPIRED;
        }

        var now = DateTime.UtcNow;
        var protectedState = stateStore.Read();
        var starts = new List<DateTime>();
        if (protectedState.TrialStartedAtUtc.HasValue)
        {
            starts.Add(protectedState.TrialStartedAtUtc.Value);
        }

        if (profile.TrialStartedAtUtc.HasValue)
        {
            starts.Add(profile.TrialStartedAtUtc.Value);
        }

        if (starts.Count == 0)
        {
            starts.Add(now);
        }

        var trial = TrialClock.Evaluate(
            now,
            starts,
            Max(protectedState.LastObservedUtc, profile.LastEntitlementCheckAtUtc));
        var trialStart = starts.Min();
        profile.TrialStartedAtUtc = trialStart;
        profile.ClockRollbackDetected |= protectedState.ClockRollbackDetected || trial.ClockMovedBackwards;
        var observedAt = Max(protectedState.LastObservedUtc, profile.LastEntitlementCheckAtUtc);
        if (trial.ClockMovedBackwards || observedAt is null || now - observedAt.Value >= TimeSpan.FromMinutes(1))
        {
            profile.LastEntitlementCheckAtUtc = now > (protectedState.LastObservedUtc ?? DateTime.MinValue)
                ? now
                : protectedState.LastObservedUtc;
            await context.SaveChangesAsync(cancellationToken);
            stateStore.Write(new AccessState(
                trialStart,
                Max(protectedState.LastObservedUtc, now),
                profile.ClockRollbackDetected));
        }
        else if (!protectedState.TrialStartedAtUtc.HasValue)
        {
            stateStore.Write(new AccessState(trialStart, observedAt, profile.ClockRollbackDetected));
        }

        if (profile.ClockRollbackDetected || !trial.IsTrialActive || trial.ClockMovedBackwards)
        {
            return EntitlementStatus.EXPIRED;
        }

        var missing = ModeGuard.GetMissingLicenceTypes(
            profile.BusinessMode,
            await context.LicenceRecords.ToListAsync(cancellationToken),
            DateOnly.FromDateTime(now));
        if (missing.Count != 0)
        {
            return EntitlementStatus.EXPIRED;
        }

        if (subscription.IsSubscribed)
        {
            return EntitlementStatus.SUBSCRIBED;
        }

        if (subscription.IsOfflineGrace)
        {
            return EntitlementStatus.GRACE_OFFLINE;
        }

        return EntitlementStatus.TRIAL;
    }

    public async Task<bool> IsReadOnlyAsync(CancellationToken cancellationToken = default) =>
        await GetStatusAsync(cancellationToken) == EntitlementStatus.EXPIRED;

    public async Task<bool> CanPerformAsync(
        ProtectedOperation operation,
        CancellationToken cancellationToken = default)
    {
        if (await IsReadOnlyAsync(cancellationToken))
        {
            return operation is ProtectedOperation.View or ProtectedOperation.Search or
                ProtectedOperation.Print or ProtectedOperation.Export or ProtectedOperation.Backup;
        }

        return true;
    }

    private static DateTime? Max(DateTime? first, DateTime? second) =>
        first.HasValue && second.HasValue
            ? (first.Value >= second.Value ? first : second)
            : first ?? second;
}
