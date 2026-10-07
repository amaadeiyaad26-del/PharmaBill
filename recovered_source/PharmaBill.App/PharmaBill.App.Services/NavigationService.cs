using System;

namespace PharmaBill.App.Services;

public sealed class NavigationService : INavigationService
{
	public string CurrentSectionKey { get; private set; } = "Dashboard";

	public event EventHandler? SectionChanged;

	public void Navigate(string sectionKey)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(sectionKey, "sectionKey");
		if (!(CurrentSectionKey == sectionKey))
		{
			CurrentSectionKey = sectionKey;
			SectionChanged?.Invoke(this, EventArgs.Empty);
		}
	}
}
