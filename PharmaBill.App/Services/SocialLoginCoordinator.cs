using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using PharmaBill.Core.Entities;
using PharmaBill.Core.Services.Auth;
using PharmaBill.Data.Persistence;
using PharmaBill.Data.Services;

namespace PharmaBill.App.Services;

public sealed class SocialLoginCoordinator(IServiceScopeFactory scopeFactory, SocialAuthService socialAuthService, CurrentSession currentSession, DatabaseStorageOptions storage)
{
	public async Task<SocialLoginOutcome> SignInWithGoogleAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		return await SignInAsync(() => socialAuthService.SignInWithGoogleAsync(Path.Combine(storage.RootDirectory, "google-signin-tokens"), cancellationToken), cancellationToken);
	}

	public async Task<SocialLoginOutcome> LinkPrimaryAdminAsync(Guid adminId, SocialAuthProfile profile, string adminSecret, CancellationToken cancellationToken = default(CancellationToken))
	{
		try
		{
			using IServiceScope scope = scopeFactory.CreateScope();
			AppUser user = await scope.ServiceProvider.GetRequiredService<SocialLoginService>().LinkPrimaryAdminAsync(adminId, profile, adminSecret, cancellationToken);
			currentSession.SignIn(user);
			return new SocialLoginOutcome(SocialLoginOutcomeKind.SignedIn, profile);
		}
		catch (Exception ex)
		{
			return new SocialLoginOutcome(SocialLoginOutcomeKind.Error, null, ex.Message);
		}
	}

	private async Task<SocialLoginOutcome> SignInAsync(Func<Task<SocialAuthProfile>> acquireProfile, CancellationToken cancellationToken)
	{
		if (!(await SocialAuthService.IsOnlineAsync(cancellationToken)))
		{
			return new SocialLoginOutcome(SocialLoginOutcomeKind.Offline, null, "Internet connection required for Google sign-in. Please use your local PIN or password to sign in offline.");
		}
		SocialAuthProfile profile;
		try
		{
			profile = await acquireProfile();
		}
		catch (OperationCanceledException)
		{
			return new SocialLoginOutcome(SocialLoginOutcomeKind.Cancelled);
		}
		catch (Exception ex2)
		{
			return new SocialLoginOutcome(SocialLoginOutcomeKind.Error, null, ex2.Message);
		}
		using IServiceScope scope = scopeFactory.CreateScope();
		SocialLoginService requiredService = scope.ServiceProvider.GetRequiredService<SocialLoginService>();
		try
		{
			SocialLoginResolution socialLoginResolution = await requiredService.ResolveAsync(profile, cancellationToken);
			SocialLoginOutcome result;
			switch (socialLoginResolution.Kind)
			{
			case SocialLoginResolutionKind.SignedIn:
			{
				AppUser existingUser = socialLoginResolution.ExistingUser;
				if (existingUser != null)
				{
					result = SignInExisting(existingUser);
					break;
				}
				goto default;
			}
			case SocialLoginResolutionKind.SetupRequired:
				if ((object)socialLoginResolution.SetupProfile != null)
				{
					result = new SocialLoginOutcome(SocialLoginOutcomeKind.SetupRequired, socialLoginResolution.SetupProfile);
					break;
				}
				goto default;
			case SocialLoginResolutionKind.LinkPrimaryAdmin:
			{
				AppUser linkableAdmin = socialLoginResolution.LinkableAdmin;
				if (linkableAdmin != null && (object)socialLoginResolution.SetupProfile != null)
				{
					result = new SocialLoginOutcome(SocialLoginOutcomeKind.LinkAdminRequired, socialLoginResolution.SetupProfile, null, linkableAdmin.Id, linkableAdmin.DisplayName);
					break;
				}
				goto default;
			}
			case SocialLoginResolutionKind.Unlinked:
				result = new SocialLoginOutcome(SocialLoginOutcomeKind.Error, null, socialLoginResolution.Message ?? "No PharmaBill account is linked to this Google sign-in.");
				break;
			default:
				result = new SocialLoginOutcome(SocialLoginOutcomeKind.Error, null, "Could not complete social sign-in.");
				break;
			}
			return result;
		}
		catch (Exception ex3)
		{
			return new SocialLoginOutcome(SocialLoginOutcomeKind.Error, null, ex3.Message);
		}
	}

	private SocialLoginOutcome SignInExisting(AppUser user)
	{
		currentSession.SignIn(user);
		return new SocialLoginOutcome(SocialLoginOutcomeKind.SignedIn);
	}
}
