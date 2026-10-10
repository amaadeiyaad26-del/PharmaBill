namespace PharmaBill.App.Services;

public interface IConfirmationService
{
	bool Confirm(string message, string title);

	bool? Choose(string message, string title, string acceptLabel, string alternateLabel);

	void Notify(string title, string message);

	void NotifyError(string title, string message);
}
