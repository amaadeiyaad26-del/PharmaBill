using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PharmaBill.App.Services;
using PharmaBill.Core.Entities;
using PharmaBill.Core.Security;
using PharmaBill.Data.Persistence;
using PharmaBill.Data.Services;

namespace PharmaBill.App.ViewModels;

public sealed class AddStockViewModel : ObservableObject
{
	private static readonly string[] ExpiryFormats =
	[
		"MM/yyyy", "M/yyyy", "MM-yyyy", "M-yyyy", "MM/yy", "M/yy",
		"yyyy-MM", "yyyy/MM", "yyyy-MM-dd", "yyyy/MM/dd"
	];

	private readonly IServiceScopeFactory? _scopeFactory;

	private readonly CurrentSession? _session;

	private readonly IConfirmationService _confirmation;

	private readonly IPromptService? _prompt;

	private bool _isManualMedicine;

	private string _medicineName = string.Empty;

	private string _composition = string.Empty;

	private string _manufacturer = string.Empty;

	private string? _schedule;

	private string _scheduleHint = string.Empty;

	private string _batchNo = string.Empty;

	private string _expiryText = string.Empty;

	private string _mrpText = string.Empty;

	private string _purchaseRateText = string.Empty;

	private string _quantityText = string.Empty;

	private string _freeQuantityText = string.Empty;

	private string _packSizeText = string.Empty;

	private string _gstText = string.Empty;

	private Supplier? _selectedSupplier;

	private string _supplierInvoiceNo = string.Empty;

	private DateTime? _invoiceDate;

	private string _rack = string.Empty;

	private string _medicineError = string.Empty;

	private string _scheduleError = string.Empty;

	private string _batchNoError = string.Empty;

	private string _expiryError = string.Empty;

	private string _mrpError = string.Empty;

	private string _purchaseRateError = string.Empty;

	private string _quantityError = string.Empty;

	private string _freeQuantityError = string.Empty;

	private string _packSizeError = string.Empty;

	private string _gstError = string.Empty;

	private string _supplierError = string.Empty;

	private string _supplierInvoiceNoError = string.Empty;

	private string _invoiceDateError = string.Empty;

	private string _errorMessage = string.Empty;

	private bool _isReadOnlyMode;

	private string _firstInvalidField = string.Empty;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? addSupplierCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? saveCommand;

	public IReadOnlyList<string> ScheduleOptions { get; } = AddStockService.Schedules;

	public ObservableCollection<Supplier> Suppliers { get; } = new ObservableCollection<Supplier>();

	public Guid? DrugId { get; private set; }

	public Guid? CatalogMedicineId { get; private set; }

	public bool IsMedicineReadOnly => !IsManualMedicine;

	public string Title
	{
		get
		{
			if (!IsManualMedicine)
			{
				return "Add stock";
			}
			return "Add new medicine and stock";
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool IsManualMedicine
	{
		get
		{
			return _isManualMedicine;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_isManualMedicine, value))
			{
				OnPropertyChanging(nameof(IsManualMedicine));
				_isManualMedicine = value;
				OnIsManualMedicineChanged(value);
				OnPropertyChanged(nameof(IsManualMedicine));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string MedicineName
	{
		get
		{
			return _medicineName;
		}
		[MemberNotNull("_medicineName")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_medicineName, value))
			{
				OnPropertyChanging(nameof(MedicineName));
				_medicineName = value;
				OnPropertyChanged(nameof(MedicineName));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string Composition
	{
		get
		{
			return _composition;
		}
		[MemberNotNull("_composition")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_composition, value))
			{
				OnPropertyChanging(nameof(Composition));
				_composition = value;
				OnPropertyChanged(nameof(Composition));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string Manufacturer
	{
		get
		{
			return _manufacturer;
		}
		[MemberNotNull("_manufacturer")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_manufacturer, value))
			{
				OnPropertyChanging(nameof(Manufacturer));
				_manufacturer = value;
				OnPropertyChanged(nameof(Manufacturer));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string? Schedule
	{
		get
		{
			return _schedule;
		}
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_schedule, value))
			{
				OnPropertyChanging(nameof(Schedule));
				_schedule = value;
				OnPropertyChanged(nameof(Schedule));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string ScheduleHint
	{
		get
		{
			return _scheduleHint;
		}
		[MemberNotNull("_scheduleHint")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_scheduleHint, value))
			{
				OnPropertyChanging(nameof(ScheduleHint));
				_scheduleHint = value;
				OnPropertyChanged(nameof(ScheduleHint));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string BatchNo
	{
		get
		{
			return _batchNo;
		}
		[MemberNotNull("_batchNo")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_batchNo, value))
			{
				OnPropertyChanging(nameof(BatchNo));
				_batchNo = value;
				OnPropertyChanged(nameof(BatchNo));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string ExpiryText
	{
		get
		{
			return _expiryText;
		}
		[MemberNotNull("_expiryText")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_expiryText, value))
			{
				OnPropertyChanging(nameof(ExpiryText));
				_expiryText = value;
				OnPropertyChanged(nameof(ExpiryText));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string MrpText
	{
		get
		{
			return _mrpText;
		}
		[MemberNotNull("_mrpText")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_mrpText, value))
			{
				OnPropertyChanging(nameof(MrpText));
				_mrpText = value;
				OnPropertyChanged(nameof(MrpText));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string PurchaseRateText
	{
		get
		{
			return _purchaseRateText;
		}
		[MemberNotNull("_purchaseRateText")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_purchaseRateText, value))
			{
				OnPropertyChanging(nameof(PurchaseRateText));
				_purchaseRateText = value;
				OnPropertyChanged(nameof(PurchaseRateText));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string QuantityText
	{
		get
		{
			return _quantityText;
		}
		[MemberNotNull("_quantityText")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_quantityText, value))
			{
				OnPropertyChanging(nameof(QuantityText));
				_quantityText = value;
				OnPropertyChanged(nameof(QuantityText));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string FreeQuantityText
	{
		get
		{
			return _freeQuantityText;
		}
		[MemberNotNull("_freeQuantityText")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_freeQuantityText, value))
			{
				OnPropertyChanging(nameof(FreeQuantityText));
				_freeQuantityText = value;
				OnPropertyChanged(nameof(FreeQuantityText));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string PackSizeText
	{
		get
		{
			return _packSizeText;
		}
		[MemberNotNull("_packSizeText")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_packSizeText, value))
			{
				OnPropertyChanging(nameof(PackSizeText));
				_packSizeText = value;
				OnPropertyChanged(nameof(PackSizeText));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string GstText
	{
		get
		{
			return _gstText;
		}
		[MemberNotNull("_gstText")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_gstText, value))
			{
				OnPropertyChanging(nameof(GstText));
				_gstText = value;
				OnPropertyChanged(nameof(GstText));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public Supplier? SelectedSupplier
	{
		get
		{
			return _selectedSupplier;
		}
		set
		{
			if (!EqualityComparer<Supplier>.Default.Equals(_selectedSupplier, value))
			{
				OnPropertyChanging(nameof(SelectedSupplier));
				_selectedSupplier = value;
				OnPropertyChanged(nameof(SelectedSupplier));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string SupplierInvoiceNo
	{
		get
		{
			return _supplierInvoiceNo;
		}
		[MemberNotNull("_supplierInvoiceNo")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_supplierInvoiceNo, value))
			{
				OnPropertyChanging(nameof(SupplierInvoiceNo));
				_supplierInvoiceNo = value;
				OnPropertyChanged(nameof(SupplierInvoiceNo));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public DateTime? InvoiceDate
	{
		get
		{
			return _invoiceDate;
		}
		set
		{
			if (!EqualityComparer<DateTime?>.Default.Equals(_invoiceDate, value))
			{
				OnPropertyChanging(nameof(InvoiceDate));
				_invoiceDate = value;
				OnPropertyChanged(nameof(InvoiceDate));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string Rack
	{
		get
		{
			return _rack;
		}
		[MemberNotNull("_rack")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_rack, value))
			{
				OnPropertyChanging(nameof(Rack));
				_rack = value;
				OnPropertyChanged(nameof(Rack));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string MedicineError
	{
		get
		{
			return _medicineError;
		}
		[MemberNotNull("_medicineError")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_medicineError, value))
			{
				OnPropertyChanging(nameof(MedicineError));
				_medicineError = value;
				OnPropertyChanged(nameof(MedicineError));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string ScheduleError
	{
		get
		{
			return _scheduleError;
		}
		[MemberNotNull("_scheduleError")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_scheduleError, value))
			{
				OnPropertyChanging(nameof(ScheduleError));
				_scheduleError = value;
				OnPropertyChanged(nameof(ScheduleError));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string BatchNoError
	{
		get
		{
			return _batchNoError;
		}
		[MemberNotNull("_batchNoError")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_batchNoError, value))
			{
				OnPropertyChanging(nameof(BatchNoError));
				_batchNoError = value;
				OnPropertyChanged(nameof(BatchNoError));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string ExpiryError
	{
		get
		{
			return _expiryError;
		}
		[MemberNotNull("_expiryError")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_expiryError, value))
			{
				OnPropertyChanging(nameof(ExpiryError));
				_expiryError = value;
				OnPropertyChanged(nameof(ExpiryError));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string MrpError
	{
		get
		{
			return _mrpError;
		}
		[MemberNotNull("_mrpError")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_mrpError, value))
			{
				OnPropertyChanging(nameof(MrpError));
				_mrpError = value;
				OnPropertyChanged(nameof(MrpError));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string PurchaseRateError
	{
		get
		{
			return _purchaseRateError;
		}
		[MemberNotNull("_purchaseRateError")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_purchaseRateError, value))
			{
				OnPropertyChanging(nameof(PurchaseRateError));
				_purchaseRateError = value;
				OnPropertyChanged(nameof(PurchaseRateError));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string QuantityError
	{
		get
		{
			return _quantityError;
		}
		[MemberNotNull("_quantityError")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_quantityError, value))
			{
				OnPropertyChanging(nameof(QuantityError));
				_quantityError = value;
				OnPropertyChanged(nameof(QuantityError));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string FreeQuantityError
	{
		get
		{
			return _freeQuantityError;
		}
		[MemberNotNull("_freeQuantityError")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_freeQuantityError, value))
			{
				OnPropertyChanging(nameof(FreeQuantityError));
				_freeQuantityError = value;
				OnPropertyChanged(nameof(FreeQuantityError));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string PackSizeError
	{
		get
		{
			return _packSizeError;
		}
		[MemberNotNull("_packSizeError")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_packSizeError, value))
			{
				OnPropertyChanging(nameof(PackSizeError));
				_packSizeError = value;
				OnPropertyChanged(nameof(PackSizeError));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string GstError
	{
		get
		{
			return _gstError;
		}
		[MemberNotNull("_gstError")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_gstError, value))
			{
				OnPropertyChanging(nameof(GstError));
				_gstError = value;
				OnPropertyChanged(nameof(GstError));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string SupplierError
	{
		get
		{
			return _supplierError;
		}
		[MemberNotNull("_supplierError")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_supplierError, value))
			{
				OnPropertyChanging(nameof(SupplierError));
				_supplierError = value;
				OnPropertyChanged(nameof(SupplierError));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string SupplierInvoiceNoError
	{
		get
		{
			return _supplierInvoiceNoError;
		}
		[MemberNotNull("_supplierInvoiceNoError")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_supplierInvoiceNoError, value))
			{
				OnPropertyChanging(nameof(SupplierInvoiceNoError));
				_supplierInvoiceNoError = value;
				OnPropertyChanged(nameof(SupplierInvoiceNoError));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string InvoiceDateError
	{
		get
		{
			return _invoiceDateError;
		}
		[MemberNotNull("_invoiceDateError")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_invoiceDateError, value))
			{
				OnPropertyChanging(nameof(InvoiceDateError));
				_invoiceDateError = value;
				OnPropertyChanged(nameof(InvoiceDateError));
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
	public bool IsReadOnlyMode
	{
		get
		{
			return _isReadOnlyMode;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_isReadOnlyMode, value))
			{
				OnPropertyChanging(nameof(IsReadOnlyMode));
				_isReadOnlyMode = value;
				OnPropertyChanged(nameof(IsReadOnlyMode));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string FirstInvalidField
	{
		get
		{
			return _firstInvalidField;
		}
		[MemberNotNull("_firstInvalidField")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_firstInvalidField, value))
			{
				OnPropertyChanging(nameof(FirstInvalidField));
				_firstInvalidField = value;
				OnPropertyChanged(nameof(FirstInvalidField));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand AddSupplierCommand => addSupplierCommand ?? (addSupplierCommand = new AsyncRelayCommand(AddSupplierAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand SaveCommand => saveCommand ?? (saveCommand = new AsyncRelayCommand(SaveAsync));

	public event EventHandler<AddStockResult>? Saved;

	public AddStockViewModel(IServiceScopeFactory? scopeFactory, CurrentSession? session, IConfirmationService confirmation, IPromptService? prompt)
	{
		_scopeFactory = scopeFactory;
		_session = session;
		_confirmation = confirmation;
		_prompt = prompt;
	}

	public async Task InitializeAsync(AddStockRequest request)
	{
		DrugId = request.DrugId;
		CatalogMedicineId = request.CatalogMedicineId;
		IsManualMedicine = string.IsNullOrWhiteSpace(request.MedicineName);
		MedicineName = request.MedicineName;
		Composition = request.Composition ?? string.Empty;
		Manufacturer = request.Manufacturer ?? string.Empty;
		OnPropertyChanged("Title");
		if (_scopeFactory == null)
		{
			return;
		}
		using IServiceScope scope = _scopeFactory.CreateScope();
		foreach (Supplier item in await (from item in scope.ServiceProvider.GetRequiredService<PharmaBillDbContext>().Suppliers
			where item.IsActive
			orderby item.Name
			select item).ToListAsync())
		{
			Suppliers.Add(item);
		}
		IsReadOnlyMode = await scope.ServiceProvider.GetRequiredService<IEntitlementService>().IsReadOnlyAsync();
		if (IsReadOnlyMode)
		{
			ErrorMessage = "Read-only mode: stock entry is blocked until the required drug licence is renewed.";
		}
		if (!IsManualMedicine)
		{
			AddStockService service = scope.ServiceProvider.GetRequiredService<AddStockService>();
			string text = await service.SuggestScheduleAsync(DrugId, CatalogMedicineId, MedicineName);
			ScheduleHint = ((text == null) ? "Choose the schedule from the pack label" : ("Suggested: " + text + ". Please choose to confirm."));
			decimal? num = await service.SuggestMrpAsync(DrugId, CatalogMedicineId);
			if (num.HasValue && string.IsNullOrWhiteSpace(MrpText))
			{
				MrpText = num.Value.ToString("0.00", CultureInfo.CurrentCulture);
			}
		}
	}

	private async Task AddSupplierAsync()
	{
		if (_scopeFactory == null || _session?.User == null || _prompt == null)
		{
			return;
		}
		string text = _prompt.AskText("Add new supplier", "Enter the supplier name.", "Supplier name");
		if (text == null)
		{
			return;
		}
		try
		{
			using IServiceScope scope = _scopeFactory.CreateScope();
			Supplier supplier = await scope.ServiceProvider.GetRequiredService<AddStockService>().AddSupplierAsync(text, _session.User.Id, _session.User.Role);
			Supplier supplier2 = Suppliers.FirstOrDefault((Supplier item) => item.Id == supplier.Id);
			if (supplier2 == null)
			{
				Suppliers.Add(supplier);
				supplier2 = supplier;
			}
			SelectedSupplier = supplier2;
			SupplierError = string.Empty;
		}
		catch (Exception ex)
		{
			ErrorMessage = ex.Message;
		}
	}

	public bool Validate()
	{
		MedicineError = ((IsManualMedicine && string.IsNullOrWhiteSpace(MedicineName)) ? "Medicine name is required" : string.Empty);
		ScheduleError = (string.IsNullOrWhiteSpace(Schedule) ? "Choose the schedule" : string.Empty);
		BatchNoError = (string.IsNullOrWhiteSpace(BatchNo) ? "Batch no. is required" : string.Empty);
		ExpiryError = string.Empty;
		DateOnly lastDay;
		if (string.IsNullOrWhiteSpace(ExpiryText))
		{
			ExpiryError = "Expiry (month/year) is required";
		}
		else if (!TryParseExpiry(ExpiryText, out lastDay))
		{
			ExpiryError = "Use month/year, for example 08/2027";
		}
		else if (lastDay < DateOnly.FromDateTime(DateTime.Today))
		{
			ExpiryError = "This date has expired; expired stock cannot be added";
		}
		MrpError = RequiredAmount(MrpText, "MRP per pack", allowZero: true);
		PurchaseRateError = RequiredAmount(PurchaseRateText, "Purchase rate", allowZero: true);
		QuantityError = RequiredAmount(QuantityText, "Quantity (packs)", allowZero: false);
		FreeQuantityError = OptionalAmount(FreeQuantityText, "Free quantity");
		PackSizeError = OptionalAmount(PackSizeText, "Pack size");
		GstError = OptionalAmount(GstText, "GST %");
		if (GstError.Length == 0 && TryAmount(GstText, out var value) && value > 100m)
		{
			GstError = "GST % cannot be above 100";
		}
		SupplierError = ((SelectedSupplier == null) ? "Supplier is required" : string.Empty);
		SupplierInvoiceNoError = (string.IsNullOrWhiteSpace(SupplierInvoiceNo) ? "Supplier invoice no. is required" : string.Empty);
		InvoiceDateError = ((!InvoiceDate.HasValue) ? "Invoice date is required" : string.Empty);
		FirstInvalidField = string.Empty;
		(string, string)[] source = new (string, string)[13]
		{
			("Medicine", MedicineError),
			("Schedule", ScheduleError),
			("BatchNo", BatchNoError),
			("Expiry", ExpiryError),
			("Mrp", MrpError),
			("PurchaseRate", PurchaseRateError),
			("Quantity", QuantityError),
			("FreeQuantity", FreeQuantityError),
			("PackSize", PackSizeError),
			("Gst", GstError),
			("Supplier", SupplierError),
			("SupplierInvoiceNo", SupplierInvoiceNoError),
			("InvoiceDate", InvoiceDateError)
		};
		FirstInvalidField = source.FirstOrDefault(((string Name, string Error) item) => item.Error.Length > 0).Item1 ?? string.Empty;
		return FirstInvalidField.Length == 0;
	}

	public bool ConfirmWarnings()
	{
		TryParseExpiry(ExpiryText, out var lastDay);
		if (lastDay > DateOnly.FromDateTime(DateTime.Today).AddYears(5) && !_confirmation.Confirm($"This expiry ({lastDay:MM/yyyy}) is more than 5 years away. Is it correct?", "Check expiry date"))
		{
			return false;
		}
		TryAmount(MrpText, out var value);
		TryAmount(PurchaseRateText, out var value2);
		if (!(value >= value2))
		{
			return _confirmation.Confirm($"MRP ({value:N2}) is lower than the purchase rate ({value2:N2}). Save anyway?", "Check prices");
		}
		return true;
	}

	private async Task SaveAsync()
	{
		ErrorMessage = string.Empty;
		if (_scopeFactory == null || _session?.User == null)
		{
			ErrorMessage = "Sign in again to add stock.";
		}
		else if (IsReadOnlyMode)
		{
			ErrorMessage = "Read-only mode: stock entry is blocked until the required drug licence is renewed.";
		}
		else
		{
			if (!Validate() || !ConfirmWarnings())
			{
				return;
			}
			try
			{
				AddStockInput input = BuildInput();
				using IServiceScope scope = _scopeFactory.CreateScope();
				AddStockService service = scope.ServiceProvider.GetRequiredService<AddStockService>();
				if (await service.BatchExistsAsync(input.DrugId, input.CatalogMedicineId, input.SupplierId, input.BatchNo, input.ExpiryDate) && !_confirmation.Confirm("Batch " + input.BatchNo + " already exists for this supplier and expiry. Add this quantity to the existing batch? Choose No to cancel.", "Batch already exists"))
				{
					return;
				}
				AddStockResult e = await service.AddStockAsync(input, _session.User.Id, _session.User.Role);
				Saved?.Invoke(this, e);
			}
			catch (Exception ex)
			{
				ErrorMessage = ex.Message;
			}
		}
	}

	public AddStockInput BuildInput()
	{
		TryParseExpiry(ExpiryText, out var lastDay);
		TryAmount(MrpText, out var value);
		TryAmount(PurchaseRateText, out var value2);
		TryAmount(QuantityText, out var value3);
		TryAmount(FreeQuantityText, out var value4);
		TryAmount(PackSizeText, out var value5);
		TryAmount(GstText, out var value6);
		return new AddStockInput(DrugId, CatalogMedicineId, MedicineName.Trim(), string.IsNullOrWhiteSpace(Composition) ? null : Composition.Trim(), string.IsNullOrWhiteSpace(Manufacturer) ? null : Manufacturer.Trim(), Schedule, BatchNo.Trim(), lastDay, value, value2, value3, value4, value5, value6, SelectedSupplier.Id, SupplierInvoiceNo.Trim(), DateOnly.FromDateTime(InvoiceDate.Value), string.IsNullOrWhiteSpace(Rack) ? null : Rack.Trim());
	}

	public static bool TryParseExpiry(string text, out DateOnly lastDay)
	{
		lastDay = default;
		if (!DateTime.TryParseExact(text.Trim(), ExpiryFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var result))
		{
			return false;
		}
		lastDay = new DateOnly(result.Year, result.Month, DateTime.DaysInMonth(result.Year, result.Month));
		return true;
	}

	private static bool TryAmount(string text, out decimal value)
	{
		value = 0m;
		if (!string.IsNullOrWhiteSpace(text))
		{
			return decimal.TryParse(text.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out value);
		}
		return true;
	}

	private static string RequiredAmount(string text, string label, bool allowZero)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return label + " is required";
		}
		if (!TryAmount(text, out var value) || value < 0m || (!allowZero && value == 0m))
		{
			if (!allowZero)
			{
				return label + " must be more than 0";
			}
			return label + " must be a number, 0 or more";
		}
		return string.Empty;
	}

	private static string OptionalAmount(string text, string label)
	{
		if (!string.IsNullOrWhiteSpace(text) && (!TryAmount(text, out var value) || !(value >= 0m)))
		{
			return label + " must be a number, 0 or more";
		}
		return string.Empty;
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnIsManualMedicineChanged(bool value)
	{
		OnPropertyChanged("IsMedicineReadOnly");
	}
}
