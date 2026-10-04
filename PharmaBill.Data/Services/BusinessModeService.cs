using Microsoft.EntityFrameworkCore;
using PharmaBill.Core.Entities;
using PharmaBill.Core.Security;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Data.Services;

public sealed class BusinessModeService(IUnitOfWork unitOfWork)
{
    public async Task<IReadOnlyList<string>> GetMissingRequirementsAsync(
        BusinessMode mode,
        CancellationToken cancellationToken = default)
    {
        var licences = await unitOfWork.Context.LicenceRecords.ToListAsync(cancellationToken);
        return ModeGuard.GetMissingLicenceTypes(mode, licences, DateOnly.FromDateTime(DateTime.UtcNow));
    }

    public async Task ChangeModeAsync(
        BusinessMode mode,
        Guid actingUserId,
        CancellationToken cancellationToken = default)
    {
        var actingUser = await unitOfWork.Context.AppUsers
            .SingleOrDefaultAsync(user => user.Id == actingUserId && user.IsActive, cancellationToken);
        if (actingUser is null ||
            !PermissionMatrix.Allows(actingUser.Role, AppPermission.ChangeBusinessMode))
        {
            throw new UnauthorizedAccessException("The current role cannot change the pharmacy business mode.");
        }

        var profile = await unitOfWork.Context.PharmacyProfiles.SingleAsync(cancellationToken);
        if (profile.BusinessMode == mode)
        {
            return;
        }

        var missing = await GetMissingRequirementsAsync(mode, cancellationToken);
        if (missing.Count != 0)
        {
            throw new InvalidOperationException($"Mode change blocked. Missing: {string.Join(", ", missing)}.");
        }

        var priorMode = profile.BusinessMode;
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        profile.BusinessMode = mode;
        unitOfWork.Context.AuditLogs.Add(new AuditLog
        {
            UserId = actingUserId,
            ActionAtUtc = DateTime.UtcNow,
            Action = "BusinessModeChanged",
            EntityName = nameof(PharmacyProfile),
            EntityId = profile.Id,
            Details = $"{priorMode} -> {mode}"
        });
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
