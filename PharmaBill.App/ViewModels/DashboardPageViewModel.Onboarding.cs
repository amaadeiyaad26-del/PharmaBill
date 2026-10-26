using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using PharmaBill.Data.Persistence;
using PharmaBill.Data.Services;

namespace PharmaBill.App.ViewModels;

// Getting started: a new shop's three milestones on the dashboard, ticked from its records, until done or hidden.
public partial class DashboardPageViewModel
{
	private OnboardingProgress _onboarding = new(true, true, true);
	private bool _showGettingStarted;
	private RelayCommand? hideGettingStartedCommand;

	public bool ShowGettingStarted
	{
		get => _showGettingStarted;
		private set => SetProperty(ref _showGettingStarted, value);
	}

	public bool StoreDetailsDone => _onboarding.StoreDetails;

	public bool FirstDrugDone => _onboarding.FirstDrug;

	public bool FirstSaleDone => _onboarding.FirstSale;

	public string GettingStartedProgress => $"{_onboarding.Done} of 3 done";

	public IRelayCommand HideGettingStartedCommand => hideGettingStartedCommand ??= new RelayCommand(() =>
	{
		onboarding?.HideChecklist();
		ShowGettingStarted = false;
	});

	private async Task RefreshOnboardingAsync(CancellationToken cancellationToken)
	{
		if (onboarding == null || onboarding.IsChecklistHidden())
		{
			ShowGettingStarted = false;
			return;
		}
		try
		{
			using IServiceScope scope = scopeFactory.CreateScope();
			_onboarding = await new OnboardingProgressService(scope.ServiceProvider.GetRequiredService<PharmaBillDbContext>()).GetAsync(cancellationToken);
		}
		catch (Exception)
		{
			// The checklist is a help, never a reason for the dashboard to fail.
			ShowGettingStarted = false;
			return;
		}
		OnPropertyChanged(nameof(StoreDetailsDone));
		OnPropertyChanged(nameof(FirstDrugDone));
		OnPropertyChanged(nameof(FirstSaleDone));
		OnPropertyChanged(nameof(GettingStartedProgress));
		// Once all three are done, it has served its purpose and stays away.
		if (_onboarding.Complete)
		{
			onboarding.HideChecklist();
		}
		ShowGettingStarted = !_onboarding.Complete;
	}
}
