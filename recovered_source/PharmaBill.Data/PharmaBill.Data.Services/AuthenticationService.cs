using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PharmaBill.Core.Entities;
using PharmaBill.Core.Security;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Data.Services;

public sealed class AuthenticationService(PharmaBillDbContext context)
{
	public async Task<AppUser?> AuthenticateAsync(string username, string secret, CancellationToken cancellationToken = default(CancellationToken))
	{
		AppUser user = await context.AppUsers.SingleOrDefaultAsync((AppUser item) => item.UserName == username && item.IsActive, cancellationToken);
		if (user == null || !PasswordHasher.Verify(secret, user.PasswordHash))
		{
			return null;
		}
		user.LastLoginAtUtc = DateTime.UtcNow;
		await context.SaveChangesAsync(cancellationToken);
		return user;
	}
}
