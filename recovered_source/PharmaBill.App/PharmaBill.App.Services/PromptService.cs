using System.Windows;

namespace PharmaBill.App.Services;

public sealed class PromptService : IPromptService
{
	public string? AskText(string title, string message, string label, string? initialText = null)
	{
		TextPromptWindow textPromptWindow = new TextPromptWindow(title, message, label, initialText)
		{
			Owner = Application.Current?.MainWindow
		};
		if (textPromptWindow.ShowDialog() != true)
		{
			return null;
		}
		return textPromptWindow.Value;
	}
}
