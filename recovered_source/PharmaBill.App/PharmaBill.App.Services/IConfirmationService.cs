namespace PharmaBill.App.Services;

public interface IConfirmationService
{
	bool Confirm(string message, string title);

	void Notify(string title, string message);

	void NotifyError(string title, string message);
}
