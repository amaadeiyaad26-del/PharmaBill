using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PharmaBill.App.ViewModels;

/// <summary>One tab of a hub: the existing page it shows, unchanged.</summary>
/// <remarks><paramref name="onShow"/> runs when the tab is chosen: several tabs can show one page in different views.</remarks>
public sealed class HubTab(string key, string title, object page, string? toolTip = null, Action? onShow = null) : ObservableObject
{
	private bool _isSelected;
	private string _title = title;

	public string Key { get; } = key;

	public string Title
	{
		get => _title;
		set => SetProperty(ref _title, value);
	}

	public object Page { get; } = page;

	public string? ToolTip { get; } = toolTip;

	public bool IsSelected
	{
		get => _isSelected;
		internal set => SetProperty(ref _isSelected, value);
	}

	internal void Shown() => onShow?.Invoke();
}

/// <summary>
/// A sidebar item that gathers several existing screens as tabs (Stock Management, Accounts &amp; Parties). Each tab
/// shows its page with its own template, commands and data; a tab loads its page each time it is shown, as opening
/// the old screen did. The pages are the same long-lived ones as before, so their state survives tab switches.
/// </summary>
public sealed class HubPageViewModel(string sectionKey, string title, string subtitle) : SectionPageViewModel(title), ILoadablePage
{
	private HubTab? _active;
	private string _subtitle = subtitle;
	private string _errorMessage = string.Empty;
	private RelayCommand<string>? selectTabCommand;

	/// <summary>The sidebar key of this hub.</summary>
	public string SectionKey { get; } = sectionKey;

	public string Subtitle
	{
		get => _subtitle;
		set => SetProperty(ref _subtitle, value);
	}

	public ObservableCollection<HubTab> Tabs { get; } = [];

	public string? ActiveTabKey => _active?.Key;

	/// <summary>The page shown under the tabs.</summary>
	public object? ActivePage => _active?.Page;

	/// <summary>A failure loading the shown tab, for pages that do not show their own.</summary>
	public string ErrorMessage
	{
		get => _errorMessage;
		private set => SetProperty(ref _errorMessage, value);
	}

	public IRelayCommand<string> SelectTabCommand => selectTabCommand ??= new RelayCommand<string>(key => _ = SelectTabAsync(key));

	public bool HasTab(string key) => Tabs.Any(tab => tab.Key == key);

	/// <summary>Sets the tabs (they can depend on the business mode); keeps the shown tab when it is still there.</summary>
	public void SetTabs(IEnumerable<HubTab> tabs)
	{
		string? keep = _active?.Key;
		Tabs.Clear();
		foreach (HubTab tab in tabs)
		{
			Tabs.Add(tab);
		}
		Activate(Tabs.FirstOrDefault(tab => tab.Key == keep) ?? Tabs.FirstOrDefault());
	}

	/// <summary>Chooses the tab the next visit opens on, without loading it.</summary>
	public void ShowTab(string key)
	{
		if (Tabs.FirstOrDefault(tab => tab.Key == key) is HubTab tab)
		{
			Activate(tab);
		}
	}

	public Task SelectTabAsync(string? key)
	{
		if (key != null)
		{
			ShowTab(key);
		}
		return LoadAsync();
	}

	public async Task LoadAsync(CancellationToken cancellationToken = default)
	{
		ErrorMessage = string.Empty;
		if (_active?.Page is not ILoadablePage page)
		{
			return;
		}
		// Tab clicks start this without awaiting: a failure shows under the tabs instead of vanishing.
		try
		{
			await page.LoadAsync(cancellationToken);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			ErrorMessage = PharmaBill.Core.UserMessage.Of(ex);
		}
	}

	private void Activate(HubTab? tab)
	{
		tab?.Shown();
		if (ReferenceEquals(tab, _active))
		{
			return;
		}
		_active = tab;
		foreach (HubTab each in Tabs)
		{
			each.IsSelected = ReferenceEquals(each, tab);
		}
		OnPropertyChanged(nameof(ActiveTabKey));
		OnPropertyChanged(nameof(ActivePage));
	}
}
