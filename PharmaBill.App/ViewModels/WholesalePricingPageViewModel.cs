using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PharmaBill.App.Services;
using PharmaBill.Core.Entities;
using PharmaBill.Data.Persistence;
using PharmaBill.Data.Services;

namespace PharmaBill.App.ViewModels;

public partial class WholesalePricingPageViewModel : ObservableObject, ILoadablePage
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly CurrentSession _session;

    public WholesalePricingPageViewModel(IServiceScopeFactory scopeFactory, CurrentSession session)
    {
        _scopeFactory = scopeFactory;
        _session = session;
    }

    public ObservableCollection<Drug> Drugs { get; } = [];
    public ObservableCollection<Batch> Batches { get; } = [];
    public ObservableCollection<DrugPackLevel> PackLevels { get; } = [];
    public ObservableCollection<WholesaleRateHistoryRow> RecentRates { get; } = [];
    public PackLevel[] PackLevelOptions { get; } = Enum.GetValues<PackLevel>();

    [ObservableProperty] private Drug? _selectedDrug;
    [ObservableProperty] private Batch? _selectedBatch;
    [ObservableProperty] private string _priceCategory = string.Empty;
    [ObservableProperty] private string _categoryPrice = string.Empty;
    [ObservableProperty] private string _tradeDiscountPercent = string.Empty;
    [ObservableProperty] private string _buyQuantity = string.Empty;
    [ObservableProperty] private string _freeQuantity = string.Empty;
    [ObservableProperty] private DateTime? _effectiveFrom = DateTime.Today;
    [ObservableProperty] private DateTime? _effectiveTo;
    [ObservableProperty] private decimal _purchaseRate;
    [ObservableProperty] private decimal? _ptr;
    [ObservableProperty] private decimal? _pts;
    [ObservableProperty] private decimal _mrp;
    [ObservableProperty] private string _message = string.Empty;
    [ObservableProperty] private string _errorMessage = string.Empty;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<PharmaBillDbContext>();
        Drugs.Clear();
        foreach (var drug in await context.Drugs.AsNoTracking().Where(item => item.IsActive).OrderBy(item => item.Name).ToListAsync(cancellationToken))
        {
            Drugs.Add(drug);
        }

        if (SelectedDrug is not null)
        {
            await LoadDrugAsync(SelectedDrug.Id, cancellationToken);
        }
    }

    partial void OnSelectedDrugChanged(Drug? value)
    {
        if (value is not null)
        {
            _ = LoadDrugAsync(value.Id, CancellationToken.None);
        }
    }

    partial void OnSelectedBatchChanged(Batch? value)
    {
        if (value is not null)
        {
            PurchaseRate = value.PurchasePrice;
            Ptr = value.Ptr;
            Pts = value.Pts;
            Mrp = value.Mrp ?? 0m;
        }
    }

    [RelayCommand]
    private async Task AddPackLevelAsync()
    {
        if (SelectedDrug is null || !Enum.TryParse<PackLevel>(NewPackLevelName, out var level) ||
            !decimal.TryParse(NewPackUnits, out var units) || units <= 0)
        {
            ErrorMessage = "Select a medicine, pack level and positive units-per-pack.";
            return;
        }

        var levels = PackLevels.Where(item => item.Level != level)
            .Select(item => new DrugPackLevel { Level = item.Level, Label = item.Label, UnitsPerPack = item.UnitsPerPack })
            .Append(new DrugPackLevel
            {
                Level = level,
                Label = string.IsNullOrWhiteSpace(NewPackLabel) ? level.ToString() : NewPackLabel.Trim(),
                UnitsPerPack = units
            }).ToArray();
        try
        {
            using var scope = _scopeFactory.CreateScope();
            await scope.ServiceProvider.GetRequiredService<WholesalePricingService>()
                .SavePackLevelsAsync(SelectedDrug.Id, levels);
            await LoadDrugAsync(SelectedDrug.Id, CancellationToken.None);
            Message = $"{level} pack conversion saved.";
            ErrorMessage = string.Empty;
        }
        catch (InvalidOperationException exception)
        {
            ErrorMessage = exception.Message;
        }
    }

    [ObservableProperty] private string _newPackLevelName = "Unit";
    [ObservableProperty] private string _newPackLabel = string.Empty;
    [ObservableProperty] private string _newPackUnits = "1";

    [RelayCommand]
    private async Task SaveBatchRatesAsync()
    {
        if (SelectedBatch is null)
        {
            ErrorMessage = "Select a batch before updating rates.";
            return;
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            await scope.ServiceProvider.GetRequiredService<WholesalePricingService>()
                .SaveBatchRatesAsync(
                    SelectedBatch.Id,
                    new WholesaleBatchRate(PurchaseRate, Ptr, Pts, Mrp),
                    _session.User?.Id ?? throw new UnauthorizedAccessException("Sign in before editing rates."));
            Message = "Batch purchase rate, PTR, PTS and MRP saved.";
            ErrorMessage = string.Empty;
            await LoadDrugAsync(SelectedDrug!.Id, CancellationToken.None);
        }
        catch (Exception exception) when (exception is InvalidOperationException or UnauthorizedAccessException)
        {
            ErrorMessage = exception.Message;
        }
    }

    [RelayCommand]
    private async Task SaveCategoryPriceAsync()
    {
        if (SelectedDrug is null || string.IsNullOrWhiteSpace(PriceCategory) ||
            !decimal.TryParse(CategoryPrice, out var price))
        {
            ErrorMessage = "Select a medicine, category and valid category price.";
            return;
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            await scope.ServiceProvider.GetRequiredService<WholesalePricingService>().SaveCategoryPriceAsync(
                new CustomerCategoryPrice
                {
                    DrugId = SelectedDrug.Id,
                    PriceCategory = PriceCategory.Trim(),
                    UnitPrice = price,
                    EffectiveFrom = EffectiveFrom.HasValue ? DateOnly.FromDateTime(EffectiveFrom.Value) : null,
                    EffectiveTo = EffectiveTo.HasValue ? DateOnly.FromDateTime(EffectiveTo.Value) : null
                });
            Message = "Customer category price saved.";
            ErrorMessage = string.Empty;
        }
        catch (InvalidOperationException exception)
        {
            ErrorMessage = exception.Message;
        }
    }

    [RelayCommand]
    private async Task SaveTradeDiscountAsync()
    {
        if (string.IsNullOrWhiteSpace(PriceCategory) ||
            !decimal.TryParse(TradeDiscountPercent, out var discount))
        {
            ErrorMessage = "Enter a price category and a valid discount percentage.";
            return;
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            await scope.ServiceProvider.GetRequiredService<WholesalePricingService>().SaveTradeDiscountAsync(
                new TradeDiscount
                {
                    DrugId = SelectedDrug?.Id,
                    PriceCategory = PriceCategory.Trim(),
                    DiscountPercent = discount,
                    EffectiveFrom = EffectiveFrom.HasValue ? DateOnly.FromDateTime(EffectiveFrom.Value) : null,
                    EffectiveTo = EffectiveTo.HasValue ? DateOnly.FromDateTime(EffectiveTo.Value) : null
                });
            Message = "Trade discount saved.";
            ErrorMessage = string.Empty;
        }
        catch (InvalidOperationException exception)
        {
            ErrorMessage = exception.Message;
        }
    }

    [RelayCommand]
    private async Task SaveSchemeAsync()
    {
        if (!decimal.TryParse(BuyQuantity, out var buy) || !decimal.TryParse(FreeQuantity, out var free) ||
            !EffectiveFrom.HasValue || !EffectiveTo.HasValue)
        {
            ErrorMessage = "Enter scheme buy/free quantities and start/end dates.";
            return;
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            await scope.ServiceProvider.GetRequiredService<WholesalePricingService>().SaveSchemeAsync(
                new WholesaleScheme
                {
                    DrugId = SelectedDrug?.Id,
                    PriceCategory = string.IsNullOrWhiteSpace(PriceCategory) ? null : PriceCategory.Trim(),
                    BuyQuantity = buy,
                    FreeQuantity = free,
                    StartsOn = DateOnly.FromDateTime(EffectiveFrom.Value),
                    EndsOn = DateOnly.FromDateTime(EffectiveTo.Value)
                });
            Message = $"Scheme saved: {buy:0.##}+{free:0.##}.";
            ErrorMessage = string.Empty;
        }
        catch (InvalidOperationException exception)
        {
            ErrorMessage = exception.Message;
        }
    }

    [RelayCommand]
    private async Task LoadRateHistoryAsync()
    {
        if (SelectedDrug is null || SelectedCustomerId == Guid.Empty)
        {
            ErrorMessage = "Enter/select a customer ID and medicine to view recent rates.";
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var rates = await scope.ServiceProvider.GetRequiredService<WholesalePricingService>()
            .GetLastRatesAsync(SelectedCustomerId, SelectedDrug.Id);
        RecentRates.Clear();
        foreach (var rate in rates)
        {
            RecentRates.Add(rate);
        }
    }

    [ObservableProperty] private Guid _selectedCustomerId;

    private async Task LoadDrugAsync(Guid drugId, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<PharmaBillDbContext>();
        var batches = await context.Batches.AsNoTracking()
            .Where(item => item.DrugId == drugId)
            .OrderBy(item => item.ExpiryDate)
            .ThenBy(item => item.BatchNo)
            .ToListAsync(cancellationToken);
        Batches.Clear();
        foreach (var batch in batches)
        {
            Batches.Add(batch);
        }

        var levels = await context.DrugPackLevels.AsNoTracking()
            .Where(item => item.DrugId == drugId)
            .OrderBy(item => item.Level)
            .ToListAsync(cancellationToken);
        PackLevels.Clear();
        foreach (var level in levels)
        {
            PackLevels.Add(level);
        }
    }
}
