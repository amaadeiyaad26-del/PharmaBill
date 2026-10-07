using System;
using System.Threading;
using System.Threading.Tasks;
using PharmaBill.Core.Entities;
using PharmaBill.Core.Security;

namespace PharmaBill.Data.Services;

public sealed class AuthorizationService(IEntitlementService entitlementService)
{
	public async Task<bool> CanPerformAsync(UserRole role, AppPermission permission, ProtectedOperation operation, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (!PermissionMatrix.Allows(role, permission))
		{
			return false;
		}
		return await entitlementService.CanPerformAsync(operation, cancellationToken);
	}

	public async Task DemandAsync(UserRole role, AppPermission permission, ProtectedOperation operation, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (!(await CanPerformAsync(role, permission, operation, cancellationToken)))
		{
			throw new UnauthorizedAccessException($"Role '{role}' or the current access status does not allow '{operation}'.");
		}
	}
}
