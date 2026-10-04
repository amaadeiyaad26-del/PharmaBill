namespace PharmaBill.App.Services;

public interface IPromptService
{
    // Returns the trimmed text, or null when the user cancels.
    string? AskText(string title, string message, string label, string? initialText = null);
}

public sealed class PromptService : IPromptService
{
    public string? AskText(string title, string message, string label, string? initialText = null)
    {
        var window = new TextPromptWindow(title, message, label, initialText)
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };
        return window.ShowDialog() == true ? window.Value : null;
    }
}