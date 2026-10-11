using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using PharmaBill.App.Services;
using PharmaBill.Data.Services;

namespace PharmaBill.App.ViewModels;

public sealed class WholesaleDashboardPageViewModel : ObservableObject, ILoadablePage
{
	private readonly IServiceScopeFactory _scopeFactory;
	private readonly INavigationService _navigation;

	private WholesaleDashboardSnapshot _snapshot = new(
		0m, 0, 0m, 0m, 0m, 0m, 0m, 0m, 0, 0, 0, 0, 0m, 0m, 0m, 0m,
		Array.Empty<WholesaleOverdueBuyerRow>(), 0, 0m, 0, 0, 0,
		Array.Empty<WholesaleBulkVelocityRow>(), 0, 0, 0);

	private string _errorMessage = string.Empty;
	private string _statusMessage = string.Empty;
	private DateTime _lastUpdated = DateTime.Now;
	private AsyncRelayCommand? _refreshCommand;
	private RelayCommand? _newB2bOrderCommand;
	private RelayCommand? _outstandingStatementCommand;
	private RelayCommand? _openGstCommand;
	private RelayCommand<WholesaleOverdueBuyerRow?>? _whatsAppReminderCommand;
	private RelayCommand<WholesaleOverdueBuyerRow?>? _openLedgerCommand;

	public WholesaleDashboardPageViewModel(IServiceScopeFactory scopeFactory, INavigationService navigation)
	{
		_scopeFactory = scopeFactory;
		_navigation = navigation;
	}

	public ObservableCollection<WholesaleOverdueBuyerRow> TopOverdueBuyers { get; } = new();

	public ObservableCollection<WholesaleBulkVelocityRow> FastMovingBulk { get; } = new();

	public WholesaleDashboardSnapshot Snapshot
	{
		get => _snapshot;
		private set
		{
			if (SetProperty(ref _snapshot, value))
			{
				OnPropertyChanged(nameof(TodayTurnoverText));
				OnPropertyChanged(nameof(TodayOrdersText));
				OnPropertyChanged(nameof(TrendText));
				OnPropertyChanged(nameof(TrendPositive));
				OnPropertyChanged(nameof(OutstandingText));
				OnPropertyChanged(nameof(Overdue45Text));
				OnPropertyChanged(nameof(PayablesText));
				OnPropertyChanged(nameof(DueThisWeekText));
				OnPropertyChanged(nameof(ActiveOrdersSummary));
				OnPropertyChanged(nameof(Ageing0Share));
				OnPropertyChanged(nameof(Ageing31Share));
				OnPropertyChanged(nameof(Ageing61Share));
				OnPropertyChanged(nameof(Ageing90Share));
				OnPropertyChanged(nameof(Ageing0Text));
				OnPropertyChanged(nameof(Ageing31Text));
				OnPropertyChanged(nameof(Ageing61Text));
				OnPropertyChanged(nameof(Ageing90Text));
				OnPropertyChanged(nameof(TaxReadinessText));
			}
		}
	}

	public string ErrorMessage
	{
		get => _errorMessage;
		private set => SetProperty(ref _errorMessage, value);
	}

	public string StatusMessage
	{
		get => _statusMessage;
		private set => SetProperty(ref _statusMessage, value);
	}

	public DateTime LastUpdated
	{
		get => _lastUpdated;
		private set => SetProperty(ref _lastUpdated, value);
	}

	public string TodayTurnoverText => $"₹{Snapshot.TodayTurnover:N2}";
	public string TodayOrdersText => $"{Snapshot.TodayOrderCount} order(s)";
	public string TrendText => Snapshot.TurnoverTrendPercent >= 0
		? $"+{Snapshot.TurnoverTrendPercent:0.#}% vs yesterday"
		: $"{Snapshot.TurnoverTrendPercent:0.#}% vs yesterday";
	public bool TrendPositive => Snapshot.TurnoverTrendPercent >= 0;
	public string OutstandingText => $"₹{Snapshot.OutstandingReceivables:N2}";
	public string Overdue45Text => $"Overdue (>45 days): ₹{Snapshot.OverdueOver45:N2}";
	public string PayablesText => $"₹{Snapshot.SupplierPayables:N2}";
	public string DueThisWeekText => $"Due this week: ₹{Snapshot.PayablesDueThisWeek:N2}";
	public string ActiveOrdersSummary =>
		$"Draft {Snapshot.DraftOrders} · Pack {Snapshot.ReadyToPackOrders} · Dispatched {Snapshot.DispatchedOrders} · e-Way {Snapshot.PendingEWayOrders}";

	public double Ageing0Share => AgeingShare(Snapshot.Ageing0To30);
	public double Ageing31Share => AgeingShare(Snapshot.Ageing31To60);
	public double Ageing61Share => AgeingShare(Snapshot.Ageing61To90);
	public double Ageing90Share => AgeingShare(Snapshot.AgeingOver90);
	public string Ageing0Text => $"₹{Snapshot.Ageing0To30:N0}";
	public string Ageing31Text => $"₹{Snapshot.Ageing31To60:N0}";
	public string Ageing61Text => $"₹{Snapshot.Ageing61To90:N0}";
	public string Ageing90Text => $"₹{Snapshot.AgeingOver90:N0}";
	public string TaxReadinessText =>
		$"e-Invoices today: {Snapshot.EInvoicesToday} · Pending IRN: {Snapshot.PendingIrn} · e-Way expiring ~24h: {Snapshot.EWayExpiringSoon}";

	public IAsyncRelayCommand RefreshCommand => _refreshCommand ??= new AsyncRelayCommand(() => LoadAsync());
	public IRelayCommand NewB2bOrderCommand => _newB2bOrderCommand ??= new RelayCommand(() => _navigation.Navigate("WholesaleBilling"));
	public IRelayCommand OutstandingStatementCommand => _outstandingStatementCommand ??= new RelayCommand(() => _navigation.Navigate("CollectionsDunning"));
	public IRelayCommand OpenGstCommand => _openGstCommand ??= new RelayCommand(() => _navigation.Navigate("GstReturns"));
	public IRelayCommand<WholesaleOverdueBuyerRow?> WhatsAppReminderCommand =>
		_whatsAppReminderCommand ??= new RelayCommand<WholesaleOverdueBuyerRow?>(SendWhatsAppReminder);
	public IRelayCommand<WholesaleOverdueBuyerRow?> OpenLedgerCommand =>
		_openLedgerCommand ??= new RelayCommand<WholesaleOverdueBuyerRow?>(_ => _navigation.Navigate("Accounts"));

	public async Task LoadAsync(CancellationToken cancellationToken = default)
	{
		try
		{
			using IServiceScope scope = _scopeFactory.CreateScope();
			WholesaleDashboardService service = scope.ServiceProvider.GetRequiredService<WholesaleDashboardService>();
			WholesaleDashboardSnapshot snapshot = await service.GetSnapshotAsync(DateTime.Now, cancellationToken);
			Snapshot = snapshot;
			TopOverdueBuyers.Clear();
			foreach (WholesaleOverdueBuyerRow row in snapshot.TopOverdueBuyers)
			{
				TopOverdueBuyers.Add(row);
			}

			FastMovingBulk.Clear();
			foreach (WholesaleBulkVelocityRow row in snapshot.FastMovingBulk)
			{
				FastMovingBulk.Add(row);
			}

			LastUpdated = DateTime.Now;
			ErrorMessage = string.Empty;
			StatusMessage = "Wholesale B2B dashboard refreshed from local books.";
		}
		catch (Exception ex)
		{
			ErrorMessage = ex.Message;
		}
	}

	private double AgeingShare(decimal bucket)
	{
		decimal total = Snapshot.Ageing0To30 + Snapshot.Ageing31To60 + Snapshot.Ageing61To90 + Snapshot.AgeingOver90;
		if (total <= 0m)
		{
			return 0.01;
		}

		return Math.Max(0.04, (double)(bucket / total));
	}

	private void SendWhatsAppReminder(WholesaleOverdueBuyerRow? row)
	{
		if (row is null)
		{
			return;
		}

		string digits = new string((row.Phone ?? string.Empty).Where(char.IsDigit).ToArray());
		if (digits.Length < 10)
		{
			ErrorMessage = "No valid mobile number for WhatsApp reminder on " + row.PharmacyName + ".";
			return;
		}

		if (digits.Length == 10)
		{
			digits = "91" + digits;
		}

		string text = Uri.EscapeDataString(
			$"Dear {row.PharmacyName}, your overdue balance is ₹{row.OverdueAmount:0.00} ({row.DaysOverdue} days). Kindly arrange payment. Thank you.");
		try
		{
			Process.Start(new ProcessStartInfo($"https://wa.me/{digits}?text={text}") { UseShellExecute = true });
			StatusMessage = "Opened WhatsApp reminder for " + row.PharmacyName + ".";
		}
		catch (Exception ex)
		{
			ErrorMessage = ex.Message;
		}
	}
}
