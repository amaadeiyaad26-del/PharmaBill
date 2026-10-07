using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel.__Internals;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using PharmaBill.App.Services;
using PharmaBill.Data.Services;

namespace PharmaBill.App.ViewModels;

public class WholesaleReportsPageViewModel : ObservableObject, ILoadablePage
{
	private readonly IServiceScopeFactory _scopeFactory;

	private readonly IFilePickerService _filePicker;

	private readonly TabularExportService _exports;

	[ObservableProperty]
	private DateTime _fromDate;

	[ObservableProperty]
	private DateTime _toDate;

	[ObservableProperty]
	private string _analysisDimension = "customer";

	[ObservableProperty]
	private string _statusMessage = string.Empty;

	[ObservableProperty]
	private string _errorMessage = string.Empty;

	[ObservableProperty]
	private string _gstr3bNotice = "For reference only; verify with your CA.";

	[ObservableProperty]
	private string _gstr3bFigures = string.Empty;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? refreshCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? exportGstr1CsvCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? exportGstr1JsonCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? exportSalesRegisterCommand;

	public ObservableCollection<WholesaleSalesRegisterRow> SalesRegister { get; } = new ObservableCollection<WholesaleSalesRegisterRow>();

	public ObservableCollection<WholesalePurchaseRegisterRow> PurchaseRegister { get; } = new ObservableCollection<WholesalePurchaseRegisterRow>();

	public ObservableCollection<GstRateSummary> HsnSummary { get; } = new ObservableCollection<GstRateSummary>();

	public ObservableCollection<WholesaleSalesAnalysisRow> Analysis { get; } = new ObservableCollection<WholesaleSalesAnalysisRow>();

	public string[] AnalysisDimensions { get; } = new string[5] { "customer", "item", "company", "salesman", "area" };

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
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.FromDate);
				_fromDate = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.FromDate);
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
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.ToDate);
				_toDate = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.ToDate);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string AnalysisDimension
	{
		get
		{
			return _analysisDimension;
		}
		[MemberNotNull("_analysisDimension")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_analysisDimension, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.AnalysisDimension);
				_analysisDimension = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.AnalysisDimension);
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
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.StatusMessage);
				_statusMessage = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.StatusMessage);
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
	public string Gstr3bNotice
	{
		get
		{
			return _gstr3bNotice;
		}
		[MemberNotNull("_gstr3bNotice")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_gstr3bNotice, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.Gstr3bNotice);
				_gstr3bNotice = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.Gstr3bNotice);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string Gstr3bFigures
	{
		get
		{
			return _gstr3bFigures;
		}
		[MemberNotNull("_gstr3bFigures")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_gstr3bFigures, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.Gstr3bFigures);
				_gstr3bFigures = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.Gstr3bFigures);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand RefreshCommand => refreshCommand ?? (refreshCommand = new AsyncRelayCommand(RefreshAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand ExportGstr1CsvCommand => exportGstr1CsvCommand ?? (exportGstr1CsvCommand = new AsyncRelayCommand(ExportGstr1CsvAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand ExportGstr1JsonCommand => exportGstr1JsonCommand ?? (exportGstr1JsonCommand = new AsyncRelayCommand(ExportGstr1JsonAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand ExportSalesRegisterCommand => exportSalesRegisterCommand ?? (exportSalesRegisterCommand = new AsyncRelayCommand(ExportSalesRegisterAsync));

	public WholesaleReportsPageViewModel(IServiceScopeFactory scopeFactory, IFilePickerService filePicker, TabularExportService exports)
	{
		_scopeFactory = scopeFactory;
		_filePicker = filePicker;
		_exports = exports;
		FromDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
		ToDate = DateTime.Today;
	}

	public async Task LoadAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		await RefreshAsync(cancellationToken);
	}

	[RelayCommand]
	private async Task RefreshAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		_ = 4;
		try
		{
			using IServiceScope scope = _scopeFactory.CreateScope();
			WholesaleGstReportsService reports = scope.ServiceProvider.GetRequiredService<WholesaleGstReportsService>();
			DateOnly from = DateOnly.FromDateTime(FromDate);
			DateOnly to = DateOnly.FromDateTime(ToDate);
			IReadOnlyList<WholesaleSalesRegisterRow> sales = await reports.GetSalesRegisterAsync(from, to, cancellationToken);
			IReadOnlyList<WholesalePurchaseRegisterRow> purchases = await reports.GetPurchaseRegisterAsync(from, to, cancellationToken);
			IReadOnlyList<GstRateSummary> hsn = await reports.GetHsnSummaryAsync(from, to, cancellationToken);
			IReadOnlyList<WholesaleSalesAnalysisRow> analysis = await reports.GetSalesAnalysisAsync(from, to, AnalysisDimension, cancellationToken);
			Gstr3bReferenceSummary gstr3bReferenceSummary = await reports.GetGstr3bReferenceSummaryAsync(from, to, cancellationToken);
			Replace(SalesRegister, sales);
			Replace(PurchaseRegister, purchases);
			Replace(HsnSummary, hsn);
			Replace(Analysis, analysis);
			Gstr3bFigures = $"Taxable {MoneyFormat.Rupees(gstr3bReferenceSummary.Taxable)}; CGST {MoneyFormat.Rupees(gstr3bReferenceSummary.Cgst)}; SGST {MoneyFormat.Rupees(gstr3bReferenceSummary.Sgst)}; IGST {MoneyFormat.Rupees(gstr3bReferenceSummary.Igst)}";
			ErrorMessage = string.Empty;
		}
		catch (Exception ex) when ((ex is ArgumentException || ex is InvalidOperationException) ? true : false)
		{
			ErrorMessage = ex.Message;
		}
	}

	[RelayCommand]
	private async Task ExportGstr1CsvAsync()
	{
		await ExportGstr1Async("csv");
	}

	[RelayCommand]
	private async Task ExportGstr1JsonAsync()
	{
		await ExportGstr1Async("json");
	}

	[RelayCommand]
	private async Task ExportSalesRegisterAsync()
	{
		string path = _filePicker.PickExportDestination("xlsx", "Wholesale-sales-register");
		if (path != null)
		{
			IReadOnlyList<string>[] rows = ((IEnumerable<WholesaleSalesRegisterRow>)SalesRegister).Select((Func<WholesaleSalesRegisterRow, IReadOnlyList<string>>)((WholesaleSalesRegisterRow row) => new _003C_003Ez__ReadOnlyArray<string>(new string[10]
			{
				row.InvoiceNo,
				row.InvoiceAtUtc.ToLocalTime().ToString("yyyy-MM-dd"),
				row.CustomerName,
				row.Gstin ?? string.Empty,
				row.Hsn,
				row.TaxableAmount.ToString("0.00", CultureInfo.InvariantCulture),
				row.Cgst.ToString("0.00", CultureInfo.InvariantCulture),
				row.Sgst.ToString("0.00", CultureInfo.InvariantCulture),
				row.Igst.ToString("0.00", CultureInfo.InvariantCulture),
				row.Total.ToString("0.00", CultureInfo.InvariantCulture)
			}))).ToArray();
			await _exports.ExportAsync(path, "Wholesale sales register", new _003C_003Ez__ReadOnlyArray<string>(new string[10] { "Invoice", "Date", "Customer", "GSTIN", "HSN", "Taxable", "CGST", "SGST", "IGST", "Total" }), rows);
			StatusMessage = path;
		}
	}

	private async Task ExportGstr1Async(string extension)
	{
		string path = _filePicker.PickExportDestination(extension, $"GSTR1-reference-{FromDate:yyyyMMdd}-{ToDate:yyyyMMdd}");
		if (path == null)
		{
			return;
		}
		using IServiceScope scope = _scopeFactory.CreateScope();
		WholesaleGstReportsService requiredService = scope.ServiceProvider.GetRequiredService<WholesaleGstReportsService>();
		string text = ((!(extension == "csv")) ? (await requiredService.ExportGstr1JsonAsync(DateOnly.FromDateTime(FromDate), DateOnly.FromDateTime(ToDate))) : (await requiredService.ExportGstr1CsvAsync(DateOnly.FromDateTime(FromDate), DateOnly.FromDateTime(ToDate))));
		string contents = text;
		await File.WriteAllTextAsync(path, contents, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
		StatusMessage = path;
		ErrorMessage = string.Empty;
	}

	private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> values)
	{
		target.Clear();
		foreach (T value in values)
		{
			target.Add(value);
		}
	}
}
