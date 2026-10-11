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
		// Accounts are created with a trimmed, case-insensitively unique username and a trimmed secret,
		// so match the same way. The exact secret is tried first for accounts created before trimming existed.
		string normalizedName = username.Trim().ToLowerInvariant();
		AppUser? user = await context.AppUsers.SingleOrDefaultAsync((AppUser item) => item.UserName.ToLower() == normalizedName && item.IsActive, cancellationToken);
		if (user == null || !(PasswordHasher.Verify(secret, user.PasswordHash) || PasswordHasher.Verify(secret.Trim(), user.PasswordHash)))
		{
			return null;
		}
		user.LastLoginAtUtc = DateTime.UtcNow;
		await context.SaveChangesAsync(cancellationToken);
		return user;
	}
}
