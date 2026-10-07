using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PharmaBill.App.Services;
using PharmaBill.Core.Entities;
using PharmaBill.Data.Persistence;

namespace PharmaBill.App.ViewModels;

public class OutputDataPageViewModel : SectionPageViewModel, ILoadablePage
{
	private readonly string _sectionKey;

	private readonly IServiceScopeFactory _scopeFactory;

	private readonly IFilePickerService _filePickerService;

	private readonly TabularExportService _exportService;

	private string[] _columns = Array.Empty<string>();

	private IReadOnlyList<string[]> _rows = Array.Empty<string[]>();

	private DataView? _table;

	private string _statusMessage = string.Empty;

	private string _errorMessage = string.Empty;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? exportPdfCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? exportExcelCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? exportCsvCommand;

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
				_table = value;
				OnPropertyChanged(nameof(Table));
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
	public IAsyncRelayCommand ExportPdfCommand => exportPdfCommand ?? (exportPdfCommand = new AsyncRelayCommand(ExportPdfAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand ExportExcelCommand => exportExcelCommand ?? (exportExcelCommand = new AsyncRelayCommand(ExportExcelAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand ExportCsvCommand => exportCsvCommand ?? (exportCsvCommand = new AsyncRelayCommand(ExportCsvAsync));

	public OutputDataPageViewModel(string sectionKey, string title, IServiceScopeFactory scopeFactory, IFilePickerService filePickerService, TabularExportService exportService)
		: base(title)
	{
		_sectionKey = sectionKey;
		_scopeFactory = scopeFactory;
		_filePickerService = filePickerService;
		_exportService = exportService;
	}

	public async Task LoadAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		_ = 8;
		try
		{
			using IServiceScope scope = _scopeFactory.CreateScope();
			PharmaBillDbContext context = scope.ServiceProvider.GetRequiredService<PharmaBillDbContext>();
			if (_sectionKey == "Registers")
			{
				List<ScheduleRegisterEntry> entries = await (from item in context.ScheduleRegisterEntries.AsNoTracking()
					orderby item.EntryAtUtc descending
					select item).Take(5000).ToListAsync(cancellationToken);
				List<ExpiryWriteOff> writeOffs = await (from item in context.ExpiryWriteOffs.AsNoTracking()
					orderby item.WrittenOffAtUtc descending
					select item).Take(5000).ToListAsync(cancellationToken);
				Guid[] drugIds = (from item in entries
					where item.DrugId.HasValue
					select item.DrugId.Value).Concat(writeOffs.Select((ExpiryWriteOff item) => item.DrugId)).Distinct().ToArray();
				Dictionary<Guid, string> drugNames = await (from item in context.Drugs.AsNoTracking()
					where drugIds.Contains(item.Id)
					select item).ToDictionaryAsync((Drug item) => item.Id, (Drug item) => item.Name, cancellationToken);
				Guid[] batchIds = writeOffs.Select((ExpiryWriteOff item) => item.BatchId).Distinct().ToArray();
				Dictionary<Guid, string> batchNumbers = await (from item in context.Batches.AsNoTracking()
					where batchIds.Contains(item.Id)
					select item).ToDictionaryAsync((Batch item) => item.Id, (Batch item) => item.BatchNo, cancellationToken);
				_columns = new string[9] { "Entry date", "Register", "Medicine", "Batch", "Quantity", "Patient", "Prescriber", "Registration no.", "Notes / reason" };
				_rows = (from item in (from item in entries.Select((ScheduleRegisterEntry item) => (At: item.EntryAtUtc, Values: new string[9]
						{
							item.EntryAtUtc.ToLocalTime().ToString("dd-MMM-yyyy HH:mm"),
							item.RegisterType,
							item.DrugId.HasValue ? drugNames.GetValueOrDefault(item.DrugId.Value, string.Empty) : string.Empty,
							item.BatchNo ?? string.Empty,
							item.Quantity?.ToString("0.##") ?? string.Empty,
							item.PatientName ?? string.Empty,
							item.PrescriberName ?? string.Empty,
							item.PrescriberRegistrationNumber ?? string.Empty,
							item.Notes ?? string.Empty
						})).Concat(writeOffs.Select((ExpiryWriteOff item) => (At: item.WrittenOffAtUtc, Values: new string[9]
						{
							item.WrittenOffAtUtc.ToLocalTime().ToString("dd-MMM-yyyy HH:mm"),
							"Expiry dump",
							drugNames.GetValueOrDefault(item.DrugId, string.Empty),
							batchNumbers.GetValueOrDefault(item.BatchId, string.Empty),
							item.Quantity.ToString("0.##"),
							string.Empty,
							string.Empty,
							string.Empty,
							item.Reason
						})))
						orderby item.At descending
						select item).Take(5000)
					select item.Values).ToArray();
			}
			else
			{
				BusinessMode businessMode = await (from item in context.PharmacyProfiles.AsNoTracking()
					select item.BusinessMode).SingleAsync(cancellationToken);
				List<Sale> list = ((businessMode != BusinessMode.Wholesaler) ? (await (from item in context.Sales.AsNoTracking()
					orderby item.SaleAtUtc descending
					select item).Take(5000).ToListAsync(cancellationToken)) : new List<Sale>());
				List<Sale> sales = list;
				List<WholesaleInvoice> wholesaleInvoices = await (from item in context.WholesaleInvoices.AsNoTracking()
					orderby item.InvoiceAtUtc descending
					select item).Take(5000).ToListAsync(cancellationToken);
				Guid[] patientIds = (from item in sales
					where item.PatientId.HasValue
					select item.PatientId.Value).Distinct().ToArray();
				Dictionary<Guid, string> patients = await (from item in context.Patients.AsNoTracking()
					where patientIds.Contains(item.Id)
					select item).ToDictionaryAsync((Patient item) => item.Id, (Patient item) => item.Name, cancellationToken);
				Guid[] customerIds = wholesaleInvoices.Select((WholesaleInvoice item) => item.CustomerId).Distinct().ToArray();
				Dictionary<Guid, string> customers = await (from item in context.Customers.AsNoTracking()
					where customerIds.Contains(item.Id)
					select item).ToDictionaryAsync((Customer item) => item.Id, (Customer item) => item.Name, cancellationToken);
				_columns = ((businessMode != BusinessMode.Wholesaler) ? new string[9] { "Date", "Invoice no.", "Patient / customer", "Subtotal", "GST", "Discount", "Total", "Paid", "Status" } : new string[9] { "Date", "Invoice no.", "Customer", "Subtotal", "GST", "Discount", "Total", "Paid", "Status" });
				_rows = (from item in (from item in sales.Select((Sale item) => (At: item.SaleAtUtc, Values: new string[9]
						{
							item.SaleAtUtc.ToLocalTime().ToString("dd-MMM-yyyy HH:mm"),
							item.InvoiceNo,
							item.PatientId.HasValue ? patients.GetValueOrDefault(item.PatientId.Value, string.Empty) : string.Empty,
							item.Subtotal.ToString("N2"),
							item.TaxAmount.ToString("N2"),
							item.DiscountAmount.ToString("N2"),
							item.TotalAmount.ToString("N2"),
							item.PaidAmount.ToString("N2"),
							item.PaymentStatus ?? string.Empty
						})).Concat(wholesaleInvoices.Select((WholesaleInvoice item) => (At: item.InvoiceAtUtc, Values: new string[9]
						{
							item.InvoiceAtUtc.ToLocalTime().ToString("dd-MMM-yyyy HH:mm"),
							item.InvoiceNo,
							customers.GetValueOrDefault(item.CustomerId, string.Empty),
							item.Subtotal.ToString("N2"),
							item.TaxAmount.ToString("N2"),
							item.DiscountAmount.ToString("N2"),
							item.TotalAmount.ToString("N2"),
							item.PaidAmount.ToString("N2"),
							item.PaymentStatus ?? string.Empty
						})))
						orderby item.At descending
						select item).Take(5000)
					select item.Values).ToArray();
			}
			BuildTable();
			StatusMessage = $"{_rows.Count} row(s) loaded. Showing at most 5,000 records.";
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

	private Task ExportCsvAsync()
	{
		return ExportAsync("csv");
	}

	private async Task ExportAsync(string extension)
	{
		string path = _filePickerService.PickExportDestination(extension, _sectionKey.ToLowerInvariant());
		if (path == null)
		{
			return;
		}
		try
		{
			await _exportService.ExportAsync(path, Title, _columns, _rows, CancellationToken.None);
			StatusMessage = $"Exported {_rows.Count} row(s) to {path}.";
			ErrorMessage = string.Empty;
		}
		catch (Exception ex)
		{
			ErrorMessage = ex.Message;
		}
	}

	private void BuildTable()
	{
		DataTable dataTable = new DataTable();
		string[] columns = _columns;
		foreach (string columnName in columns)
		{
			dataTable.Columns.Add(columnName);
		}
		foreach (string[] row in _rows)
		{
			dataTable.Rows.Add(row.Cast<object>().ToArray());
		}
		Table = dataTable.DefaultView;
	}
}
