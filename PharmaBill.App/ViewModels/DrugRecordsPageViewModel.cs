using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PharmaBill.App.Services;
using PharmaBill.Core.Security;
using PharmaBill.Data.Services;

namespace PharmaBill.App.ViewModels;

public sealed class DrugRecordsPageViewModel : SectionPageViewModel, ILoadablePage
{
	private readonly DrugRecordsService _recordsService;

	private readonly SensitiveAccessService _sensitiveAccess;

	private readonly IConfirmationService _confirmation;

	private readonly IFilePickerService _filePicker;

	private readonly TabularExportService _exportService;

	private readonly CurrentSession _currentSession;

	private bool _authorized;

	private string _searchText = string.Empty;

	private DrugRecordsPeriod _period;

	private string _recordType = "Both";

	private int _selectedMonth;

	private int _selectedYear;

	private DateTime? _customStartDate;

	private DateTime? _customEndDate;

	private bool _hidePatientPhoneNumber;

	private string _statusMessage = string.Empty;

	private string _errorMessage = string.Empty;

	private decimal _openingBalance;

	private decimal _purchased;

	private decimal _sold;

	private decimal _closingBalance;

	private decimal _currentStock;

	private int _uniquePatients;

	private bool _stockMismatch;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand<string>? setPeriodCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? searchDrugsCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? runReportCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? exportPdfCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? exportExcelCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? printPdfCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? showPdfForSharingCommand;

	public IReadOnlyList<DrugRecordsPeriod> PeriodOptions { get; }

	public IReadOnlyList<string> RecordTypeOptions { get; }

	public IReadOnlyList<int> MonthOptions { get; }

	public IReadOnlyList<int> YearOptions { get; }

	public ObservableCollection<DrugRecordsDrugChoiceRow> SearchResults { get; } = new ObservableCollection<DrugRecordsDrugChoiceRow>();

	public ObservableCollection<DrugRecordsRow> Records { get; } = new ObservableCollection<DrugRecordsRow>();

	public bool IsCustomPeriod => Period == DrugRecordsPeriod.Custom;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string SearchText
	{
		get
		{
			return _searchText;
		}
		[MemberNotNull("_searchText")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_searchText, value))
			{
				OnPropertyChanging(nameof(SearchText));
				_searchText = value;
				OnPropertyChanged(nameof(SearchText));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public DrugRecordsPeriod Period
	{
		get
		{
			return _period;
		}
		set
		{
			if (!EqualityComparer<DrugRecordsPeriod>.Default.Equals(_period, value))
			{
				OnPropertyChanging(nameof(Period));
				_period = value;
				OnPeriodChanged(value);
				OnPropertyChanged(nameof(Period));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string RecordType
	{
		get
		{
			return _recordType;
		}
		[MemberNotNull("_recordType")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_recordType, value))
			{
				OnPropertyChanging(nameof(RecordType));
				_recordType = value;
				OnPropertyChanged(nameof(RecordType));
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
				OnPropertyChanged(nameof(SelectedYear));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public DateTime? CustomStartDate
	{
		get
		{
			return _customStartDate;
		}
		set
		{
			if (!EqualityComparer<DateTime?>.Default.Equals(_customStartDate, value))
			{
				OnPropertyChanging(nameof(CustomStartDate));
				_customStartDate = value;
				OnPropertyChanged(nameof(CustomStartDate));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public DateTime? CustomEndDate
	{
		get
		{
			return _customEndDate;
		}
		set
		{
			if (!EqualityComparer<DateTime?>.Default.Equals(_customEndDate, value))
			{
				OnPropertyChanging(nameof(CustomEndDate));
				_customEndDate = value;
				OnPropertyChanged(nameof(CustomEndDate));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool HidePatientPhoneNumber
	{
		get
		{
			return _hidePatientPhoneNumber;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_hidePatientPhoneNumber, value))
			{
				OnPropertyChanging(nameof(HidePatientPhoneNumber));
				_hidePatientPhoneNumber = value;
				OnPropertyChanged(nameof(HidePatientPhoneNumber));
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
	public decimal OpeningBalance
	{
		get
		{
			return _openingBalance;
		}
		set
		{
			if (!EqualityComparer<decimal>.Default.Equals(_openingBalance, value))
			{
				OnPropertyChanging(nameof(OpeningBalance));
				_openingBalance = value;
				OnPropertyChanged(nameof(OpeningBalance));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public decimal Purchased
	{
		get
		{
			return _purchased;
		}
		set
		{
			if (!EqualityComparer<decimal>.Default.Equals(_purchased, value))
			{
				OnPropertyChanging(nameof(Purchased));
				_purchased = value;
				OnPropertyChanged(nameof(Purchased));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public decimal Sold
	{
		get
		{
			return _sold;
		}
		set
		{
			if (!EqualityComparer<decimal>.Default.Equals(_sold, value))
			{
				OnPropertyChanging(nameof(Sold));
				_sold = value;
				OnPropertyChanged(nameof(Sold));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public decimal ClosingBalance
	{
		get
		{
			return _closingBalance;
		}
		set
		{
			if (!EqualityComparer<decimal>.Default.Equals(_closingBalance, value))
			{
				OnPropertyChanging(nameof(ClosingBalance));
				_closingBalance = value;
				OnPropertyChanged(nameof(ClosingBalance));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public decimal CurrentStock
	{
		get
		{
			return _currentStock;
		}
		set
		{
			if (!EqualityComparer<decimal>.Default.Equals(_currentStock, value))
			{
				OnPropertyChanging(nameof(CurrentStock));
				_currentStock = value;
				OnPropertyChanged(nameof(CurrentStock));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public int UniquePatients
	{
		get
		{
			return _uniquePatients;
		}
		set
		{
			if (!EqualityComparer<int>.Default.Equals(_uniquePatients, value))
			{
				OnPropertyChanging(nameof(UniquePatients));
				_uniquePatients = value;
				OnPropertyChanged(nameof(UniquePatients));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool StockMismatch
	{
		get
		{
			return _stockMismatch;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_stockMismatch, value))
			{
				OnPropertyChanging(nameof(StockMismatch));
				_stockMismatch = value;
				OnPropertyChanged(nameof(StockMismatch));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand<string> SetPeriodCommand => setPeriodCommand ?? (setPeriodCommand = new RelayCommand<string>(SetPeriod));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand SearchDrugsCommand => searchDrugsCommand ?? (searchDrugsCommand = new AsyncRelayCommand(SearchDrugsAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand RunReportCommand => runReportCommand ?? (runReportCommand = new AsyncRelayCommand(RunReportAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand ExportPdfCommand => exportPdfCommand ?? (exportPdfCommand = new AsyncRelayCommand(ExportPdfAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand ExportExcelCommand => exportExcelCommand ?? (exportExcelCommand = new AsyncRelayCommand(ExportExcelAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand PrintPdfCommand => printPdfCommand ?? (printPdfCommand = new AsyncRelayCommand(PrintPdfAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand ShowPdfForSharingCommand => showPdfForSharingCommand ?? (showPdfForSharingCommand = new AsyncRelayCommand(ShowPdfForSharingAsync));

	public DrugRecordsPageViewModel(DrugRecordsService recordsService, IFilePickerService filePicker, TabularExportService exportService, SensitiveAccessService sensitiveAccess, IConfirmationService confirmation, CurrentSession currentSession)
		: base("Drug Records")
	{
		_recordsService = recordsService;
		_filePicker = filePicker;
		_exportService = exportService;
		_sensitiveAccess = sensitiveAccess;
		_confirmation = confirmation;
		_currentSession = currentSession;
		PeriodOptions = Enum.GetValues<DrugRecordsPeriod>();
		RecordTypeOptions = new _003C_003Ez__ReadOnlyArray<string>(new string[3] { "Both", "Sale", "Purchase" });
		MonthOptions = Enumerable.Range(1, 12).ToArray();
		YearOptions = Enumerable.Range(DateTime.Now.Year - 10, 11).Reverse().ToArray();
		SelectedYear = DateTime.Now.Year;
		SelectedMonth = DateTime.Now.Month;
		Period = DrugRecordsPeriod.ThisMonth;
		CustomStartDate = DateTime.Today;
		CustomEndDate = DateTime.Today;
	}

	private void SetPeriod(string value)
	{
		if (!Enum.TryParse<DrugRecordsPeriod>(value, ignoreCase: true, out var result))
		{
			throw new ArgumentException("Unsupported Drug Records period '" + value + "'.", "value");
		}
		Period = result;
	}

	public async Task LoadAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		_authorized = await _sensitiveAccess.RequestCurrentUserPinAsync(Application.Current?.MainWindow, "open Drug Records", cancellationToken);
		if (!_authorized)
		{
			ErrorMessage = "Access cancelled or PIN verification failed.";
			return;
		}
		StatusMessage = "Access granted. Search a brand or salt and select medicines.";
		ErrorMessage = string.Empty;
	}

	private async Task SearchDrugsAsync()
	{
		if (!_authorized || _currentSession.User == null)
		{
			ErrorMessage = "Unlock Drug Records before searching.";
			return;
		}
		try
		{
			IReadOnlyList<DrugRecordsDrugChoice> readOnlyList = await _recordsService.SearchDrugsAsync(SearchText, _currentSession.User.Id);
			SearchResults.Clear();
			foreach (DrugRecordsDrugChoice item in readOnlyList)
			{
				SearchResults.Add(new DrugRecordsDrugChoiceRow(item));
			}
			StatusMessage = $"{readOnlyList.Count} matching medicine(s).";
			ErrorMessage = string.Empty;
		}
		catch (Exception ex)
		{
			ErrorMessage = ex.Message;
		}
	}

	private async Task RunReportAsync()
	{
		if (!_authorized || _currentSession.User == null)
		{
			ErrorMessage = "Unlock Drug Records before running a search.";
			return;
		}
		Guid[] array = (from item in SearchResults
			where item.IsSelected
			select item.DrugId).ToArray();
		if (array.Length == 0)
		{
			ErrorMessage = "Select at least one medicine from the search results.";
			return;
		}
		try
		{
			(DateTime, DateTime) range = DrugRecordsPeriodRange.GetRange(Period, DateTime.Now, SelectedMonth, SelectedYear, CustomStartDate.HasValue ? new DateOnly?(DateOnly.FromDateTime(CustomStartDate.Value)) : ((DateOnly?)null), CustomEndDate.HasValue ? new DateOnly?(DateOnly.FromDateTime(CustomEndDate.Value)) : ((DateOnly?)null));
			DrugRecordsReport drugRecordsReport = await _recordsService.SearchAsync(new DrugRecordsQuery(array, range.Item1, range.Item2, RecordType), _currentSession.User.Id);
			Records.Clear();
			foreach (DrugRecordsRow row in drugRecordsReport.Rows)
			{
				Records.Add(row);
			}
			OpeningBalance = drugRecordsReport.Summary.OpeningBalance;
			Purchased = drugRecordsReport.Summary.Purchased;
			Sold = drugRecordsReport.Summary.Sold;
			ClosingBalance = drugRecordsReport.Summary.ClosingBalance;
			CurrentStock = drugRecordsReport.Summary.CurrentStock;
			UniquePatients = drugRecordsReport.Summary.UniquePatients;
			StockMismatch = !drugRecordsReport.Summary.ClosingMatchesCurrentStock;
			StatusMessage = $"{drugRecordsReport.Rows.Count} record(s) from {drugRecordsReport.StartUtc.ToLocalTime():d} through {drugRecordsReport.EndUtc.ToLocalTime().AddDays(-1.0):d}.";
			ErrorMessage = string.Empty;
		}
		catch (Exception ex)
		{
			ErrorMessage = ex.Message;
		}
	}

	private Task ExportPdfAsync()
	{
		return ExportAsync("pdf");
	}

	private Task ExportExcelAsync()
	{
		return ExportAsync("xlsx");
	}

	private Task PrintPdfAsync()
	{
		return ExportAsync("pdf", print: true);
	}

	private Task ShowPdfForSharingAsync()
	{
		return ExportAsync("pdf", print: false, showForSharing: true);
	}

	private async Task ExportAsync(string extension, bool print = false, bool showForSharing = false)
	{
		if (!_authorized || _currentSession.User == null)
		{
			ErrorMessage = "Unlock Drug Records before exporting.";
		}
		else if (!PermissionMatrix.Allows(_currentSession.User.Role, AppPermission.Export))
		{
			ErrorMessage = "Your role cannot export patient Drug Records.";
		}
		else if (!(await _sensitiveAccess.RequestCurrentUserPinAsync(Application.Current?.MainWindow, "export patient drug records")))
		{
			ErrorMessage = "Export cancelled or PIN verification failed.";
		}
		else
		{
			if (!_confirmation.Confirm("This file contains patient details. Share only with authorised persons.", "Confirm sensitive patient data export"))
			{
				return;
			}
			string path = _filePicker.PickExportDestination(extension, "drug-records");
			if (path == null)
			{
				return;
			}
			string[] columns = new string[11]
			{
				"Date/time", "Record type", "Document no.", "Patient / buyer", "Address", "Phone", "Doctor", "Doctor registration no.", "Batch", "Quantity",
				"Running balance"
			};
			IReadOnlyList<string>[] first = new IReadOnlyList<string>[6]
			{
				new _003C_003Ez__ReadOnlyArray<string>(new string[11]
				{
					"Summary: opening balance",
					"",
					"",
					"",
					"",
					"",
					"",
					"",
					"",
					OpeningBalance.ToString("0.##", CultureInfo.InvariantCulture),
					""
				}),
				new _003C_003Ez__ReadOnlyArray<string>(new string[11]
				{
					"Summary: purchased",
					"",
					"",
					"",
					"",
					"",
					"",
					"",
					"",
					Purchased.ToString("0.##", CultureInfo.InvariantCulture),
					""
				}),
				new _003C_003Ez__ReadOnlyArray<string>(new string[11]
				{
					"Summary: sold (net of returns)",
					"",
					"",
					"",
					"",
					"",
					"",
					"",
					"",
					Sold.ToString("0.##", CultureInfo.InvariantCulture),
					""
				}),
				new _003C_003Ez__ReadOnlyArray<string>(new string[11]
				{
					"Summary: closing balance",
					"",
					"",
					"",
					"",
					"",
					"",
					"",
					"",
					ClosingBalance.ToString("0.##", CultureInfo.InvariantCulture),
					""
				}),
				new _003C_003Ez__ReadOnlyArray<string>(new string[11]
				{
					"Summary: current stock",
					"",
					"",
					"",
					"",
					"",
					"",
					"",
					"",
					CurrentStock.ToString("0.##", CultureInfo.InvariantCulture),
					""
				}),
				new _003C_003Ez__ReadOnlyArray<string>(new string[11]
				{
					"Summary: unique patients",
					"",
					"",
					"",
					"",
					"",
					"",
					"",
					"",
					UniquePatients.ToString(CultureInfo.InvariantCulture),
					""
				})
			};
			IEnumerable<IReadOnlyList<string>> second = ((IEnumerable<DrugRecordsRow>)Records).Select((Func<DrugRecordsRow, IReadOnlyList<string>>)((DrugRecordsRow row) => new _003C_003Ez__ReadOnlyArray<string>(new string[11]
			{
				row.AtUtc.ToLocalTime().ToString("dd-MMM-yyyy HH:mm", CultureInfo.InvariantCulture),
				row.RecordType,
				row.DocumentNo,
				row.PatientName,
				row.Address,
				HidePatientPhoneNumber ? string.Empty : row.Phone,
				row.DoctorName,
				row.DoctorRegistrationNo,
				row.BatchNo,
				row.Quantity.ToString("0.##", CultureInfo.InvariantCulture),
				row.RunningBalance.ToString("0.##", CultureInfo.InvariantCulture)
			})));
			IReadOnlyList<IReadOnlyList<string>> rows = first.Concat(second).ToArray();
			try
			{
				await _exportService.ExportAsync(path, "Drug Records", columns, rows);
				await _recordsService.LogExportAsync(_currentSession.User.Id, extension, HidePatientPhoneNumber);
				if (print)
				{
					if (Process.Start(new ProcessStartInfo(path)
					{
						UseShellExecute = true,
						Verb = "print"
					}) == null)
					{
						throw new InvalidOperationException("Windows could not open the Drug Records PDF print handler.");
					}
				}
				else if (showForSharing)
				{
					RetailBillPdfService.ShowInExplorer(path);
				}
				DrugRecordsPageViewModel drugRecordsPageViewModel = this;
				string statusMessage;
				if (print)
				{
					statusMessage = "Drug Records PDF sent to the default print handler: " + path;
				}
				else
				{
					statusMessage = (showForSharing ? ("Drug Records PDF ready to share: " + path) : ("Drug records exported to " + path + "."));
				}
				drugRecordsPageViewModel.StatusMessage = statusMessage;
				ErrorMessage = string.Empty;
			}
			catch (Exception ex)
			{
				ErrorMessage = ex.Message;
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnPeriodChanged(DrugRecordsPeriod value)
	{
		OnPropertyChanged("IsCustomPeriod");
	}
}
