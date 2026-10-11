namespace PharmaBill.App.ViewModels;

public sealed record NavigationItem(string SectionKey, string Title, bool IsSubItem = false);
