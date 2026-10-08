using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PharmaBill.App.Services;
using PharmaBill.Core.Ai;
using PharmaBill.Data.Services;

namespace PharmaBill.App.ViewModels;

public class CatalogSearchViewModel : ObservableObject, IDisposable
{
	private readonly IServiceScopeFactory _scopeFactory;

	private readonly ILogger<CatalogSearchViewModel> _logger;

	private readonly IAddStockDialogService? _addStockDialog;

	private readonly IQuickAddMedicineDialog? _quickAdd;

	private readonly ISmartDrugLookupService? _smartLookup;

	private bool _showOnlineLookup;

	private CancellationTokenSource? _searchCancellation;

	private string _query = string.Empty;

	private const string DebounceMarker = "debounce";

	private const int SearchDebounceMs = 180;

	private bool _isVisible;

	private bool _isSearching;

	private string _errorMessage = string.Empty;

	private bool _showHint = true;

	private string _hintText = "Type at least 2 letters to search";

	private bool _showAddManual = true;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand<MedicineSearchResult?>? addStockCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? addManualCommand;

	private AsyncRelayCommand? addMedicineCommand;

	private AsyncRelayCommand? searchOnlineCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand<MedicineSearchResult?>? viewStockCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand<object?>? searchCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand<MedicineSearchResult?>? showSubstitutesCommand;

	public ObservableCollection<MedicineSearchResult> InStockResults { get; } = new ObservableCollection<MedicineSearchResult>();

	public ObservableCollection<MedicineSearchResult> CatalogResults { get; } = new ObservableCollection<MedicineSearchResult>();

	public ObservableCollection<MedicineSearchResult> SubstituteResults { get; } = new ObservableCollection<MedicineSearchResult>();

	public bool HasInStock => InStockResults.Count > 0;

	public bool HasCatalog => CatalogResults.Count > 0;

	public bool HasSubstitutes => SubstituteResults.Count > 0;

	public string SubstituteNote => "Pharmacist must confirm suitability and prescription rules";

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string Query
	{
		get
		{
			return _query;
		}
		[MemberNotNull("_query")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_query, value))
			{
				OnPropertyChanging(nameof(Query));
				_query = value;
				OnQueryChanged(value);
				OnPropertyChanged(nameof(Query));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool IsVisible
	{
		get
		{
			return _isVisible;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_isVisible, value))
			{
				OnPropertyChanging(nameof(IsVisible));
				_isVisible = value;
				OnPropertyChanged(nameof(IsVisible));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool IsSearching
	{
		get
		{
			return _isSearching;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_isSearching, value))
			{
				OnPropertyChanging(nameof(IsSearching));
				_isSearching = value;
				OnPropertyChanged(nameof(IsSearching));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string ErrorMessage
	{
		get
		{
			return _errorMessage;
		}
		[MemberNotNull("_errorMessage")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_errorMessage, value))
			{
				OnPropertyChanging(nameof(ErrorMessage));
				_errorMessage = value;
				OnPropertyChanged(nameof(ErrorMessage));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool ShowHint
	{
		get
		{
			return _showHint;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_showHint, value))
			{
				OnPropertyChanging(nameof(ShowHint));
				_showHint = value;
				OnPropertyChanged(nameof(ShowHint));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string HintText
	{
		get
		{
			return _hintText;
		}
		[MemberNotNull("_hintText")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_hintText, value))
			{
				OnPropertyChanging(nameof(HintText));
				_hintText = value;
				OnPropertyChanged(nameof(HintText));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool ShowAddManual
	{
		get
		{
			return _showAddManual;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_showAddManual, value))
			{
				OnPropertyChanging(nameof(ShowAddManual));
				_showAddManual = value;
				OnPropertyChanged(nameof(ShowAddManual));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand<MedicineSearchResult?> AddStockCommand => addStockCommand ?? (addStockCommand = new AsyncRelayCommand<MedicineSearchResult>(AddStockAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand AddManualCommand => addManualCommand ?? (addManualCommand = new AsyncRelayCommand(() => OpenQuickAddAsync(Query.Trim())));

	public IAsyncRelayCommand AddMedicineCommand => addMedicineCommand ?? (addMedicineCommand = new AsyncRelayCommand(() => OpenQuickAddAsync(string.Empty)));

	public IAsyncRelayCommand SearchOnlineCommand => searchOnlineCommand ?? (searchOnlineCommand = new AsyncRelayCommand(SearchOnlineAsync));

	public bool ShowOnlineLookup => _showOnlineLookup;

	public string OnlineLookupCaption => SmartDrugLookupPrompt.Caption;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand<MedicineSearchResult?> ViewStockCommand => viewStockCommand ?? (viewStockCommand = new RelayCommand<MedicineSearchResult>(ViewStock));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand<object?> SearchCommand => searchCommand ?? (searchCommand = new AsyncRelayCommand<object>(SearchAsync, AsyncRelayCommandOptions.AllowConcurrentExecutions));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand<MedicineSearchResult?> ShowSubstitutesCommand => showSubstitutesCommand ?? (showSubstitutesCommand = new AsyncRelayCommand<MedicineSearchResult>(ShowSubstitutesAsync));

	public event EventHandler? StockChanged;

	public event EventHandler<string>? ViewStockRequested;

	public string AddManualCaption => "+ New Medicine";

	public bool ShowCreateUnlisted
	{
		get
		{
			string text = Query.Trim();
			return text.Length >= 2
				&& InStockResults.Count == 0
				&& CatalogResults.Count == 0
				&& !string.IsNullOrEmpty(HintText)
				&& HintText.StartsWith("No match", StringComparison.Ordinal);
		}
	}

	public string CreateUnlistedCaption => "+ Create '" + Query.Trim() + "' as New Medicine";

	public CatalogSearchViewModel(IServiceScopeFactory scopeFactory, ILogger<CatalogSearchViewModel> logger, IAddStockDialogService? addStockDialog = null, IQuickAddMedicineDialog? quickAdd = null, ISmartDrugLookupService? smartLookup = null)
	{
		_scopeFactory = scopeFactory;
		_logger = logger;
		_addStockDialog = addStockDialog;
		_quickAdd = quickAdd;
		_smartLookup = smartLookup;
	}

	private void RefreshHint(bool searched)
	{
		OnPropertyChanged("HasInStock");
		OnPropertyChanged("HasCatalog");
		string text = Query.Trim();
		if (text.Length == 0)
		{
			HintText = "Type at least 2 letters to search";
			ShowAddManual = true;
			ShowHint = true;
			SetOnlineLookup(false);
			NotifyCreateUnlisted();
		}
		else if (text.Length < 2)
		{
			HintText = "Type at least 2 letters to search";
			ShowAddManual = true;
			ShowHint = true;
			SetOnlineLookup(false);
			NotifyCreateUnlisted();
		}
		else
		{
			bool flag = searched && InStockResults.Count == 0 && CatalogResults.Count == 0;
			HintText = (flag ? ("No match in stock or catalogue for '" + text + "'") : string.Empty);
			ShowAddManual = true;
			ShowHint = flag;
			SetOnlineLookup(flag);
			NotifyCreateUnlisted();
		}
	}

	private void SetOnlineLookup(bool visible)
	{
		if (_showOnlineLookup == visible)
		{
			return;
		}

		_showOnlineLookup = visible;
		OnPropertyChanged(nameof(ShowOnlineLookup));
	}

	private async Task AddStockAsync(MedicineSearchResult? medicine)
	{
		if (_addStockDialog != null && (object)medicine != null && await _addStockDialog.ShowAsync(new AddStockRequest(null, medicine.CatalogMedicineId, medicine.Name, medicine.Composition, medicine.Manufacturer)))
		{
			StockChanged?.Invoke(this, EventArgs.Empty);
			await SearchAsync(null);
		}
	}

	private async Task SearchOnlineAsync()
	{
		string typed = Query.Trim();
		SmartDrugLookupOutcome outcome = _smartLookup == null
			? new SmartDrugLookupOutcome(null, "Online lookup is not connected.")
			: await _smartLookup.LookupAsync(typed);
		await OpenQuickAddAsync(outcome.Suggestion?.BrandName ?? typed, outcome.Suggestion, outcome.Message);
	}

	private async Task OpenQuickAddAsync(string brandName, SmartDrugSuggestion? suggestion = null, string? notice = null)
	{
		if (_quickAdd == null)
		{
			if (_addStockDialog != null && await _addStockDialog.ShowAsync(new AddStockRequest(null, null, brandName, null, null)))
			{
				StockChanged?.Invoke(this, EventArgs.Empty);
				await SearchAsync(null);
			}

			return;
		}

		CustomMedicineResult? saved = await _quickAdd.ShowAsync(brandName, suggestion, notice);
		if (saved == null)
		{
			return;
		}

		if (_addStockDialog != null)
		{
			await _addStockDialog.ShowAsync(new AddStockRequest(saved.DrugId, saved.CatalogMedicineId, saved.Name, saved.GenericName, null));
		}

		StockChanged?.Invoke(this, EventArgs.Empty);
		Query = saved.Name;
		await SearchAsync(null);
	}

	private void ViewStock(MedicineSearchResult? medicine)
	{
		if ((object)medicine != null)
		{
			ViewStockRequested?.Invoke(this, medicine.Name);
		}
	}

	private async Task SearchAsync(object? mode)
	{
		_searchCancellation?.Cancel();
		_searchCancellation?.Dispose();
		_searchCancellation = new CancellationTokenSource();
		CancellationToken token = _searchCancellation.Token;
		ErrorMessage = string.Empty;
		bool flag = mode == "debounce";
		if (Query.Trim().Length < 2)
		{
			IsSearching = false;
			InStockResults.Clear();
			CatalogResults.Clear();
			SubstituteResults.Clear();
			OnPropertyChanged("HasSubstitutes");
			RefreshHint(searched: false);
			return;
		}
		try
		{
			if (flag)
			{
				await Task.Delay(180, token);
			}
			IsSearching = true;
			using IServiceScope scope = _scopeFactory.CreateScope();
			MedicineSearchResults medicineSearchResults = await scope.ServiceProvider.GetRequiredService<CatalogSearchService>().SearchAsync(Query.Trim(), token);
			token.ThrowIfCancellationRequested();
			InStockResults.Clear();
			CatalogResults.Clear();
			SubstituteResults.Clear();
			OnPropertyChanged("HasSubstitutes");
			foreach (MedicineSearchResult item in medicineSearchResults.InStock)
			{
				InStockResults.Add(item);
			}
			foreach (MedicineSearchResult item2 in medicineSearchResults.FromCatalog)
			{
				CatalogResults.Add(item2);
			}
			RefreshHint(searched: true);
		}
		catch (OperationCanceledException) when (token.IsCancellationRequested)
		{
		}
		catch (Exception exception)
		{
			_logger.LogError(exception, "Medicine catalogue search failed.");
			ErrorMessage = "Search could not be completed. Please try again.";
		}
		finally
		{
			if (!token.IsCancellationRequested)
			{
				IsSearching = false;
			}
		}
	}

	private async Task ShowSubstitutesAsync(MedicineSearchResult? medicine)
	{
		if ((object)medicine == null || string.IsNullOrWhiteSpace(medicine.CompositionKey))
		{
			return;
		}
		try
		{
			using IServiceScope scope = _scopeFactory.CreateScope();
			List<SubstituteStockResult> list = await scope.ServiceProvider.GetRequiredService<CatalogSearchService>().FindSubstitutesAsync(medicine.CompositionKey, medicine.CatalogMedicineId);
			SubstituteResults.Clear();
			foreach (SubstituteStockResult item in list)
			{
				SubstituteResults.Add(CatalogSearchService.ToMedicineSearchResult(item));
			}
			OnPropertyChanged("HasSubstitutes");
		}
		catch (Exception exception)
		{
			_logger.LogError(exception, "Medicine substitute search failed.");
			ErrorMessage = "Substitutes could not be loaded.";
		}
	}

	public void Dispose()
	{
		_searchCancellation?.Cancel();
		_searchCancellation?.Dispose();
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void NotifyCreateUnlisted()
	{
		OnPropertyChanged(nameof(AddManualCaption));
		OnPropertyChanged(nameof(ShowCreateUnlisted));
		OnPropertyChanged(nameof(CreateUnlistedCaption));
	}

	private void OnQueryChanged(string value)
	{
		NotifyCreateUnlisted();
		SearchCommand.ExecuteAsync("debounce");
	}
}
