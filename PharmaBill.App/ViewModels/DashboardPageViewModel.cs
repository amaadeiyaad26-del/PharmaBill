using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using PharmaBill.Data.Services;

namespace PharmaBill.App.ViewModels;

public partial class DashboardPageViewModel(IServiceScopeFactory scopeFactory) : ObservableObject, ILoadablePage
{
    [ObservableProperty] private DashboardSnapshot _snapshot = new(0, 0, 0, 0, 0, 0);
    [ObservableProperty] private string _errorMessage = string.Empty;
    [ObservableProperty] private DateTime _lastUpdated = DateTime.Now;

    public async Task LoadAsync(CancellationToken cancellationToken = default) => await RefreshAsync(cancellationToken);

    [RelayCommand]
    private async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            Snapshot = await scope.ServiceProvider.GetRequiredService<ReportsDashboardService>()
                .GetDashboardAsync(DateTime.Now, cancellationToken);
            LastUpdated = DateTime.Now;
            ErrorMessage = string.Empty;
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }
    }
}
