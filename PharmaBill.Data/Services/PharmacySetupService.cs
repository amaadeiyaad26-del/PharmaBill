using Microsoft.EntityFrameworkCore;
using PharmaBill.Core.Entities;
using PharmaBill.Core.Security;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Data.Services;

public sealed class PharmacySetupService(
    IUnitOfWork unitOfWork,
    ProtectedAccessStateStore protectedAccessStateStore)
{
    public async Task<AppUser> CompleteSetupAsync(
        PharmacyProfile profile,
        IEnumerable<LicenceRecord> licences,
        AppUser owner,
        string ownerSecret,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerSecret);

        var licenceList = licences.ToList();
        var missing = ModeGuard.GetMissingLicenceTypes(
            profile.BusinessMode,
            licenceList,
            DateOnly.FromDateTime(DateTime.UtcNow));
        if (missing.Count > 0)
        {
            throw new InvalidOperationException($"Setup requires: {string.Join(", ", missing)}.");
        }

        if (await unitOfWork.Context.PharmacyProfiles.AnyAsync(cancellationToken))
        {
            throw new InvalidOperationException("First-run setup has already been completed.");
        }

        var now = DateTime.UtcNow;
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Trial state is protected with Windows DPAPI and Registry.");
        }

        profile.TrialStartedAtUtc = now;
        profile.LastEntitlementCheckAtUtc = now;
        owner.Role = UserRole.Owner;
        owner.PasswordHash = PasswordHasher.Hash(ownerSecret);
        owner.IsActive = true;
        owner.IdleLockMinutes = Math.Clamp(owner.IdleLockMinutes, 1, 240);

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        unitOfWork.Context.PharmacyProfiles.Add(profile);
        unitOfWork.Context.LicenceRecords.AddRange(licenceList);
        unitOfWork.Context.AppUsers.Add(owner);
        unitOfWork.Context.AuditLogs.Add(new AuditLog
        {
            UserId = owner.Id,
            ActionAtUtc = now,
            Action = "FirstRunSetupCompleted",
            EntityName = nameof(PharmacyProfile),
            EntityId = profile.Id,
            Details = $"Business mode: {profile.BusinessMode}"
        });
        await unitOfWork.SaveChangesAsync(cancellationToken);
        protectedAccessStateStore.Write(new AccessState(now, now));
        await transaction.CommitAsync(cancellationToken);
        return owner;
    }
}
