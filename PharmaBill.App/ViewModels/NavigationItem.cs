using CommunityToolkit.Mvvm.ComponentModel;

namespace PharmaBill.App.ViewModels;

/// <summary>One sidebar entry. IsActive is true while its screen is open, so the sidebar can light it up.</summary>
public sealed class NavigationItem : ObservableObject
{
	private bool _isActive;

	public NavigationItem(string sectionKey, string title, bool IsSubItem = false)
	{
		SectionKey = sectionKey;
		Title = title;
		this.IsSubItem = IsSubItem;
	}

	public string SectionKey { get; }

	public string Title { get; }

	public bool IsSubItem { get; }

	public bool IsActive
	{
		get => _isActive;
		set => SetProperty(ref _isActive, value);
	}
}
