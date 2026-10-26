using System.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PharmaBill.Data.Services;

namespace PharmaBill.App.ViewModels;

// The medicine search flyout under the search box: it floats over the bill instead of pushing it down, opens whenever a
// search finds something, and closes on an outside click or Esc. Each row carries its own action.
public sealed partial class RetailBillingViewModel
{
	private bool _isSearchFlyoutOpen;
	private AsyncRelayCommand<RetailStockChoice?>? addSearchResultCommand;
	private AsyncRelayCommand<MedicineSearchResult?>? showSubstitutesForCommand;
	private RelayCommand? reopenSearchFlyoutCommand;

	public bool IsSearchFlyoutOpen
	{
		get => _isSearchFlyoutOpen;
		set => SetProperty(ref _isSearchFlyoutOpen, value);
	}

	public bool HasAnySearchMatches => HasSearchResults || HasCatalogOnlyMatches;

	/// <summary>Adds that row's batch: the cashier picked it, so FEFO does not override the choice.</summary>
	public IAsyncRelayCommand<RetailStockChoice?> AddSearchResultCommand => addSearchResultCommand ??= new AsyncRelayCommand<RetailStockChoice?>(async choice =>
	{
		if (choice == null)
		{
			return;
		}
		SelectedSearchResult = choice;
		NoteExplicitBatchPick();
		await AddMatchedItemAsync();
	});

	/// <summary>Salt substitutes for a drug bank row with no stock.</summary>
	public IAsyncRelayCommand<MedicineSearchResult?> ShowSubstitutesForCommand => showSubstitutesForCommand ??= new AsyncRelayCommand<MedicineSearchResult?>(async medicine =>
	{
		if (medicine == null)
		{
			return;
		}
		SelectedOutOfStockMedicine = medicine;
		IsSearchFlyoutOpen = false;
		await ShowSubstitutesAsync();
	});

	/// <summary>Back in the search box with results still there: show them again.</summary>
	public IRelayCommand ReopenSearchFlyoutCommand => reopenSearchFlyoutCommand ??= new RelayCommand(() =>
	{
		if (HasAnySearchMatches)
		{
			IsSearchFlyoutOpen = true;
		}
	});

	protected override void OnPropertyChanged(PropertyChangedEventArgs e)
	{
		base.OnPropertyChanged(e);
		// Every search and every clear announces these; the flyout follows.
		if (e.PropertyName is nameof(HasSearchResults) or nameof(HasCatalogOnlyMatches))
		{
			base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(HasAnySearchMatches)));
			IsSearchFlyoutOpen = HasAnySearchMatches;
		}
	}
}
