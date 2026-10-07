using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PharmaBill.Core.Entities;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Data.Services;

public static class PharmacySetupProbe
{
	public static async Task<bool> IsInitialSetupRequiredAsync(PharmaBillDbContext context, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (!(await context.PharmacyProfiles.AnyAsync(cancellationToken)))
		{
			return true;
		}
		return !(await context.AppUsers.AnyAsync((AppUser user) => user.IsActive && ((int)user.Role == 0 || (int)user.Role == 1), cancellationToken));
	}
}
