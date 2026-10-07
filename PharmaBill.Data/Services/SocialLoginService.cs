using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PharmaBill.Core.Entities;
using PharmaBill.Core.Security;
using PharmaBill.Core.Services.Auth;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Data.Services;

public sealed class SocialLoginService(PharmaBillDbContext context)
{
	public async Task<SocialLoginResolution> ResolveAsync(SocialAuthProfile profile, CancellationToken cancellationToken = default(CancellationToken))
	{
		ArgumentNullException.ThrowIfNull(profile, "profile");
		AppUser user = await FindLinkedUserAsync(profile, cancellationToken);
		if (user != null)
		{
			user.LastLoginAtUtc = DateTime.UtcNow;
			await context.SaveChangesAsync(cancellationToken);
			return new SocialLoginResolution(SocialLoginResolutionKind.SignedIn, user);
		}
		if (!(await context.AppUsers.AnyAsync((AppUser item) => item.IsActive && !item.IsDeleted, cancellationToken)) | await PharmacySetupProbe.IsInitialSetupRequiredAsync(context, cancellationToken))
		{
			return new SocialLoginResolution(SocialLoginResolutionKind.SetupRequired, null, profile);
		}
		AppUser appUser = await FindPrimaryUnlinkedAdminAsync(cancellationToken);
		if (appUser != null)
		{
			return new SocialLoginResolution(SocialLoginResolutionKind.LinkPrimaryAdmin, null, profile, appUser);
		}
		return new SocialLoginResolution(SocialLoginResolutionKind.Unlinked, null, null, null, "No PharmaBill account is linked to this Google sign-in. Ask your Admin to add you in Settings → Users & Operators.");
	}

	public async Task<AppUser> LinkPrimaryAdminAsync(Guid adminId, SocialAuthProfile profile, string adminSecret, CancellationToken cancellationToken = default(CancellationToken))
	{
		ArgumentNullException.ThrowIfNull(profile, "profile");
		ArgumentException.ThrowIfNullOrWhiteSpace(adminSecret, "adminSecret");
		AppUser admin = (await context.AppUsers.SingleOrDefaultAsync((AppUser user) => user.Id == adminId && user.IsActive && !user.IsDeleted && ((int)user.Role == 0 || (int)user.Role == 1), cancellationToken)) ?? throw new InvalidOperationException("Primary Admin account was not found.");
		if (!PasswordHasher.Verify(adminSecret.Trim(), admin.PasswordHash))
		{
			throw new InvalidOperationException("Incorrect Admin PIN or password.");
		}
		if (!string.IsNullOrWhiteSpace(admin.AuthProvider) && !string.IsNullOrWhiteSpace(admin.ProviderSubjectId) && (!string.Equals(admin.AuthProvider, profile.Provider, StringComparison.OrdinalIgnoreCase) || !string.Equals(admin.ProviderSubjectId, profile.SubjectId, StringComparison.Ordinal)))
		{
			throw new InvalidOperationException("This Admin account is already linked to a different Google sign-in.");
		}
		admin.AuthProvider = profile.Provider;
		admin.ProviderSubjectId = profile.SubjectId;
		if (!string.IsNullOrWhiteSpace(profile.Email))
		{
			admin.Email = profile.Email.Trim();
		}
		if (!string.IsNullOrWhiteSpace(profile.Name) && string.IsNullOrWhiteSpace(admin.DisplayName))
		{
			admin.DisplayName = profile.Name.Trim();
		}
		admin.UpdatedAtUtc = DateTime.UtcNow;
		admin.LastLoginAtUtc = DateTime.UtcNow;
		context.AuditLogs.Add(new AuditLog
		{
			UserId = admin.Id,
			ActionAtUtc = DateTime.UtcNow,
			Action = "SocialAccountLinked",
			EntityName = "AppUser",
			EntityId = admin.Id,
			Details = profile.Provider + " linked (" + (profile.Email ?? profile.SubjectId) + ")"
		});
		await context.SaveChangesAsync(cancellationToken);
		return admin;
	}

	private async Task<AppUser?> FindPrimaryUnlinkedAdminAsync(CancellationToken cancellationToken)
	{
		return (await (from user in context.AppUsers
			where user.IsActive && !user.IsDeleted && ((int)user.Role == 0 || (int)user.Role == 1)
			orderby user.Role, user.CreatedAtUtc
			select user).ToListAsync(cancellationToken)).FirstOrDefault((AppUser user) => string.IsNullOrWhiteSpace(user.AuthProvider) || string.IsNullOrWhiteSpace(user.ProviderSubjectId));
	}

	private async Task<AppUser?> FindLinkedUserAsync(SocialAuthProfile profile, CancellationToken cancellationToken)
	{
		AppUser appUser = await context.AppUsers.Where((AppUser user) => user.IsActive && !user.IsDeleted && user.AuthProvider == profile.Provider && user.ProviderSubjectId == profile.SubjectId).SingleOrDefaultAsync(cancellationToken);
		if (appUser != null)
		{
			return appUser;
		}
		if (string.IsNullOrWhiteSpace(profile.Email))
		{
			return null;
		}
		string normalizedEmail = profile.Email.Trim();
		AppUser appUser2 = await context.AppUsers.Where((AppUser user) => user.IsActive && !user.IsDeleted && user.Email == normalizedEmail).SingleOrDefaultAsync(cancellationToken);
		if (appUser2 == null)
		{
			return null;
		}
		appUser2.AuthProvider = profile.Provider;
		appUser2.ProviderSubjectId = profile.SubjectId;
		if (string.IsNullOrWhiteSpace(appUser2.Email))
		{
			appUser2.Email = normalizedEmail;
		}
		return appUser2;
	}
}
