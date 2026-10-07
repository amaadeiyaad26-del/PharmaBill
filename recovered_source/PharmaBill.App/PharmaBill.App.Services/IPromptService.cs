namespace PharmaBill.App.Services;

public interface IPromptService
{
	string? AskText(string title, string message, string label, string? initialText = null);
}
