using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PharmaBill.Core.Entities;
using PharmaBill.Core.Security;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Data.Services;

public sealed class OperatorAccountService(IUnitOfWork unitOfWork)
{
	public async Task<IReadOnlyList<OperatorAccountRow>> ListAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		return (await (from user in unitOfWork.Context.AppUsers.AsNoTracking()
			orderby user.UserName
			select user).ToListAsync(cancellationToken)).Select((AppUser user) => new OperatorAccountRow(user.Id, user.UserName, user.DisplayName, PermissionMatrix.ToOperatorLabel(user.Role), user.Role, user.IsActive, user.CreatedAtUtc, user.LastLoginAtUtc)).ToArray();
	}

	public async Task<AppUser> CreateAsync(string userName, string displayName, string password, string roleLabel, Guid actingUserId, UserRole actingRole, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (!PermissionMatrix.Allows(actingRole, AppPermission.ManageUsers))
		{
			throw new UnauthorizedAccessException("Only an Admin can manage operators.");
		}
		ArgumentException.ThrowIfNullOrWhiteSpace(userName, "userName");
		ArgumentException.ThrowIfNullOrWhiteSpace(displayName, "displayName");
		ArgumentException.ThrowIfNullOrWhiteSpace(password, "password");
		if (password.Trim().Length < 4)
		{
			throw new InvalidOperationException("PIN / password must be at least 4 characters.");
		}
		string normalized = userName.Trim().ToLowerInvariant();
		if (await unitOfWork.Context.AppUsers.AnyAsync((AppUser appUser) => appUser.UserName.ToLower() == normalized, cancellationToken))
		{
			throw new InvalidOperationException("Username '" + userName.Trim() + "' is already in use.");
		}
		UserRole role = PermissionMatrix.FromOperatorLabel(roleLabel);
		AppUser user = new AppUser
		{
			UserName = userName.Trim(),
			DisplayName = displayName.Trim(),
			Role = role,
			PasswordHash = PasswordHasher.Hash(password.Trim()),
			IsActive = true,
			IdleLockMinutes = 10
		};
		unitOfWork.Context.AppUsers.Add(user);
		unitOfWork.Context.AuditLogs.Add(new AuditLog
		{
			UserId = actingUserId,
			Action = "OperatorCreated",
			EntityName = "AppUser",
			EntityId = user.Id,
			Details = $"Created {PermissionMatrix.ToOperatorLabel(role)} '{user.UserName}'"
		});
		await unitOfWork.SaveChangesAsync(cancellationToken);
		return user;
	}

	public async Task SetActiveAsync(Guid userId, bool isActive, Guid actingUserId, UserRole actingRole, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (!PermissionMatrix.Allows(actingRole, AppPermission.ManageUsers))
		{
			throw new UnauthorizedAccessException("Only an Admin can manage operators.");
		}
		if (userId == actingUserId && !isActive)
		{
			throw new InvalidOperationException("You cannot deactivate your own account.");
		}
		AppUser appUser = (await unitOfWork.Context.AppUsers.SingleOrDefaultAsync((AppUser item) => item.Id == userId, cancellationToken)) ?? throw new InvalidOperationException("Operator not found.");
		appUser.IsActive = isActive;
		unitOfWork.Context.AuditLogs.Add(new AuditLog
		{
			UserId = actingUserId,
			Action = (isActive ? "OperatorReactivated" : "OperatorDeactivated"),
			EntityName = "AppUser",
			EntityId = appUser.Id,
			Details = appUser.UserName + " → " + (isActive ? "active" : "inactive")
		});
		await unitOfWork.SaveChangesAsync(cancellationToken);
	}

	public async Task ResetPasswordAsync(Guid userId, string newPassword, Guid actingUserId, UserRole actingRole, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (!PermissionMatrix.Allows(actingRole, AppPermission.ManageUsers))
		{
			throw new UnauthorizedAccessException("Only an Admin can reset operator PINs.");
		}
		ArgumentException.ThrowIfNullOrWhiteSpace(newPassword, "newPassword");
		if (newPassword.Trim().Length < 4)
		{
			throw new InvalidOperationException("PIN / password must be at least 4 characters.");
		}
		AppUser appUser = (await unitOfWork.Context.AppUsers.SingleOrDefaultAsync((AppUser item) => item.Id == userId, cancellationToken)) ?? throw new InvalidOperationException("Operator not found.");
		appUser.PasswordHash = PasswordHasher.Hash(newPassword.Trim());
		unitOfWork.Context.AuditLogs.Add(new AuditLog
		{
			UserId = actingUserId,
			Action = "OperatorPasswordReset",
			EntityName = "AppUser",
			EntityId = appUser.Id,
			Details = "PIN reset for " + appUser.UserName
		});
		await unitOfWork.SaveChangesAsync(cancellationToken);
	}

	public async Task ChangeRoleAsync(Guid userId, string roleLabel, Guid actingUserId, UserRole actingRole, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (!PermissionMatrix.Allows(actingRole, AppPermission.ManageUsers))
		{
			throw new UnauthorizedAccessException("Only an Admin can change operator roles.");
		}
		AppUser appUser = (await unitOfWork.Context.AppUsers.SingleOrDefaultAsync((AppUser item) => item.Id == userId, cancellationToken)) ?? throw new InvalidOperationException("Operator not found.");
		UserRole role = PermissionMatrix.FromOperatorLabel(roleLabel);
		string value = PermissionMatrix.ToOperatorLabel(appUser.Role);
		appUser.Role = role;
		unitOfWork.Context.AuditLogs.Add(new AuditLog
		{
			UserId = actingUserId,
			Action = "OperatorRoleChanged",
			EntityName = "AppUser",
			EntityId = appUser.Id,
			Details = $"{appUser.UserName}: {value} → {PermissionMatrix.ToOperatorLabel(role)}"
		});
		await unitOfWork.SaveChangesAsync(cancellationToken);
	}
}
