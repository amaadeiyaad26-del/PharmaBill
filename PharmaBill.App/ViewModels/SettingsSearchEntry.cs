namespace PharmaBill.App.ViewModels;

public sealed record SettingsSearchEntry(string Id, string Title, string Summary, string Keywords, string Category, string? NavigateSection = null);
