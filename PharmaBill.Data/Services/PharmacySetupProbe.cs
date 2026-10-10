using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PharmaBill.Core.Entities;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Data.Services;

public static class PharmacySetupProbe
{
	public static async Task<bool> IsInitialSetupRequiredAsync(PharmaBillDbContext context, CancellationToken cancellationToken = default(CancellationToken))
	{
		try
		{
			if (!(await context.PharmacyProfiles.AnyAsync(cancellationToken)))
			{
				return true;
			}
			return !(await context.AppUsers.AnyAsync((AppUser user) => user.IsActive && ((int)user.Role == 0 || (int)user.Role == 1), cancellationToken));
		}
		catch (SqliteException ex) when (IsMissingTable(ex))
		{
			// Schema not created yet (wiped DB / migrate not run) — treat as first-run setup.
			return true;
		}
		catch (Exception ex) when (ex.InnerException is SqliteException inner && IsMissingTable(inner))
		{
			return true;
		}
	}

	private static bool IsMissingTable(SqliteException ex)
	{
		return ex.SqliteErrorCode == 1
			&& ex.Message.Contains("no such table", StringComparison.OrdinalIgnoreCase);
	}
}
