namespace PharmaBill.App.Services;

public interface IUserManualDialogService
{
	void ShowInteractiveGuide();

	string OpenPdfManual();
}
