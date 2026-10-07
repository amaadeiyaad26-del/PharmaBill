using PharmaBill.Core.Entities;
using PharmaBill.Core.Services.Auth;

namespace PharmaBill.Data.Services;

public sealed record SocialLoginResolution(SocialLoginResolutionKind Kind, AppUser? ExistingUser = null, SocialAuthProfile? SetupProfile = null, AppUser? LinkableAdmin = null, string? Message = null)
{
	public bool NeedsPharmacySetup
	{
		get
		{
			if (Kind == SocialLoginResolutionKind.SetupRequired)
			{
				return (object)SetupProfile != null;
			}
			return false;
		}
	}
}
