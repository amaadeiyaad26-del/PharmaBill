namespace PharmaBill.App.Services;

public interface INavigationService
{
    string CurrentSectionKey { get; }

    event EventHandler? SectionChanged;

    void Navigate(string sectionKey);
}
