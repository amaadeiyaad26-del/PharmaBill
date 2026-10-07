using System;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using PharmaBill.App.ViewModels;

namespace PharmaBill.App.Services;

public sealed class AddStockDialogService(IServiceProvider services) : IAddStockDialogService
{
	public async Task<bool> ShowAsync(AddStockRequest request)
	{
		AddStockViewModel viewModel = ActivatorUtilities.CreateInstance<AddStockViewModel>(services, Array.Empty<object>());
		await viewModel.InitializeAsync(request);
		return new AddStockWindow(viewModel, services.GetRequiredService<IConfirmationService>())
		{
			Owner = Application.Current?.MainWindow
		}.ShowDialog() == true;
	}
}
