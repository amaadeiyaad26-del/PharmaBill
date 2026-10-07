using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PharmaBill.App.Services;
using PharmaBill.Data.Persistence;
using PharmaBill.Data.Services.Dunning;

namespace PharmaBill.App.ViewModels;

public class DunningDashboardPageViewModel : ObservableObject, ILoadablePage
{
	private readonly IServiceScopeFactory _scopeFactory;

	private readonly CurrentSession _session;

	private string _paymentDetails = string.Empty;

	private string _statusMessage = string.Empty;

	private string _errorMessage = string.Empty;

	private string _pharmacyName = "PharmaBill";

	private DunningReceivableRow? _selectedReceivable;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? refreshCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? sendWhatsAppRemindersCommand;

	public ObservableCollection<DunningReceivableRow> Receivables { get; } = new ObservableCollection<DunningReceivableRow>();

	public ObservableCollection<CustomerCreditProfile> Profiles { get; } = new ObservableCollection<CustomerCreditProfile>();

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string PaymentDetails
	{
		get
		{
			return _paymentDetails;
		}
		[MemberNotNull("_paymentDetails")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_paymentDetails, value))
			{
				OnPropertyChanging(nameof(PaymentDetails));
				_paymentDetails = value;
				OnPropertyChanged(nameof(PaymentDetails));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string StatusMessage
	{
		get
		{
			return _statusMessage;
		}
		[MemberNotNull("_statusMessage")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_statusMessage, value))
			{
				OnPropertyChanging(nameof(StatusMessage));
				_statusMessage = value;
				OnPropertyChanged(nameof(StatusMessage));
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
	public string PharmacyName
	{
		get
		{
			return _pharmacyName;
		}
		[MemberNotNull("_pharmacyName")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_pharmacyName, value))
			{
				OnPropertyChanging(nameof(PharmacyName));
				_pharmacyName = value;
				OnPropertyChanged(nameof(PharmacyName));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public DunningReceivableRow? SelectedReceivable
	{
		get
		{
			return _selectedReceivable;
		}
		set
		{
			if (!EqualityComparer<DunningReceivableRow>.Default.Equals(_selectedReceivable, value))
			{
				OnPropertyChanging(nameof(SelectedReceivable));
				_selectedReceivable = value;
				OnPropertyChanged(nameof(SelectedReceivable));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand RefreshCommand => refreshCommand ?? (refreshCommand = new AsyncRelayCommand(RefreshAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand SendWhatsAppRemindersCommand => sendWhatsAppRemindersCommand ?? (sendWhatsAppRemindersCommand = new RelayCommand(SendWhatsAppReminders));

	public DunningDashboardPageViewModel(IServiceScopeFactory scopeFactory, CurrentSession session)
	{
		_scopeFactory = scopeFactory;
		_session = session;
	}

	public async Task LoadAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		_ = 2;
		try
		{
			ErrorMessage = string.Empty;
			using IServiceScope scope = _scopeFactory.CreateScope();
			IDunningService dunning = scope.ServiceProvider.GetRequiredService<IDunningService>();
			PharmacyName = (await scope.ServiceProvider.GetRequiredService<PharmaBillDbContext>().PharmacyProfiles.AsNoTracking().FirstOrDefaultAsync(cancellationToken))?.Name ?? "PharmaBill";
			IReadOnlyList<DunningReceivableRow> source = await dunning.GetOpenReceivablesAsync(cancellationToken);
			Receivables.Clear();
			foreach (DunningReceivableRow item in from item in source
				orderby item.RiskScore descending, item.OutstandingAmount descending
				select item)
			{
				Receivables.Add(item);
			}
			IReadOnlyList<CustomerCreditProfile> readOnlyList = await dunning.GetCreditProfilesAsync(cancellationToken);
			Profiles.Clear();
			foreach (CustomerCreditProfile item2 in readOnlyList)
			{
				Profiles.Add(item2);
			}
			StatusMessage = $"{Receivables.Count} open receivable(s); {Receivables.Count((DunningReceivableRow row) => row.Tier == DunningTier.T4_Critical)} at T4 critical.";
		}
		catch (Exception ex)
		{
			ErrorMessage = ex.Message;
		}
	}

	private async Task RefreshAsync()
	{
		await LoadAsync();
	}

	private void SendWhatsAppReminders()
	{
		try
		{
			ErrorMessage = string.Empty;
			using IServiceScope serviceScope = _scopeFactory.CreateScope();
			IDunningService requiredService = serviceScope.ServiceProvider.GetRequiredService<IDunningService>();
			DunningReceivableRow[] array = (((object)SelectedReceivable == null) ? Receivables.Where((DunningReceivableRow row) => row.Tier >= DunningTier.T2_Due).ToArray() : Receivables.Where((DunningReceivableRow row) => row.CustomerId == SelectedReceivable.CustomerId).ToArray());
			if (array.Length == 0)
			{
				ErrorMessage = "No overdue rows selected for WhatsApp reminders.";
				return;
			}
			IReadOnlyList<DunningWhatsAppMessage> readOnlyList = requiredService.BuildWhatsAppReminders(array, PharmacyName, PaymentDetails);
			if (readOnlyList.Count == 0)
			{
				ErrorMessage = "No valid mobile numbers found for WhatsApp reminders.";
				return;
			}
			foreach (DunningWhatsAppMessage item in readOnlyList.Take(8))
			{
				Process.Start(new ProcessStartInfo(item.WaMeUrl)
				{
					UseShellExecute = true
				});
			}
			StatusMessage = $"Opened {Math.Min(8, readOnlyList.Count)} WhatsApp reminder(s).";
		}
		catch (Exception ex)
		{
			ErrorMessage = ex.Message;
		}
	}
}
