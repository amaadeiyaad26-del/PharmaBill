using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel.__Internals;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using PharmaBill.Data.Services;
using PharmaBill.Sync;

namespace PharmaBill.App.ViewModels;

public class DashboardPageViewModel(IServiceScopeFactory scopeFactory, LocalSyncServer localSync) : ObservableObject, ILoadablePage
{
	[ObservableProperty]
	private DashboardSnapshot _snapshot = new DashboardSnapshot(0m, 0m, 0, 0, 0m, 0);

	[ObservableProperty]
	private string _errorMessage = string.Empty;

	[ObservableProperty]
	private DateTime _lastUpdated = DateTime.Now;

	[ObservableProperty]
	private IReadOnlyList<ChartBar> _dailySales = Array.Empty<ChartBar>();

	[ObservableProperty]
	private IReadOnlyList<ChartBar> _hourlyBills = Array.Empty<ChartBar>();

	[ObservableProperty]
	private IReadOnlyList<TopMedicine> _topMedicines = Array.Empty<TopMedicine>();

	[ObservableProperty]
	private IReadOnlyList<CategoryShare> _categories = Array.Empty<CategoryShare>();

	[ObservableProperty]
	private string _dailyAxisMax = "0";

	[ObservableProperty]
	private string _dailyAxisMid = "0";

	[ObservableProperty]
	private bool _hasDailySales;

	[ObservableProperty]
	private bool _hasHourlyBills;

	[ObservableProperty]
	private bool _hasTopMedicines;

	[ObservableProperty]
	private bool _hasCategories;

	[ObservableProperty]
	private string _highlightSalesHeadline = "Today: —";

	[ObservableProperty]
	private string _highlightSalesSub = "No invoices yet";

	[ObservableProperty]
	private string _highlightStockHeadline = "Stock Alert";

	[ObservableProperty]
	private string _highlightStockSub = "No inventory alerts";

	[ObservableProperty]
	private string _highlightSyncHeadline = "Mobile LAN Sync";

	[ObservableProperty]
	private string _highlightSyncSub = "Host status unavailable";

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand<string?>? drillDownCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? refreshCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public DashboardSnapshot Snapshot
	{
		get
		{
			return _snapshot;
		}
		[MemberNotNull("_snapshot")]
		set
		{
			if (!EqualityComparer<DashboardSnapshot>.Default.Equals(_snapshot, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.Snapshot);
				_snapshot = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.Snapshot);
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
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.ErrorMessage);
				_errorMessage = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.ErrorMessage);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public DateTime LastUpdated
	{
		get
		{
			return _lastUpdated;
		}
		set
		{
			if (!EqualityComparer<DateTime>.Default.Equals(_lastUpdated, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.LastUpdated);
				_lastUpdated = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.LastUpdated);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IReadOnlyList<ChartBar> DailySales
	{
		get
		{
			return _dailySales;
		}
		[MemberNotNull("_dailySales")]
		set
		{
			if (!EqualityComparer<IReadOnlyList<ChartBar>>.Default.Equals(_dailySales, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.DailySales);
				_dailySales = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.DailySales);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IReadOnlyList<ChartBar> HourlyBills
	{
		get
		{
			return _hourlyBills;
		}
		[MemberNotNull("_hourlyBills")]
		set
		{
			if (!EqualityComparer<IReadOnlyList<ChartBar>>.Default.Equals(_hourlyBills, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.HourlyBills);
				_hourlyBills = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.HourlyBills);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IReadOnlyList<TopMedicine> TopMedicines
	{
		get
		{
			return _topMedicines;
		}
		[MemberNotNull("_topMedicines")]
		set
		{
			if (!EqualityComparer<IReadOnlyList<TopMedicine>>.Default.Equals(_topMedicines, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.TopMedicines);
				_topMedicines = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.TopMedicines);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IReadOnlyList<CategoryShare> Categories
	{
		get
		{
			return _categories;
		}
		[MemberNotNull("_categories")]
		set
		{
			if (!EqualityComparer<IReadOnlyList<CategoryShare>>.Default.Equals(_categories, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.Categories);
				_categories = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.Categories);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string DailyAxisMax
	{
		get
		{
			return _dailyAxisMax;
		}
		[MemberNotNull("_dailyAxisMax")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_dailyAxisMax, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.DailyAxisMax);
				_dailyAxisMax = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.DailyAxisMax);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string DailyAxisMid
	{
		get
		{
			return _dailyAxisMid;
		}
		[MemberNotNull("_dailyAxisMid")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_dailyAxisMid, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.DailyAxisMid);
				_dailyAxisMid = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.DailyAxisMid);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool HasDailySales
	{
		get
		{
			return _hasDailySales;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_hasDailySales, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.HasDailySales);
				_hasDailySales = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.HasDailySales);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool HasHourlyBills
	{
		get
		{
			return _hasHourlyBills;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_hasHourlyBills, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.HasHourlyBills);
				_hasHourlyBills = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.HasHourlyBills);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool HasTopMedicines
	{
		get
		{
			return _hasTopMedicines;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_hasTopMedicines, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.HasTopMedicines);
				_hasTopMedicines = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.HasTopMedicines);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool HasCategories
	{
		get
		{
			return _hasCategories;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_hasCategories, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.HasCategories);
				_hasCategories = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.HasCategories);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string HighlightSalesHeadline
	{
		get
		{
			return _highlightSalesHeadline;
		}
		[MemberNotNull("_highlightSalesHeadline")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_highlightSalesHeadline, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.HighlightSalesHeadline);
				_highlightSalesHeadline = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.HighlightSalesHeadline);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string HighlightSalesSub
	{
		get
		{
			return _highlightSalesSub;
		}
		[MemberNotNull("_highlightSalesSub")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_highlightSalesSub, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.HighlightSalesSub);
				_highlightSalesSub = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.HighlightSalesSub);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string HighlightStockHeadline
	{
		get
		{
			return _highlightStockHeadline;
		}
		[MemberNotNull("_highlightStockHeadline")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_highlightStockHeadline, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.HighlightStockHeadline);
				_highlightStockHeadline = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.HighlightStockHeadline);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string HighlightStockSub
	{
		get
		{
			return _highlightStockSub;
		}
		[MemberNotNull("_highlightStockSub")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_highlightStockSub, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.HighlightStockSub);
				_highlightStockSub = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.HighlightStockSub);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string HighlightSyncHeadline
	{
		get
		{
			return _highlightSyncHeadline;
		}
		[MemberNotNull("_highlightSyncHeadline")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_highlightSyncHeadline, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.HighlightSyncHeadline);
				_highlightSyncHeadline = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.HighlightSyncHeadline);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string HighlightSyncSub
	{
		get
		{
			return _highlightSyncSub;
		}
		[MemberNotNull("_highlightSyncSub")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_highlightSyncSub, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.HighlightSyncSub);
				_highlightSyncSub = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.HighlightSyncSub);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand<string?> DrillDownCommand => drillDownCommand ?? (drillDownCommand = new RelayCommand<string>(DrillDown));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand RefreshCommand => refreshCommand ?? (refreshCommand = new AsyncRelayCommand(RefreshAsync));

	public event Action<DashboardTarget>? DrillDownRequested;

	[RelayCommand]
	private void DrillDown(string? target)
	{
		if (Enum.TryParse<DashboardTarget>(target, out var result))
		{
			DrillDownRequested?.Invoke(result);
		}
	}

	public async Task LoadAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		await RefreshAsync(cancellationToken);
	}

	[RelayCommand]
	private async Task RefreshAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		_ = 1;
		try
		{
			using IServiceScope scope = scopeFactory.CreateScope();
			ReportsDashboardService service = scope.ServiceProvider.GetRequiredService<ReportsDashboardService>();
			DateTime now = DateTime.Now;
			DashboardSnapshot snapshot = await service.GetDashboardAsync(now, cancellationToken);
			DashboardAnalytics dashboardAnalytics = await service.GetDashboardAnalyticsAsync(now, cancellationToken);
			Snapshot = snapshot;
			DailySales = dashboardAnalytics.DailySales;
			HourlyBills = dashboardAnalytics.HourlyBills;
			TopMedicines = dashboardAnalytics.TopMedicines;
			Categories = dashboardAnalytics.Categories;
			HasDailySales = dashboardAnalytics.DailySales.Any((ChartBar item) => item.Value > 0m);
			HasHourlyBills = dashboardAnalytics.HourlyBills.Any((ChartBar item) => item.Value > 0m);
			HasTopMedicines = dashboardAnalytics.TopMedicines.Count > 0;
			HasCategories = dashboardAnalytics.Categories.Count > 0;
			decimal num = ((dashboardAnalytics.DailySales.Count == 0) ? 0m : dashboardAnalytics.DailySales.Max((ChartBar item) => item.Value));
			DailyAxisMax = $"₹{num:N0}";
			DailyAxisMid = $"₹{num / 2m:N0}";
			UpdateHighlightSlides(snapshot);
			LastUpdated = DateTime.Now;
			ErrorMessage = string.Empty;
		}
		catch (Exception ex)
		{
			ErrorMessage = ex.Message;
		}
	}

	private void UpdateHighlightSlides(DashboardSnapshot snapshot)
	{
		string value = ((snapshot.TodayInvoiceCount == 1) ? "Invoice" : "Invoices");
		HighlightSalesHeadline = $"Today: ₹{snapshot.TodaySales:N0}";
		HighlightSalesSub = $"{snapshot.TodayInvoiceCount} {value} created • System healthy";
		HighlightStockHeadline = "Stock Alert";
		if (snapshot.ExpiringSoonBatches > 0)
		{
			HighlightStockSub = $"{snapshot.ExpiringSoonBatches} Batches nearing expiry within 90 days" + ((snapshot.LowStockDrugs > 0) ? $" • {snapshot.LowStockDrugs} low stock" : string.Empty);
		}
		else if (snapshot.LowStockDrugs > 0)
		{
			HighlightStockSub = $"{snapshot.LowStockDrugs} medicines below reorder level";
		}
		else
		{
			HighlightStockSub = "No batches nearing expiry";
		}
		HighlightSyncHeadline = "Mobile LAN Sync";
		string highlightSyncSub;
		switch (localSync.Status.State)
		{
		case LocalSyncState.Listening:
		case LocalSyncState.DeviceConnected:
			highlightSyncSub = $"Host running on port {localSync.Port} • Ready to scan";
			break;
		case LocalSyncState.Unavailable:
			highlightSyncSub = "Unavailable — " + localSync.Status.Message;
			break;
		default:
			highlightSyncSub = localSync.Status.Message;
			break;
		}
		HighlightSyncSub = highlightSyncSub;
	}
}
