using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PharmaBill.App.Services;
using PharmaBill.Data.Services;

namespace PharmaBill.App.ViewModels;

public partial class CatalogSearchViewModel : ObservableObject, IDisposable
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CatalogSearchViewModel> _logger;
    private readonly IAddStockDialogService? _addStockDialog;
    private CancellationTokenSource? _searchCancellation;

    public CatalogSearchViewModel(
        IServiceScopeFactory scopeFactory,
        ILogger<CatalogSearchViewModel> logger,
        IAddStockDialogService? addStockDialog = null)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _addStockDialog = addStockDialog;
    }

    [ObservableProperty]
    private string _query = string.Empty;

    partial void OnQueryChanged(string value) => _ = SearchCommand.ExecuteAsync(DebounceMarker);

    private const string DebounceMarker = "debounce";

    [ObservableProperty]
    private bool _isVisible;

    [ObservableProperty]
    private bool _isSearching;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    public ObservableCollection<MedicineSearchResult> InStockResults { get; } = [];

    public ObservableCollection<MedicineSearchResult> CatalogResults { get; } = [];

    public ObservableCollection<MedicineSearchResult> SubstituteResults { get; } = [];

    public bool HasInStock => InStockResults.Count > 0;

    public bool HasCatalog => CatalogResults.Count > 0;

    [ObservableProperty]
    private bool _showHint = true;

    [ObservableProperty]
    private string _hintText = "Type at least 2 letters to search";

    [ObservableProperty]
    private bool _showAddManual;

    public event EventHandler? StockChanged;

    public event EventHandler<string>? ViewStockRequested;

    private void RefreshHint(bool searched)
    {
        OnPropertyChanged(nameof(HasInStock));
        OnPropertyChanged(nameof(HasCatalog));
        var text = Query.Trim();
        if (text.Length < 2)
        {
            HintText = "Type at least 2 letters to search";
            ShowAddManual = false;
            ShowHint = true;
            return;
        }

        var empty = searched && InStockResults.Count == 0 && CatalogResults.Count == 0;
        HintText = empty ? $"No match in stock or catalogue for '{text}'" : string.Empty;
        ShowAddManual = empty;
        ShowHint = empty;
    }

    [RelayCommand]
    private async Task AddStockAsync(MedicineSearchResult? medicine)
    {
        if (_addStockDialog is null || medicine is null)
        {
            return;
        }

        var saved = await _addStockDialog.ShowAsync(
            new AddStockRequest(null, medicine.CatalogMedicineId, medicine.Name, medicine.Composition, medicine.Manufacturer));
        if (saved)
        {
            StockChanged?.Invoke(this, EventArgs.Empty);
            await SearchAsync(null);
        }
    }

    [RelayCommand]
    private async Task AddManualAsync()
    {
        if (_addStockDialog is null)
        {
            return;
        }

        var saved = await _addStockDialog.ShowAsync(new AddStockRequest(null, null, Query.Trim(), null, null));
        if (saved)
        {
            StockChanged?.Invoke(this, EventArgs.Empty);
            await SearchAsync(null);
        }
    }

    [RelayCommand]
    private void ViewStock(MedicineSearchResult? medicine)
    {
        if (medicine is not null)
        {
            ViewStockRequested?.Invoke(this, medicine.Name);
        }
    }

    public bool HasSubstitutes => SubstituteResults.Count > 0;

    public string SubstituteNote => "Pharmacist must confirm suitability and prescription rules";

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task SearchAsync(object? mode)
    {
        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
        _searchCancellation = new CancellationTokenSource();
        var token = _searchCancellation.Token;
        IsSearching = false;
        InStockResults.Clear();
        CatalogResults.Clear();
        SubstituteResults.Clear();
        OnPropertyChanged(nameof(HasSubstitutes));
        ErrorMessage = string.Empty;
        RefreshHint(searched: false);
        if (Query.Trim().Length < 2)
        {
            return;
        }

        try
        {
            IsSearching = true;
            if (ReferenceEquals(mode, DebounceMarker))
            {
                await Task.Delay(250, token);
            }

            using var scope = _scopeFactory.CreateScope();
            var search = scope.ServiceProvider.GetRequiredService<CatalogSearchService>();
            var results = await search.SearchAsync(Query.Trim(), token);
            foreach (var result in results.InStock)
            {
                InStockResults.Add(result);
            }

            foreach (var result in results.FromCatalog)
            {
                CatalogResults.Add(result);
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

    [RelayCommand]
    private async Task ShowSubstitutesAsync(MedicineSearchResult? medicine)
    {
        if (medicine is null || string.IsNullOrWhiteSpace(medicine.CompositionKey))
        {
            return;
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var search = scope.ServiceProvider.GetRequiredService<CatalogSearchService>();
            var substitutes = await search.FindSubstitutesAsync(medicine.CompositionKey, medicine.CatalogMedicineId);
            SubstituteResults.Clear();
            foreach (var substitute in substitutes)
            {
                SubstituteResults.Add(substitute);
            }

            OnPropertyChanged(nameof(HasSubstitutes));
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
}
