using PharmaBill.Core;
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
using ClosedXML.Excel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PharmaBill.App.Services;
using PharmaBill.Data.Services;

namespace PharmaBill.App.ViewModels;

public class GstReturnsPageViewModel : ObservableObject, ILoadablePage
{
	private readonly IServiceScopeFactory _scopeFactory;

	private readonly IFilePickerService _filePicker;

	private GstPeriodChoice? _selectedPeriod;

	private string _auditStatusLabel = "Load a period to validate.";

	private bool _isAuditClean;

	private decimal _totalTaxableTurnover;

	private decimal _outputCgst;

	private decimal _outputSgst;

	private decimal _outputIgst;

	private decimal _eligibleItc;

	private decimal _netGstPayable;

	private string _statusMessage = string.Empty;

	private string _errorMessage = string.Empty;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? refreshCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? exportGstr1JsonCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? exportGstr3bWorksheetCommand;

	private AsyncRelayCommand? exportGstr1ExcelCommand;

	private AsyncRelayCommand? importGstr2bCommand;

	private AsyncRelayCommand? exportEInvoiceCommand;

	private AsyncRelayCommand? exportEWayBillCommand;

	public ObservableCollection<GstPeriodChoice> PeriodChoices { get; } = new ObservableCollection<GstPeriodChoice>();

	public ObservableCollection<GstTaxAuditIssue> AuditIssues { get; } = new ObservableCollection<GstTaxAuditIssue>();

	public ObservableCollection<Gstr3bWorksheetRow> Gstr3bRows { get; } = new ObservableCollection<Gstr3bWorksheetRow>();

	public ObservableCollection<Gstr2bReconRow> Gstr2bReconRows { get; } = new ObservableCollection<Gstr2bReconRow>();

	public ObservableCollection<Gstr1B2bRow> Gstr1B2bRows { get; } = new ObservableCollection<Gstr1B2bRow>();

	private string _unverifiedStateNotice = string.Empty;

	public string UnverifiedStateNotice
	{
		get => _unverifiedStateNotice;
		private set
		{
			if (SetProperty(ref _unverifiedStateNotice, value))
			{
				OnPropertyChanged(nameof(HasUnverifiedStateNotice));
			}
		}
	}

	public bool HasUnverifiedStateNotice => !string.IsNullOrEmpty(UnverifiedStateNotice);

	public ObservableCollection<Gstr1B2cSlabRow> Gstr1B2cRows { get; } = new ObservableCollection<Gstr1B2cSlabRow>();

	public ObservableCollection<Gstr1HsnRow> Gstr1HsnRows { get; } = new ObservableCollection<Gstr1HsnRow>();

	public string TotalTaxableText => MoneyFormat.Rupees(TotalTaxableTurnover);

	public string OutputCgstText => MoneyFormat.Rupees(OutputCgst);

	public string OutputSgstText => MoneyFormat.Rupees(OutputSgst);

	public string EligibleItcText => MoneyFormat.Rupees(EligibleItc);

	// A negative net means input credit exceeds output tax: it carries forward, it is not owed.
	public string NetGstLabel => NetGstPayable < 0m ? "ITC carry-forward" : "Net GST payable";

	public string NetGstPayableText => MoneyFormat.Rupees(Math.Abs(NetGstPayable));

	/// <summary>The period's figures as one sum, the way the GSTR-3B offset works them out.</summary>
	public string CalculationSummary =>
		$"{MoneyFormat.Rupees(OutputCgst)} CGST + {MoneyFormat.Rupees(OutputSgst)} SGST + {MoneyFormat.Rupees(OutputIgst)} IGST − {MoneyFormat.Rupees(EligibleItc)} eligible ITC = {NetGstPayableText} {NetGstLabel.ToLowerInvariant()}";

	public string B2bEmptyHint => Gstr1B2bRows.Count == 0
		? "No B2B invoices this period. B2B lists only sales to buyers with a valid GSTIN; buyers without one are reported under B2C."
		: string.Empty;

	public string Gstr2bEmptyHint => Gstr2bReconRows.Count == 0
		? "Import the GSTR-2B CSV from the GST portal to reconcile purchase ITC against your books."
		: string.Empty;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public GstPeriodChoice? SelectedPeriod
	{
		get
		{
			return _selectedPeriod;
		}
		set
		{
			if (!EqualityComparer<GstPeriodChoice>.Default.Equals(_selectedPeriod, value))
			{
				OnPropertyChanging(nameof(SelectedPeriod));
				_selectedPeriod = value;
				OnSelectedPeriodChanged(value);
				OnPropertyChanged(nameof(SelectedPeriod));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string AuditStatusLabel
	{
		get
		{
			return _auditStatusLabel;
		}
		[MemberNotNull("_auditStatusLabel")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_auditStatusLabel, value))
			{
				OnPropertyChanging(nameof(AuditStatusLabel));
				_auditStatusLabel = value;
				OnPropertyChanged(nameof(AuditStatusLabel));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool IsAuditClean
	{
		get
		{
			return _isAuditClean;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_isAuditClean, value))
			{
				OnPropertyChanging(nameof(IsAuditClean));
				_isAuditClean = value;
				OnPropertyChanged(nameof(IsAuditClean));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public decimal TotalTaxableTurnover
	{
		get
		{
			return _totalTaxableTurnover;
		}
		set
		{
			if (!EqualityComparer<decimal>.Default.Equals(_totalTaxableTurnover, value))
			{
				OnPropertyChanging(nameof(TotalTaxableTurnover));
				_totalTaxableTurnover = value;
				OnTotalTaxableTurnoverChanged(value);
				OnPropertyChanged(nameof(TotalTaxableTurnover));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public decimal OutputCgst
	{
		get
		{
			return _outputCgst;
		}
		set
		{
			if (!EqualityComparer<decimal>.Default.Equals(_outputCgst, value))
			{
				OnPropertyChanging(nameof(OutputCgst));
				_outputCgst = value;
				OnOutputCgstChanged(value);
				OnPropertyChanged(nameof(OutputCgst));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public decimal OutputSgst
	{
		get
		{
			return _outputSgst;
		}
		set
		{
			if (!EqualityComparer<decimal>.Default.Equals(_outputSgst, value))
			{
				OnPropertyChanging(nameof(OutputSgst));
				_outputSgst = value;
				OnOutputSgstChanged(value);
				OnPropertyChanged(nameof(OutputSgst));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public decimal OutputIgst
	{
		get
		{
			return _outputIgst;
		}
		set
		{
			if (!EqualityComparer<decimal>.Default.Equals(_outputIgst, value))
			{
				OnPropertyChanging(nameof(OutputIgst));
				_outputIgst = value;
				OnPropertyChanged(nameof(OutputIgst));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public decimal EligibleItc
	{
		get
		{
			return _eligibleItc;
		}
		set
		{
			if (!EqualityComparer<decimal>.Default.Equals(_eligibleItc, value))
			{
				OnPropertyChanging(nameof(EligibleItc));
				_eligibleItc = value;
				OnEligibleItcChanged(value);
				OnPropertyChanged(nameof(EligibleItc));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public decimal NetGstPayable
	{
		get
		{
			return _netGstPayable;
		}
		set
		{
			if (!EqualityComparer<decimal>.Default.Equals(_netGstPayable, value))
			{
				OnPropertyChanging(nameof(NetGstPayable));
				_netGstPayable = value;
				OnNetGstPayableChanged(value);
				OnPropertyChanged(nameof(NetGstPayable));
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

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand RefreshCommand => refreshCommand ?? (refreshCommand = new AsyncRelayCommand(RefreshAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand ExportGstr1JsonCommand => exportGstr1JsonCommand ?? (exportGstr1JsonCommand = new AsyncRelayCommand(ExportGstr1JsonAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand ExportGstr3bWorksheetCommand => exportGstr3bWorksheetCommand ?? (exportGstr3bWorksheetCommand = new AsyncRelayCommand(ExportGstr3bWorksheetAsync));

	public IAsyncRelayCommand ExportGstr1ExcelCommand => exportGstr1ExcelCommand ?? (exportGstr1ExcelCommand = new AsyncRelayCommand(ExportGstr1ExcelAsync));

	public IAsyncRelayCommand ImportGstr2bCommand => importGstr2bCommand ?? (importGstr2bCommand = new AsyncRelayCommand(ImportGstr2bAsync));

	public IAsyncRelayCommand ExportEInvoiceCommand => exportEInvoiceCommand ?? (exportEInvoiceCommand = new AsyncRelayCommand(ExportEInvoiceAsync));

	public IAsyncRelayCommand ExportEWayBillCommand => exportEWayBillCommand ?? (exportEWayBillCommand = new AsyncRelayCommand(ExportEWayBillAsync));

	public GstReturnsPageViewModel(IServiceScopeFactory scopeFactory, IFilePickerService filePicker, CurrentSession? session = null)
	{
		_scopeFactory = scopeFactory;
		_filePicker = filePicker;
		_session = session;
		RebuildPeriodChoices();
		SelectedPeriod = PeriodChoices.FirstOrDefault((GstPeriodChoice choice) => choice.Key.StartsWith("M-", StringComparison.Ordinal)) ?? PeriodChoices[0];
	}

	public async Task LoadAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		await RefreshB2clThresholdNoticeAsync(cancellationToken);
		await RefreshAsync(cancellationToken);
	}

	private readonly CurrentSession? _session;

	private AsyncRelayCommand? saveProductTaxFixesCommand;

	private string _productTaxStatus = string.Empty;

	/// <summary>Active products that cannot be billed until their HSN code or GST rate is entered. Not limited to the selected period.</summary>
	public ObservableCollection<ProductTaxFixRow> ProductsMissingTax { get; } = new ObservableCollection<ProductTaxFixRow>();

	public string ProductsMissingTaxHeading => ProductsMissingTax.Count == 0
		? "Products missing HSN or GST rate: none"
		: $"Products missing HSN or GST rate: {ProductsMissingTax.Count}";

	public bool HasProductsMissingTax => ProductsMissingTax.Count > 0;

	public string ProductTaxStatus
	{
		get => _productTaxStatus;
		private set => SetProperty(ref _productTaxStatus, value);
	}

	public IAsyncRelayCommand SaveProductTaxFixesCommand => saveProductTaxFixesCommand ??= new AsyncRelayCommand(SaveProductTaxFixesAsync);

	private async Task RefreshProductsMissingTaxAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		using IServiceScope scope = _scopeFactory.CreateScope();
		IReadOnlyList<ProductMissingTaxRow> rows = await scope.ServiceProvider.GetRequiredService<ProductTaxService>().GetProductsMissingTaxAsync(cancellationToken);
		ProductsMissingTax.Clear();
		foreach (ProductMissingTaxRow row in rows)
		{
			ProductsMissingTax.Add(new ProductTaxFixRow(row));
		}

		OnPropertyChanged(nameof(ProductsMissingTaxHeading));
		OnPropertyChanged(nameof(HasProductsMissingTax));
	}

	/// <summary>Saves every edited row that is valid; rows with a problem keep their values and show what is wrong.</summary>
	private async Task SaveProductTaxFixesAsync()
	{
		List<ProductTaxFixRow> edited = ProductsMissingTax.Where(row => row.IsEdited).ToList();
		if (edited.Count == 0)
		{
			ProductTaxStatus = "Type an HSN code and GST rate (or tick Nil-rated) on the rows to fix, then save.";
			return;
		}

		List<ProductTaxUpdate> updates = new List<ProductTaxUpdate>();
		foreach (ProductTaxFixRow row in edited)
		{
			ProductTaxUpdate? update = row.TryBuildUpdate();
			if (update != null)
			{
				updates.Add(update);
			}
		}

		int invalid = edited.Count - updates.Count;
		try
		{
			if (updates.Count > 0)
			{
				using IServiceScope scope = _scopeFactory.CreateScope();
				await scope.ServiceProvider.GetRequiredService<ProductTaxService>().SetManyAsync(updates, _session?.User?.Id, "GST & e-Way bulk fix");
			}

			Dictionary<Guid, (string Hsn, string Rate, bool Nil, string Error)> keep = edited
				.Where(row => row.RowError.Length > 0)
				.ToDictionary(row => row.DrugId, row => (row.HsnCode, row.GstRateText, row.IsNilRated, row.RowError));
			await RefreshProductsMissingTaxAsync();
			foreach (ProductTaxFixRow row in ProductsMissingTax)
			{
				if (keep.TryGetValue(row.DrugId, out var kept))
				{
					row.HsnCode = kept.Hsn;
					row.IsNilRated = kept.Nil;
					row.GstRateText = kept.Rate;
					row.RowError = kept.Error;
				}
			}

			ProductTaxStatus = $"Saved {updates.Count} product(s)."
				+ (invalid > 0 ? $" {invalid} row(s) need correcting; see the message on each." : string.Empty)
				+ $" {ProductsMissingTax.Count} still missing.";
		}
		catch (Exception ex)
		{
			ProductTaxStatus = PharmaBill.Core.UserMessage.Of(ex);
		}
	}

	private string _b2clThresholdNotice = string.Empty;

	private string _b2clNoticeKey = string.Empty;

	private IRelayCommand? dismissB2clThresholdNoticeCommand;

	/// <summary>One-time reminder that the B2CL threshold changes what is filed and must be confirmed with an accountant.</summary>
	public string B2clThresholdNotice
	{
		get => _b2clThresholdNotice;
		private set
		{
			if (SetProperty(ref _b2clThresholdNotice, value))
			{
				OnPropertyChanged(nameof(HasB2clThresholdNotice));
			}
		}
	}

	public bool HasB2clThresholdNotice => !string.IsNullOrEmpty(B2clThresholdNotice);

	public IRelayCommand DismissB2clThresholdNoticeCommand => dismissB2clThresholdNoticeCommand ??= new RelayCommand(() =>
	{
		if (_b2clNoticeKey.Length > 0)
		{
			OneTimeNoticeStore.Default.Dismiss(_b2clNoticeKey);
		}
		B2clThresholdNotice = string.Empty;
	});

	private async Task RefreshB2clThresholdNoticeAsync(CancellationToken cancellationToken)
	{
		try
		{
			using IServiceScope scope = _scopeFactory.CreateScope();
			string store = scope.ServiceProvider.GetRequiredService<PharmaBill.Data.Persistence.ActiveStoreContext>().StoreSlug;
			_b2clNoticeKey = "b2cl-threshold-confirm:" + store;
			if (OneTimeNoticeStore.Default.IsDismissed(_b2clNoticeKey))
			{
				B2clThresholdNotice = string.Empty;
				return;
			}
			PharmaBill.Core.Entities.PharmacyProfile? profile = await scope.ServiceProvider.GetRequiredService<PharmaBill.Data.Persistence.PharmaBillDbContext>()
				.PharmacyProfiles.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
			B2clThresholdNotice = profile == null ? string.Empty : FormatB2clThresholdNotice(profile.B2cLargeInvoiceThreshold);
		}
		catch (Exception)
		{
			B2clThresholdNotice = string.Empty;
		}
	}

	internal static string FormatB2clThresholdNotice(decimal threshold) =>
		"B2CL threshold is set to ₹" + threshold.ToString("N0", CultureInfo.GetCultureInfo("en-IN")) + ". Confirm this with your accountant before filing.";

	private async Task RefreshAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		if ((object)SelectedPeriod == null)
		{
			return;
		}
		try
		{
			using IServiceScope scope = _scopeFactory.CreateScope();
			GstReturnExportService service = scope.ServiceProvider.GetRequiredService<GstReturnExportService>();
			DateOnly from = SelectedPeriod.From;
			DateOnly to = SelectedPeriod.To;
			GstReturnDashboardSummary gstReturnDashboardSummary = await service.GetDashboardSummaryAsync(from, to, cancellationToken);
			TotalTaxableTurnover = gstReturnDashboardSummary.TotalTaxableTurnover;
			OutputCgst = gstReturnDashboardSummary.OutputCgst;
			OutputSgst = gstReturnDashboardSummary.OutputSgst;
			OutputIgst = gstReturnDashboardSummary.OutputIgst;
			EligibleItc = gstReturnDashboardSummary.EligibleItcFromPurchases;
			NetGstPayable = gstReturnDashboardSummary.NetGstPayable;
			OnPropertyChanged(nameof(CalculationSummary));
			GstTaxAuditResult gstTaxAuditResult = await service.ValidatePeriodAsync(from, to, cancellationToken);
			AuditStatusLabel = gstTaxAuditResult.StatusLabel;
			IsAuditClean = gstTaxAuditResult.IsClean;
			AuditIssues.Clear();
			foreach (GstTaxAuditIssue issue in gstTaxAuditResult.Issues)
			{
				AuditIssues.Add(issue);
			}
			IReadOnlyList<Gstr3bWorksheetRow> readOnlyList = await service.GetGstr3bWorksheetAsync(from, to, cancellationToken);
			Gstr3bRows.Clear();
			foreach (Gstr3bWorksheetRow item in readOnlyList)
			{
				Gstr3bRows.Add(item);
			}

			GstService gst = scope.ServiceProvider.GetRequiredService<GstService>();
			Gstr1Tables tables = await gst.BuildGstr1TablesAsync(from, to, cancellationToken);
			Gstr1B2bRows.Clear();
			foreach (Gstr1B2bRow row in tables.Table4B2b)
			{
				Gstr1B2bRows.Add(row);
			}

			IReadOnlyList<string> unverified = tables.UnverifiedStateInvoiceNos ?? Array.Empty<string>();
			UnverifiedStateNotice = unverified.Count == 0
				? string.Empty
				: $"{unverified.Count} invoice(s)/bill(s) were saved before their tax details were recorded; GSTIN, place of supply and HSN are re-read from current master data and are UNVERIFIED: {string.Join(", ", unverified.Take(10))}{(unverified.Count > 10 ? ", …" : string.Empty)}. Check them before filing.";

			OnPropertyChanged(nameof(B2bEmptyHint));

			Gstr1B2cRows.Clear();
			foreach (Gstr1B2cSlabRow row in tables.Table7B2c)
			{
				Gstr1B2cRows.Add(row);
			}

			Gstr1HsnRows.Clear();
			foreach (Gstr1HsnRow row in tables.Table12Hsn)
			{
				Gstr1HsnRows.Add(row);
			}

			await RefreshProductsMissingTaxAsync(cancellationToken);
			ErrorMessage = string.Empty;
			StatusMessage = "GST returns loaded for " + SelectedPeriod.DisplayName + ".";
		}
		catch (Exception ex)
		{
			ErrorMessage = PharmaBill.Core.UserMessage.Of(ex);
		}
	}

	private async Task ExportGstr1JsonAsync()
	{
		if ((object)SelectedPeriod == null)
		{
			return;
		}
		try
		{
			using IServiceScope scope = _scopeFactory.CreateScope();
			GstReturnExportService service = scope.ServiceProvider.GetRequiredService<GstReturnExportService>();
			GstTaxAuditResult gstTaxAuditResult = await service.ValidatePeriodAsync(SelectedPeriod.From, SelectedPeriod.To);
			if (!gstTaxAuditResult.IsClean && gstTaxAuditResult.Issues.Any((GstTaxAuditIssue issue) => issue.Severity == GstTaxAuditSeverity.Error))
			{
				ErrorMessage = "Export blocked: fix Tax Audit errors (invalid GSTIN / missing HSN) before generating portal JSON.";
				AuditStatusLabel = gstTaxAuditResult.StatusLabel;
				AuditIssues.Clear();
				{
					foreach (GstTaxAuditIssue issue in gstTaxAuditResult.Issues)
					{
						AuditIssues.Add(issue);
					}
					return;
				}
			}
			string path = _filePicker.PickExportDestination("json", $"GSTR1-{SelectedPeriod.From:yyyyMM}-{SelectedPeriod.To:yyyyMM}");
			if (path == null)
			{
				return;
			}
			await File.WriteAllTextAsync(path, await service.ExportGstr1OfflineJsonAsync(SelectedPeriod.From, SelectedPeriod.To), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
			StatusMessage = "GSTR-1 portal JSON saved: " + path;
			ErrorMessage = string.Empty;
		}
		catch (Exception ex)
		{
			ErrorMessage = PharmaBill.Core.UserMessage.Of(ex);
		}
	}

	private async Task ExportGstr3bWorksheetAsync()
	{
		if ((object)SelectedPeriod == null)
		{
			return;
		}
		try
		{
			using IServiceScope scope = _scopeFactory.CreateScope();
			IReadOnlyList<Gstr3bWorksheetRow> rows = await scope.ServiceProvider.GetRequiredService<GstReturnExportService>().GetGstr3bWorksheetAsync(SelectedPeriod.From, SelectedPeriod.To);
			string path = _filePicker.PickExportDestination("xlsx", $"GSTR3B-worksheet-{SelectedPeriod.From:yyyyMM}");
			if (path == null)
			{
				return;
			}
			await Task.Run(() =>
			{
				using XLWorkbook xLWorkbook = new XLWorkbook();
				IXLWorksheet iXLWorksheet = xLWorkbook.Worksheets.Add("GSTR-3B");
				iXLWorksheet.Cell(1, 1).Value = "GSTR-3B worksheet (reference — verify with your CA before filing)";
				iXLWorksheet.Cell(1, 1).Style.Font.Bold = true;
				iXLWorksheet.Range(1, 1, 1, 6).Merge();
				iXLWorksheet.Cell(2, 1).Value = "Period: " + SelectedPeriod.DisplayName;
				string[] array = new string[7] { "Section", "Description", "Taxable value", "IGST", "CGST", "SGST", "Cess" };
				for (int i = 0; i < array.Length; i++)
				{
					iXLWorksheet.Cell(4, i + 1).Value = array[i];
					iXLWorksheet.Cell(4, i + 1).Style.Font.Bold = true;
				}
				int num = 5;
				foreach (Gstr3bWorksheetRow item in rows)
				{
					iXLWorksheet.Cell(num, 1).Value = item.Section;
					iXLWorksheet.Cell(num, 2).Value = item.Description;
					iXLWorksheet.Cell(num, 3).Value = item.TaxableValue;
					iXLWorksheet.Cell(num, 4).Value = item.IntegratedTax;
					iXLWorksheet.Cell(num, 5).Value = item.CentralTax;
					iXLWorksheet.Cell(num, 6).Value = item.StateTax;
					iXLWorksheet.Cell(num, 7).Value = item.Cess;
					num++;
				}
				iXLWorksheet.Columns().AdjustToContents();
				xLWorkbook.SaveAs(path);
			});
			StatusMessage = "GSTR-3B worksheet saved: " + path;
			ErrorMessage = string.Empty;
			Gstr3bRows.Clear();
			foreach (Gstr3bWorksheetRow item2 in rows)
			{
				Gstr3bRows.Add(item2);
			}
		}
		catch (Exception ex)
		{
			ErrorMessage = PharmaBill.Core.UserMessage.Of(ex);
		}
	}

	private async Task ExportGstr1ExcelAsync()
	{
		if (SelectedPeriod is null)
		{
			return;
		}

		try
		{
			using IServiceScope scope = _scopeFactory.CreateScope();
			GstService gst = scope.ServiceProvider.GetRequiredService<GstService>();
			Gstr1Tables tables = await gst.BuildGstr1TablesAsync(SelectedPeriod.From, SelectedPeriod.To);
			string path = _filePicker.PickExportDestination("xlsx", $"GSTR1-{SelectedPeriod.From:yyyyMM}");
			if (path is null)
			{
				return;
			}

			await Task.Run(() =>
			{
				using XLWorkbook workbook = new XLWorkbook();
				IXLWorksheet b2b = workbook.Worksheets.Add("Table4-B2B");
				string[] b2bHeaders = ["GSTIN", "Invoice", "Date", "Taxable", "CGST", "SGST", "IGST", "Invoice value", "POS", "POS status"];
				for (int i = 0; i < b2bHeaders.Length; i++)
				{
					b2b.Cell(1, i + 1).Value = b2bHeaders[i];
					b2b.Cell(1, i + 1).Style.Font.Bold = true;
				}

				int row = 2;
				foreach (Gstr1B2bRow item in tables.Table4B2b)
				{
					b2b.Cell(row, 1).Value = item.Gstin;
					b2b.Cell(row, 2).Value = item.InvoiceNo;
					b2b.Cell(row, 3).Value = item.InvoiceDate.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture);
					b2b.Cell(row, 4).Value = item.TaxableValue;
					b2b.Cell(row, 5).Value = item.Cgst;
					b2b.Cell(row, 6).Value = item.Sgst;
					b2b.Cell(row, 7).Value = item.Igst;
					b2b.Cell(row, 8).Value = item.InvoiceValue;
					b2b.Cell(row, 9).Value = item.PlaceOfSupply;
					b2b.Cell(row, 10).Value = item.PlaceOfSupplyStatus;
					row++;
				}

				IXLWorksheet b2c = workbook.Worksheets.Add("Table7-B2C");
				string[] b2cHeaders = ["Tax slab %", "Taxable", "CGST", "SGST", "IGST", "Invoices"];
				for (int i = 0; i < b2cHeaders.Length; i++)
				{
					b2c.Cell(1, i + 1).Value = b2cHeaders[i];
					b2c.Cell(1, i + 1).Style.Font.Bold = true;
				}

				row = 2;
				foreach (Gstr1B2cSlabRow item in tables.Table7B2c)
				{
					b2c.Cell(row, 1).Value = item.TaxRate;
					b2c.Cell(row, 2).Value = item.TaxableValue;
					b2c.Cell(row, 3).Value = item.Cgst;
					b2c.Cell(row, 4).Value = item.Sgst;
					b2c.Cell(row, 5).Value = item.Igst;
					b2c.Cell(row, 6).Value = item.InvoiceCount;
					row++;
				}

				IXLWorksheet hsn = workbook.Worksheets.Add("Table12-HSN");
				string[] hsnHeaders = ["HSN", "Qty", "Taxable", "CGST", "SGST", "IGST", "Rate %"];
				for (int i = 0; i < hsnHeaders.Length; i++)
				{
					hsn.Cell(1, i + 1).Value = hsnHeaders[i];
					hsn.Cell(1, i + 1).Style.Font.Bold = true;
				}

				row = 2;
				foreach (Gstr1HsnRow item in tables.Table12Hsn)
				{
					hsn.Cell(row, 1).Value = item.HsnCode;
					hsn.Cell(row, 2).Value = item.Quantity;
					hsn.Cell(row, 3).Value = item.TaxableValue;
					hsn.Cell(row, 4).Value = item.Cgst;
					hsn.Cell(row, 5).Value = item.Sgst;
					hsn.Cell(row, 6).Value = item.Igst;
					hsn.Cell(row, 7).Value = item.TaxRate;
					row++;
				}

				b2b.Columns().AdjustToContents();
				b2c.Columns().AdjustToContents();
				hsn.Columns().AdjustToContents();
				workbook.SaveAs(path);
			});
			StatusMessage = "GSTR-1 Excel saved: " + path;
			ErrorMessage = string.Empty;
		}
		catch (Exception ex)
		{
			ErrorMessage = PharmaBill.Core.UserMessage.Of(ex);
		}
	}

	private async Task ImportGstr2bAsync()
	{
		if (SelectedPeriod is null)
		{
			return;
		}

		try
		{
			string? path = _filePicker.PickCsvFile();
			if (string.IsNullOrWhiteSpace(path))
			{
				return;
			}

			string csv = await File.ReadAllTextAsync(path);
			IReadOnlyList<Gstr2bImportedRow> portal = GstService.ParseGstr2bCsv(csv);
			using IServiceScope scope = _scopeFactory.CreateScope();
			IReadOnlyList<Gstr2bReconRow> recon = await scope.ServiceProvider.GetRequiredService<GstService>()
				.ReconcileGstr2bAsync(SelectedPeriod.From, SelectedPeriod.To, portal);
			Gstr2bReconRows.Clear();
			foreach (Gstr2bReconRow row in recon)
			{
				Gstr2bReconRows.Add(row);
			}

			OnPropertyChanged(nameof(Gstr2bEmptyHint));
			StatusMessage = $"GSTR-2B recon: {recon.Count} row(s) — matched {recon.Count(r => r.MatchStatus == "Matched")}.";
			ErrorMessage = string.Empty;
		}
		catch (Exception ex)
		{
			ErrorMessage = PharmaBill.Core.UserMessage.Of(ex);
		}
	}

	private async Task ExportEInvoiceAsync()
	{
		try
		{
			using IServiceScope scope = _scopeFactory.CreateScope();
			PharmaBill.Data.Persistence.PharmaBillDbContext db = scope.ServiceProvider.GetRequiredService<PharmaBill.Data.Persistence.PharmaBillDbContext>();
			Guid? invoiceId = await db.WholesaleInvoices.AsNoTracking()
				.Where(i => i.Status == "Posted")
				.OrderByDescending(i => i.InvoiceAtUtc)
				.Select(i => (Guid?)i.Id)
				.FirstOrDefaultAsync();
			if (invoiceId is null)
			{
				ErrorMessage = "No posted wholesale invoice available for e-Invoice JSON.";
				return;
			}

			EInvoicePayloadResult payload = await scope.ServiceProvider.GetRequiredService<GstService>()
				.BuildEInvoiceJsonAsync(invoiceId.Value);
			string path = _filePicker.PickExportDestination("json", Path.GetFileNameWithoutExtension(payload.SuggestedFileName));
			if (path is null)
			{
				return;
			}

			await File.WriteAllTextAsync(path, payload.Json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
			StatusMessage = "e-Invoice JSON (schema v1.03) saved: " + path + (payload.StateWarning == null ? string.Empty : " WARNING: " + payload.StateWarning);
			ErrorMessage = string.Empty;
		}
		catch (Exception ex)
		{
			ErrorMessage = PharmaBill.Core.UserMessage.Of(ex);
		}
	}

	private async Task ExportEWayBillAsync()
	{
		try
		{
			using IServiceScope scope = _scopeFactory.CreateScope();
			PharmaBill.Data.Persistence.PharmaBillDbContext db = scope.ServiceProvider.GetRequiredService<PharmaBill.Data.Persistence.PharmaBillDbContext>();
			var invoice = await db.WholesaleInvoices.AsNoTracking()
				.Where(i => i.Status == "Posted")
				.OrderByDescending(i => i.InvoiceAtUtc)
				.Select(i => new { i.Id, i.VehicleNumber })
				.FirstOrDefaultAsync();
			if (invoice is null)
			{
				ErrorMessage = "No posted wholesale invoice available for e-Way Bill JSON.";
				return;
			}

			EWayBillPayloadResult payload = await scope.ServiceProvider.GetRequiredService<GstService>()
				.BuildEWayBillJsonAsync(invoice.Id, transporterId: null, vehicleNumber: invoice.VehicleNumber, distanceKm: 1m);
			string path = _filePicker.PickExportDestination("json", Path.GetFileNameWithoutExtension(payload.SuggestedFileName));
			if (path is null)
			{
				return;
			}

			await File.WriteAllTextAsync(path, payload.Json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
			StatusMessage = "e-Way Bill Part-A/B JSON saved: " + path + (payload.StateWarning == null ? string.Empty : " WARNING: " + payload.StateWarning);
			ErrorMessage = string.Empty;
		}
		catch (Exception ex)
		{
			ErrorMessage = PharmaBill.Core.UserMessage.Of(ex);
		}
	}

	private void RebuildPeriodChoices()
	{
		PeriodChoices.Clear();
		DateTime today = BusinessClock.Now.Date;
		for (int i = 0; i < 18; i++)
		{
			DateTime dateTime = today.AddMonths(-i);
			DateOnly dateOnly = new DateOnly(dateTime.Year, dateTime.Month, 1);
			DateOnly dateOnly2 = dateOnly.AddMonths(1).AddDays(-1);
			if (dateOnly2 > DateOnly.FromDateTime(today))
			{
				dateOnly2 = DateOnly.FromDateTime(today);
			}
			PeriodChoices.Add(new GstPeriodChoice($"M-{dateOnly:yyyyMM}", $"{CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(dateOnly.Month)} {dateOnly.Year}", dateOnly, dateOnly2));
		}
		PeriodChoices.Add(BuildFinancialQuarter(today, 1));
		PeriodChoices.Add(BuildFinancialQuarter(today, 2));
		PeriodChoices.Add(BuildFinancialQuarter(today, 3));
		PeriodChoices.Add(BuildFinancialQuarter(today, 4));
		PeriodChoices.Add(BuildFinancialQuarter(today.AddYears(-1), 1));
		PeriodChoices.Add(BuildFinancialQuarter(today.AddYears(-1), 2));
		PeriodChoices.Add(BuildFinancialQuarter(today.AddYears(-1), 3));
		PeriodChoices.Add(BuildFinancialQuarter(today.AddYears(-1), 4));
	}

	private static GstPeriodChoice BuildFinancialQuarter(DateTime reference, int quarter)
	{
		int num = ((reference.Month >= 4) ? reference.Year : (reference.Year - 1));
		DateOnly dateOnly;
		DateOnly dateOnly2;
		switch (quarter)
		{
		case 1:
			dateOnly = new DateOnly(num, 4, 1);
			dateOnly2 = new DateOnly(num, 6, 30);
			break;
		case 2:
			dateOnly = new DateOnly(num, 7, 1);
			dateOnly2 = new DateOnly(num, 9, 30);
			break;
		case 3:
			dateOnly = new DateOnly(num, 10, 1);
			dateOnly2 = new DateOnly(num, 12, 31);
			break;
		default:
			dateOnly = new DateOnly(num + 1, 1, 1);
			dateOnly2 = new DateOnly(num + 1, 3, 31);
			break;
		}
		DateOnly dateOnly3 = BusinessClock.Today;
		if (dateOnly2 > dateOnly3)
		{
			dateOnly2 = dateOnly3;
		}
		return new GstPeriodChoice($"Q{quarter}-FY{num % 100:00}{(num + 1) % 100:00}", $"Q{quarter} FY {num}-{(num + 1) % 100:00}", dateOnly, dateOnly2);
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnSelectedPeriodChanged(GstPeriodChoice? value)
	{
		if ((object)value != null)
		{
			RefreshAsync();
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnTotalTaxableTurnoverChanged(decimal value)
	{
		OnPropertyChanged("TotalTaxableText");
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnOutputCgstChanged(decimal value)
	{
		OnPropertyChanged("OutputCgstText");
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnOutputSgstChanged(decimal value)
	{
		OnPropertyChanged("OutputSgstText");
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnEligibleItcChanged(decimal value)
	{
		OnPropertyChanged("EligibleItcText");
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnNetGstPayableChanged(decimal value)
	{
		OnPropertyChanged("NetGstPayableText");
		OnPropertyChanged(nameof(NetGstLabel));
	}
}
