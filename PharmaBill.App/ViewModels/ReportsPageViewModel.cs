using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using PharmaBill.App.Services;
using PharmaBill.Core;
using PharmaBill.Data.Services;

namespace PharmaBill.App.ViewModels;

public class ReportsPageViewModel : ObservableObject, ILoadablePage
{
	private readonly IServiceScopeFactory _scopeFactory;

	private readonly IFilePickerService _filePicker;

	private readonly TabularExportService _exports;

	private readonly IInvoiceDetailDialogService _invoiceDialog;

	private readonly DataRetentionSettingsStore _retentionSettingsStore;

	private bool _suppressReportChangeRefresh;

	public const string RowKeyColumn = "__key";

	private ReportBranchFilterChoice _selectedBranchFilter;

	private ReportChoice _selectedReport;

	private int _selectedMonth;

	private int _selectedYear;

	private DateTime _fromDate;

	private DateTime _toDate;

	private DataView? _table;

	private string _periodLabel = string.Empty;

	private string _statusMessage = string.Empty;

	private string _errorMessage = string.Empty;

	private string _selectedQuickPeriod = "This month";

	private string _selectedDimensionFilter = "All";

	private string _dimensionFilterLabel = "Filter";

	private bool _showDimensionFilter;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? refreshCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand<string>? selectQuickPeriodCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? runCustomPeriodCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? previousMonthCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? nextMonthCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand<DataRowView?>? openInvoiceCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? exportPdfCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? exportExcelCommand;

	public ObservableCollection<ReportChoice> ReportChoices { get; } = new ObservableCollection<ReportChoice>
	{
		new ReportChoice(ReportKind.Sales, "Sales Summary (General)"),
		new ReportChoice(ReportKind.CustomerPartyWise, "Customer / Party-wise Sales"),
		new ReportChoice(ReportKind.PurchaserSupplierWise, "Purchaser / Supplier-wise Report"),
		new ReportChoice(ReportKind.BatchWise, "Batch Number (B.No) wise Report"),
		new ReportChoice(ReportKind.BrandProductWise, "Brand / Product-wise Report"),
		new ReportChoice(ReportKind.ManufacturerWise, "Manufacturer / Company-wise Report"),
		new ReportChoice(ReportKind.GstSummary, "Tax & GST Summary"),
		new ReportChoice(ReportKind.Purchases, "Purchases (invoices)"),
		new ReportChoice(ReportKind.Profit, "Profit by drug"),
		new ReportChoice(ReportKind.TopSellingDrugs, "Top-selling drugs"),
		new ReportChoice(ReportKind.Expiry, "Expiry"),
		new ReportChoice(ReportKind.LowStock, "Low stock")
	};

	public IReadOnlyList<ReportBranchFilterChoice> BranchFilterChoices { get; }

	public ObservableCollection<string> DimensionFilterChoices { get; } = new ObservableCollection<string> { "All" };

	public ObservableCollection<ReportMonth> Months { get; } = new ObservableCollection<ReportMonth>(from month in Enumerable.Range(1, 12)
		select new ReportMonth(month, CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(month)));

	public ObservableCollection<int> Years { get; } = new ObservableCollection<int>();

	public bool HasReportRows
	{
		get
		{
			DataView table = Table;
			if (table != null)
			{
				return table.Count > 0;
			}
			return false;
		}
	}

	public bool ShowEmptyReportState
	{
		get
		{
			DataView table = Table;
			if (table != null)
			{
				return table.Count == 0;
			}
			return false;
		}
	}

	public bool CanNavigateNext
	{
		get
		{
			if (SelectedYear >= DateTime.Today.Year)
			{
				if (SelectedYear == DateTime.Today.Year)
				{
					return SelectedMonth < DateTime.Today.Month;
				}
				return false;
			}
			return true;
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public ReportBranchFilterChoice SelectedBranchFilter
	{
		get
		{
			return _selectedBranchFilter;
		}
		[MemberNotNull("_selectedBranchFilter")]
		set
		{
			if (!EqualityComparer<ReportBranchFilterChoice>.Default.Equals(_selectedBranchFilter, value))
			{
				OnPropertyChanging(nameof(SelectedBranchFilter));
				_selectedBranchFilter = value;
				OnSelectedBranchFilterChanged(value);
				OnPropertyChanged(nameof(SelectedBranchFilter));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public ReportChoice SelectedReport
	{
		get
		{
			return _selectedReport;
		}
		[MemberNotNull("_selectedReport")]
		set
		{
			if (!EqualityComparer<ReportChoice>.Default.Equals(_selectedReport, value))
			{
				OnPropertyChanging(nameof(SelectedReport));
				_selectedReport = value;
				OnSelectedReportChanged(value);
				OnPropertyChanged(nameof(SelectedReport));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public int SelectedMonth
	{
		get
		{
			return _selectedMonth;
		}
		set
		{
			if (!EqualityComparer<int>.Default.Equals(_selectedMonth, value))
			{
				OnPropertyChanging(nameof(SelectedMonth));
				_selectedMonth = value;
				OnSelectedMonthChanged(value);
				OnPropertyChanged(nameof(SelectedMonth));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public int SelectedYear
	{
		get
		{
			return _selectedYear;
		}
		set
		{
			if (!EqualityComparer<int>.Default.Equals(_selectedYear, value))
			{
				OnPropertyChanging(nameof(SelectedYear));
				_selectedYear = value;
				OnSelectedYearChanged(value);
				OnPropertyChanged(nameof(SelectedYear));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public DateTime FromDate
	{
		get
		{
			return _fromDate;
		}
		set
		{
			if (!EqualityComparer<DateTime>.Default.Equals(_fromDate, value))
			{
				OnPropertyChanging(nameof(FromDate));
				_fromDate = value;
				OnPropertyChanged(nameof(FromDate));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public DateTime ToDate
	{
		get
		{
			return _toDate;
		}
		set
		{
			if (!EqualityComparer<DateTime>.Default.Equals(_toDate, value))
			{
				OnPropertyChanging(nameof(ToDate));
				_toDate = value;
				OnPropertyChanged(nameof(ToDate));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public DataView? Table
	{
		get
		{
			return _table;
		}
		set
		{
			if (!EqualityComparer<DataView>.Default.Equals(_table, value))
			{
				OnPropertyChanging(nameof(Table));
				OnPropertyChanging(nameof(HasReportRows));
				OnPropertyChanging(nameof(ShowEmptyReportState));
				_table = value;
				OnPropertyChanged(nameof(Table));
				OnPropertyChanged(nameof(HasReportRows));
				OnPropertyChanged(nameof(ShowEmptyReportState));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string PeriodLabel
	{
		get
		{
			return _periodLabel;
		}
		[MemberNotNull("_periodLabel")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_periodLabel, value))
			{
				OnPropertyChanging(nameof(PeriodLabel));
				_periodLabel = value;
				OnPropertyChanged(nameof(PeriodLabel));
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
	public string SelectedQuickPeriod
	{
		get
		{
			return _selectedQuickPeriod;
		}
		[MemberNotNull("_selectedQuickPeriod")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_selectedQuickPeriod, value))
			{
				OnPropertyChanging(nameof(SelectedQuickPeriod));
				_selectedQuickPeriod = value;
				OnPropertyChanged(nameof(SelectedQuickPeriod));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string SelectedDimensionFilter
	{
		get
		{
			return _selectedDimensionFilter;
		}
		[MemberNotNull("_selectedDimensionFilter")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_selectedDimensionFilter, value))
			{
				OnPropertyChanging(nameof(SelectedDimensionFilter));
				_selectedDimensionFilter = value;
				OnSelectedDimensionFilterChanged(value);
				OnPropertyChanged(nameof(SelectedDimensionFilter));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string DimensionFilterLabel
	{
		get
		{
			return _dimensionFilterLabel;
		}
		[MemberNotNull("_dimensionFilterLabel")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_dimensionFilterLabel, value))
			{
				OnPropertyChanging(nameof(DimensionFilterLabel));
				_dimensionFilterLabel = value;
				OnPropertyChanged(nameof(DimensionFilterLabel));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool ShowDimensionFilter
	{
		get
		{
			return _showDimensionFilter;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_showDimensionFilter, value))
			{
				OnPropertyChanging(nameof(ShowDimensionFilter));
				_showDimensionFilter = value;
				OnPropertyChanged(nameof(ShowDimensionFilter));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand RefreshCommand => refreshCommand ?? (refreshCommand = new AsyncRelayCommand(RefreshAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand<string> SelectQuickPeriodCommand => selectQuickPeriodCommand ?? (selectQuickPeriodCommand = new AsyncRelayCommand<string>(SelectQuickPeriodAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand RunCustomPeriodCommand => runCustomPeriodCommand ?? (runCustomPeriodCommand = new AsyncRelayCommand(RunCustomPeriodAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand PreviousMonthCommand => previousMonthCommand ?? (previousMonthCommand = new AsyncRelayCommand(PreviousMonthAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand NextMonthCommand => nextMonthCommand ?? (nextMonthCommand = new AsyncRelayCommand(NextMonthAsync, () => CanNavigateNext));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand<DataRowView?> OpenInvoiceCommand => openInvoiceCommand ?? (openInvoiceCommand = new AsyncRelayCommand<DataRowView>(OpenInvoiceAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand ExportPdfCommand => exportPdfCommand ?? (exportPdfCommand = new AsyncRelayCommand(ExportPdfAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand ExportExcelCommand => exportExcelCommand ?? (exportExcelCommand = new AsyncRelayCommand(ExportExcelAsync));

	public ReportsPageViewModel(IServiceScopeFactory scopeFactory, IFilePickerService filePicker, TabularExportService exports, IInvoiceDetailDialogService invoiceDialog, DataRetentionSettingsStore retentionSettingsStore)
	{
		_scopeFactory = scopeFactory;
		_filePicker = filePicker;
		_exports = exports;
		_invoiceDialog = invoiceDialog;
		_retentionSettingsStore = retentionSettingsStore;
		RebuildYearOptions();
		DateTime today = DateTime.Today;
		SelectedMonth = today.Month;
		SelectedYear = today.Year;
		FromDate = new DateTime(today.Year, today.Month, 1);
		ToDate = today;
		BranchFilterChoices = new _003C_003Ez__ReadOnlyArray<ReportBranchFilterChoice>(new ReportBranchFilterChoice[2]
		{
			new ReportBranchFilterChoice(ReportBranchScope.CurrentBranchOnly, "Current branch only"),
			new ReportBranchFilterChoice(ReportBranchScope.ConsolidatedNetwork, "Consolidated network summary")
		});
		SelectedBranchFilter = BranchFilterChoices[0];
		_suppressReportChangeRefresh = true;
		SelectedReport = ReportChoices[0];
		_suppressReportChangeRefresh = false;
		SelectedDimensionFilter = "All";
	}

	public async Task LoadAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		RebuildYearOptions();
		await RefreshWithFiltersAsync(cancellationToken);
	}

	private async Task RefreshWithFiltersAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		await LoadDimensionFiltersAsync(cancellationToken);
		await RefreshAsync(cancellationToken);
	}

	private async Task LoadDimensionFiltersAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		if (!ShowDimensionFilter)
		{
			DimensionFilterChoices.Clear();
			DimensionFilterChoices.Add("All");
			_suppressReportChangeRefresh = true;
			SelectedDimensionFilter = "All";
			_suppressReportChangeRefresh = false;
			return;
		}
		try
		{
			ClampToActiveRetentionWindow();
			DateOnly dateOnly = DateOnly.FromDateTime(FromDate);
			DateOnly to = DateOnly.FromDateTime(ToDate);
			using IServiceScope scope = _scopeFactory.CreateScope();
			IReadOnlyList<string> readOnlyList = await scope.ServiceProvider.GetRequiredService<ReportsDashboardService>().GetDimensionFilterOptionsAsync(SelectedReport.Kind, dateOnly, to, SelectedBranchFilter.Scope, cancellationToken);
			string selectedDimensionFilter = SelectedDimensionFilter;
			DimensionFilterChoices.Clear();
			foreach (string item in readOnlyList)
			{
				DimensionFilterChoices.Add(item);
			}
			_suppressReportChangeRefresh = true;
			SelectedDimensionFilter = (DimensionFilterChoices.Contains(selectedDimensionFilter) ? selectedDimensionFilter : "All");
			_suppressReportChangeRefresh = false;
		}
		catch
		{
			DimensionFilterChoices.Clear();
			DimensionFilterChoices.Add("All");
			_suppressReportChangeRefresh = true;
			SelectedDimensionFilter = "All";
			_suppressReportChangeRefresh = false;
		}
	}

	private async Task RefreshAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		try
		{
			ClampToActiveRetentionWindow();
			DateOnly from = DateOnly.FromDateTime(FromDate);
			DateOnly to = DateOnly.FromDateTime(ToDate);
			using IServiceScope scope = _scopeFactory.CreateScope();
			ReportTable reportTable = await scope.ServiceProvider.GetRequiredService<ReportsDashboardService>().GetReportAsync(SelectedReport.Kind, from, to, SelectedBranchFilter.Scope, ShowDimensionFilter ? SelectedDimensionFilter : null, cancellationToken);
			DataTable dataTable = new DataTable(reportTable.Title);
			foreach (string column in reportTable.Columns)
			{
				dataTable.Columns.Add(column);
			}
			if (reportTable.RowKeys != null)
			{
				dataTable.Columns.Add("__key");
			}
			for (int i = 0; i < reportTable.Rows.Count; i++)
			{
				List<object> list = reportTable.Rows[i].Cast<object>().ToList();
				if (reportTable.RowKeys != null)
				{
					list.Add((i < reportTable.RowKeys.Count) ? reportTable.RowKeys[i] : string.Empty);
				}
				dataTable.Rows.Add(list.ToArray());
			}
			Table = dataTable.DefaultView;
			PeriodLabel = $"{from:yyyy-MM-dd} to {to:yyyy-MM-dd} · {reportTable.Title}";
			StatusMessage = $"{reportTable.Rows.Count} row(s).";
			ErrorMessage = string.Empty;
		}
		catch (Exception ex)
		{
			ErrorMessage = ex.Message;
		}
	}

	public bool PrepareAssistantRequest(string reportKey, DateOnly from, DateOnly to)
	{
		if (!Enum.TryParse<ReportKind>(reportKey, out var kind))
		{
			return false;
		}
		ReportChoice reportChoice = ReportChoices.FirstOrDefault((ReportChoice item) => item.Kind == kind) ?? ReportChoices.FirstOrDefault((ReportChoice item) => item.Kind == ReportKind.Sales);
		if ((object)reportChoice == null)
		{
			return false;
		}
		_suppressReportChangeRefresh = true;
		SelectedReport = reportChoice;
		_suppressReportChangeRefresh = false;
		FromDate = from.ToDateTime(TimeOnly.MinValue);
		ToDate = to.ToDateTime(TimeOnly.MinValue);
		return true;
	}

	public Task RefreshNowAsync()
	{
		return RefreshWithFiltersAsync();
	}

	private async Task SelectQuickPeriodAsync(string period)
	{
		DateTime today = DateTime.Today;
		switch (period)
		{
		case "Today":
			FromDate = today;
			ToDate = today;
			break;
		case "This month":
			SelectedMonth = today.Month;
			SelectedYear = today.Year;
			FromDate = new DateTime(today.Year, today.Month, 1);
			ToDate = today;
			break;
		case "Last month":
			SetMonth(today.AddMonths(-1));
			ToDate = new DateTime(SelectedYear, SelectedMonth, DateTime.DaysInMonth(SelectedYear, SelectedMonth));
			break;
		case "Last 3 months":
			FromDate = new DateTime(today.Year, today.Month, 1).AddMonths(-2);
			ToDate = today;
			break;
		case "This year":
			FromDate = new DateTime(today.Year, 1, 1);
			ToDate = today;
			break;
		case "Active window":
			FromDate = GetActiveWindowStartLocal().Date;
			ToDate = today;
			break;
		default:
			throw new ArgumentException("Unknown report period chip.", "period");
		}
		SelectedQuickPeriod = period;
		await RefreshWithFiltersAsync();
	}

	private async Task RunCustomPeriodAsync()
	{
		SelectedQuickPeriod = string.Empty;
		await RefreshWithFiltersAsync();
	}

	private DateTime GetActiveWindowStartLocal()
	{
		DataRetentionPeriod period = _retentionSettingsStore.Load().Period;
		DateTime cutoffUtc = StatutoryRetentionPolicy.GetCutoffUtc(period);
		if (period != DataRetentionPeriod.Permanent)
		{
			return cutoffUtc.ToLocalTime();
		}
		return new DateTime(DateTime.Today.Year - 10, 1, 1);
	}

	private void RebuildYearOptions()
	{
		DateTime today = DateTime.Today;
		int year = GetActiveWindowStartLocal().Year;
		Years.Clear();
		foreach (int item in Enumerable.Range(year, today.Year - year + 1))
		{
			Years.Add(item);
		}
	}

	private void ClampToActiveRetentionWindow()
	{
		DateTime date = GetActiveWindowStartLocal().Date;
		DateTime today = DateTime.Today;
		if (FromDate < date)
		{
			FromDate = date;
		}
		if (ToDate > today)
		{
			ToDate = today;
		}
		if (FromDate > ToDate)
		{
			FromDate = ToDate;
		}
	}

	private async Task PreviousMonthAsync()
	{
		SelectedQuickPeriod = string.Empty;
		SetMonth(new DateTime(SelectedYear, SelectedMonth, 1).AddMonths(-1));
		await RefreshWithFiltersAsync();
	}

	private async Task NextMonthAsync()
	{
		if (CanNavigateNext)
		{
			SelectedQuickPeriod = string.Empty;
			SetMonth(new DateTime(SelectedYear, SelectedMonth, 1).AddMonths(1));
			await RefreshWithFiltersAsync();
		}
	}

	private async Task OpenInvoiceAsync(DataRowView? row)
	{
		if (row == null || !row.Row.Table.Columns.Contains("__key"))
		{
			return;
		}
		string text = Convert.ToString(row["__key"]) ?? string.Empty;
		if (string.IsNullOrWhiteSpace(text))
		{
			return;
		}
		try
		{
			using IServiceScope scope = _scopeFactory.CreateScope();
			InvoiceDetail invoiceDetail = await scope.ServiceProvider.GetRequiredService<ReportsDashboardService>().GetInvoiceDetailAsync(text);
			if ((object)invoiceDetail == null)
			{
				ErrorMessage = "That invoice could not be found.";
				return;
			}
			ErrorMessage = string.Empty;
			_invoiceDialog.Show(invoiceDetail);
		}
		catch (Exception ex)
		{
			ErrorMessage = ex.Message;
		}
	}

	private async Task ExportPdfAsync()
	{
		await ExportAsync("pdf");
	}

	private async Task ExportExcelAsync()
	{
		await ExportAsync("xlsx");
	}

	private async Task ExportAsync(string extension)
	{
		if (Table == null)
		{
			await RefreshAsync();
		}
		if (Table == null)
		{
			return;
		}
		string value = string.Join("-", SelectedReport.Name.Split(Path.GetInvalidFileNameChars()));
		string suggestedName = $"{value}-{FromDate:yyyyMMdd}-{ToDate:yyyyMMdd}";
		string destination = _filePicker.PickExportDestination(extension, suggestedName);
		if (destination == null)
		{
			return;
		}
		DataTable dataTable = Table.Table ?? throw new InvalidOperationException("The report has no table columns.");
		DataColumn[] visible = (from DataColumn column in dataTable.Columns
			where column.ColumnName != "__key"
			select column).ToArray();
		string[] columns = visible.Select((DataColumn column) => column.ColumnName).ToArray();
		IReadOnlyList<string>[] rows = Table.Cast<DataRowView>().Select((Func<DataRowView, IReadOnlyList<string>>)((DataRowView row) => visible.Select((DataColumn column) => Convert.ToString(row[column.ColumnName]) ?? string.Empty).ToArray())).ToArray();
		await _exports.ExportAsync(destination, SelectedReport.Name + " (" + PeriodLabel + ")", columns, rows);
		StatusMessage = $"Exported {rows.Length} row(s) to {destination}.";
		ErrorMessage = string.Empty;
	}

	private void ApplySelectedMonth()
	{
		bool flag = SelectedYear <= 0;
		if (!flag)
		{
			int selectedMonth = SelectedMonth;
			bool flag2 = ((selectedMonth < 1 || selectedMonth > 12) ? true : false);
			flag = flag2;
		}
		if (!flag)
		{
			DateTime dateTime = (FromDate = new DateTime(SelectedYear, SelectedMonth, 1));
			ToDate = ((SelectedYear == DateTime.Today.Year && SelectedMonth == DateTime.Today.Month) ? DateTime.Today : dateTime.AddMonths(1).AddDays(-1.0));
			OnPropertyChanged("CanNavigateNext");
			NextMonthCommand.NotifyCanExecuteChanged();
		}
	}

	private void SetMonth(DateTime value)
	{
		if (value > new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1))
		{
			value = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
		}
		SelectedYear = value.Year;
		SelectedMonth = value.Month;
		OnPropertyChanged("CanNavigateNext");
		NextMonthCommand.NotifyCanExecuteChanged();
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnSelectedBranchFilterChanged(ReportBranchFilterChoice value)
	{
		if (!_suppressReportChangeRefresh)
		{
			RefreshWithFiltersAsync();
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnSelectedReportChanged(ReportChoice value)
	{
		ShowDimensionFilter = ReportsDashboardService.UsesDimensionFilter(value.Kind);
		DimensionFilterLabel = ReportsDashboardService.DimensionFilterLabel(value.Kind);
		if (!_suppressReportChangeRefresh)
		{
			RefreshWithFiltersAsync();
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnSelectedMonthChanged(int value)
	{
		if (Months.Count != 0)
		{
			if (SelectedYear == DateTime.Today.Year && value > DateTime.Today.Month)
			{
				SelectedMonth = DateTime.Today.Month;
			}
			else if (value >= 1 && value <= 12)
			{
				ApplySelectedMonth();
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnSelectedYearChanged(int value)
	{
		if (Years.Count == 0)
		{
			return;
		}
		int num = Years.FirstOrDefault();
		if (num != 0)
		{
			if (value > DateTime.Today.Year)
			{
				SelectedYear = DateTime.Today.Year;
			}
			else if (value >= num && value <= DateTime.Today.Year)
			{
				ApplySelectedMonth();
				OnPropertyChanged("CanNavigateNext");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnSelectedDimensionFilterChanged(string value)
	{
		if (!_suppressReportChangeRefresh)
		{
			RefreshAsync();
		}
	}
}
