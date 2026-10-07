using System.Windows;

namespace PharmaBill.App.Services;

public sealed class ConfirmationService : IConfirmationService
{
	public bool Confirm(string message, string title)
	{
		return MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Exclamation) == MessageBoxResult.Yes;
	}

	public void Notify(string title, string message)
	{
		MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Asterisk);
	}

	public void NotifyError(string title, string message)
	{
		MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Hand);
	}
}
