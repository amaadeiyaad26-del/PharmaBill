using System.Windows;

namespace PharmaBill.App.Services;

public sealed class AccountDialogService : IAccountDialogService
{
	public void ShowLicence(LicenceSummary summary)
	{
		LicenceStatusWindow licenceStatusWindow = new LicenceStatusWindow(summary);
		licenceStatusWindow.Owner = Application.Current?.MainWindow;
		licenceStatusWindow.ShowDialog();
	}

	public bool ShowProfile(ProfileSummary summary)
	{
		return new PharmacistProfileWindow(summary)
		{
			Owner = Application.Current?.MainWindow
		}.ShowDialog() == true;
	}
}
