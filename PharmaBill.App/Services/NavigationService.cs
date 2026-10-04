namespace PharmaBill.App.Services;

public sealed class NavigationService : INavigationService
{
    public string CurrentSectionKey { get; private set; } = "Dashboard";

    public event EventHandler? SectionChanged;

    public void Navigate(string sectionKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sectionKey);
        if (CurrentSectionKey == sectionKey)
        {
            return;
        }

        CurrentSectionKey = sectionKey;
        SectionChanged?.Invoke(this, EventArgs.Empty);
    }
}
