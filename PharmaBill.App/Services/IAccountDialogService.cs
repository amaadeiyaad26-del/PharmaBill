namespace PharmaBill.App.Services;

public interface IAccountDialogService
{
	void ShowLicence(LicenceSummary summary);

	bool ShowProfile(ProfileSummary summary);
}
