using Microsoft.Extensions.DependencyInjection;
using PharmaBill.App.ViewModels;

namespace PharmaBill.App.Services;

public interface IAddStockDialogService
{
    // Returns true when stock was saved.
    Task<bool> ShowAsync(AddStockRequest request);
}

public sealed class AddStockDialogService(IServiceProvider services) : IAddStockDialogService
{
    public async Task<bool> ShowAsync(AddStockRequest request)
    {
        var viewModel = ActivatorUtilities.CreateInstance<AddStockViewModel>(services);
        await viewModel.InitializeAsync(request);
        var window = new AddStockWindow(viewModel, services.GetRequiredService<IConfirmationService>())
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };
        return window.ShowDialog() == true;
    }
}