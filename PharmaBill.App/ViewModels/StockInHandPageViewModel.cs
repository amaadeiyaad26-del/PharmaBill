using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using PharmaBill.Core.Entities;
using PharmaBill.Data.Persistence;
using PharmaBill.Data.Services;

namespace PharmaBill.App.ViewModels;

public partial class StockInHandPageViewModel(IServiceScopeFactory scopeFactory) : ObservableObject, ILoadablePage
{
    [ObservableProperty] private DateTime _asOfDate = DateTime.Today;
    [ObservableProperty] private DateTime? _fromDate;
    [ObservableProperty] private DateTime? _deadStockBefore;
    [ObservableProperty] private Supplier? _selectedSupplier;
    [ObservableProperty] private string _scheduleFilter = string.Empty;
    [ObservableProperty] private string _manufacturerFilter = string.Empty;
    [ObservableProperty] private string _errorMessage = string.Empty;

    public ObservableCollection<StockInHandRow> Rows { get; } = [];
    public ObservableCollection<ReorderSuggestion> ReorderSuggestions { get; } = [];
    public ObservableCollection<DeadStockAlert> DeadStock { get; } = [];
    public ObservableCollection<Supplier> Suppliers { get; } = [];

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        using var scope = scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<PharmaBillDbContext>();
        Suppliers.Clear();
        foreach (var supplier in await context.Suppliers.AsNoTracking().Where(item => item.IsActive).OrderBy(item => item.Name).ToListAsync(cancellationToken))
        {
            Suppliers.Add(supplier);
        }

        await RefreshAsync(cancellationToken);
    }

    [RelayCommand]
    private async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<StockInHandService>();
            var asOfExclusive = AsOfDate.Date.AddDays(1).ToUniversalTime();
            var fromUtc = FromDate.HasValue ? FromDate.Value.Date.ToUniversalTime() : (DateTime?)null;
            var rows = await service.GetAsOfAsync(
                asOfExclusive,
                fromUtc,
                supplierId: SelectedSupplier?.Id,
                schedule: string.IsNullOrWhiteSpace(ScheduleFilter) ? null : ScheduleFilter.Trim(),
                manufacturer: string.IsNullOrWhiteSpace(ManufacturerFilter) ? null : ManufacturerFilter.Trim(),
                cancellationToken: cancellationToken);
            Rows.Clear();
            foreach (var row in rows)
            {
                Rows.Add(row);
            }

            ReorderSuggestions.Clear();
            foreach (var suggestion in await service.GetReorderSuggestionsAsync(cancellationToken))
            {
                ReorderSuggestions.Add(suggestion);
            }

            DeadStock.Clear();
            if (DeadStockBefore.HasValue)
            {
                var cutoffUtc = DeadStockBefore.Value.Date.ToUniversalTime();
                foreach (var alert in await service.GetDeadStockAsync(cutoffUtc, cancellationToken))
                {
                    DeadStock.Add(alert);
                }
            }

            ErrorMessage = string.Empty;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            ErrorMessage = exception.Message;
        }
    }
}
