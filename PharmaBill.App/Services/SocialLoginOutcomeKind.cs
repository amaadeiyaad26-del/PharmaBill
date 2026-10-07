namespace PharmaBill.App.Services;

public enum SocialLoginOutcomeKind
{
	SignedIn,
	SetupRequired,
	LinkAdminRequired,
	Offline,
	Cancelled,
	Error
}
