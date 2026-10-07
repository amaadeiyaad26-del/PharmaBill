using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PharmaBill.Core.Entities;
using PharmaBill.Core.Security;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Data.Services;

public sealed class BusinessModeService(IUnitOfWork unitOfWork)
{
	public async Task<IReadOnlyList<string>> GetMissingRequirementsAsync(BusinessMode mode, CancellationToken cancellationToken = default(CancellationToken))
	{
		return ModeGuard.GetMissingLicenceTypes(mode, await unitOfWork.Context.LicenceRecords.ToListAsync(cancellationToken), DateOnly.FromDateTime(DateTime.UtcNow));
	}

	public async Task ChangeModeAsync(BusinessMode mode, Guid actingUserId, CancellationToken cancellationToken = default(CancellationToken))
	{
		AppUser appUser = await unitOfWork.Context.AppUsers.SingleOrDefaultAsync((AppUser user) => user.Id == actingUserId && user.IsActive, cancellationToken);
		if (appUser == null || !PermissionMatrix.Allows(appUser.Role, AppPermission.ChangeBusinessMode))
		{
			throw new UnauthorizedAccessException("The current role cannot change the pharmacy business mode.");
		}
		PharmacyProfile profile = await unitOfWork.Context.PharmacyProfiles.SingleAsync(cancellationToken);
		if (profile.BusinessMode == mode)
		{
			return;
		}
		IReadOnlyList<string> readOnlyList = await GetMissingRequirementsAsync(mode, cancellationToken);
		if (readOnlyList.Count != 0)
		{
			throw new InvalidOperationException("Mode change blocked. Missing: " + string.Join(", ", readOnlyList) + ".");
		}
		BusinessMode priorMode = profile.BusinessMode;
		await using IUnitOfWorkTransaction transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
		profile.BusinessMode = mode;
		unitOfWork.Context.AuditLogs.Add(new AuditLog
		{
			UserId = actingUserId,
			ActionAtUtc = DateTime.UtcNow,
			Action = "BusinessModeChanged",
			EntityName = "PharmacyProfile",
			EntityId = profile.Id,
			Details = $"{priorMode} -> {mode}"
		});
		await unitOfWork.SaveChangesAsync(cancellationToken);
		await transaction.CommitAsync(cancellationToken);
	}
}
