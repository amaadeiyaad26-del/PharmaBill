using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
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
using PharmaBill.Data.Services;

namespace PharmaBill.App.ViewModels;

public sealed class PurchasePageViewModel(IServiceScopeFactory scopeFactory, CurrentSession currentSession, IFilePickerService filePicker, IConfirmationService confirmationService, PurchaseSpreadsheetReader spreadsheetReader, IPromptService promptService) : ObservableObject, ILoadablePage
{
	private Supplier? _selectedSupplier;

	private StorageLocation? _selectedStorageLocation;

	private string _invoiceNo = string.Empty;

	private DateTime _invoiceDate = DateTime.Today;

	private string _errorMessage = string.Empty;

	private string _statusMessage = string.Empty;

	private BatchReturnOption? _selectedReturnBatch;

	private string _returnNo = string.Empty;

	private decimal _returnQuantity;

	private string _returnReason = string.Empty;

	private string _remarks = string.Empty;

	private string _supplierError = string.Empty;

	private string _invoiceNoError = string.Empty;

	private string _invoiceDateError = string.Empty;

	private bool _hasSpreadsheet;

	private DataView? _spreadsheetPreview;

	private bool _isImporting;

	private string _importStatus = string.Empty;

	private string _billTotalCheck = string.Empty;

	private bool _billTotalMatches;

	private bool _isAddItemOpen;

	private Drug? _newItemDrug;

	private string _newItemBatch = string.Empty;

	private string _newItemExpiry = string.Empty;

	private string _newItemQuantity = string.Empty;

	private string _newItemFree = string.Empty;

	private string _newItemMrp = string.Empty;

	private string _newItemRate = string.Empty;

	private string _newItemGst = string.Empty;

	private string _newItemRack = string.Empty;

	private string _newItemError = string.Empty;

	private decimal? _importedExpectedGrandTotal;

	private PurchaseSpreadsheetData? _spreadsheet;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? addSupplierCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? addLineCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? cancelAddItemCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? confirmAddItemCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand<PurchaseLineDraft?>? deleteLineCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? cancelSpreadsheetCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? importDocumentCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? loadSpreadsheetCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? applySpreadsheetMappingCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? saveCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? savePurchaseReturnCommand;

	public ObservableCollection<Supplier> Suppliers { get; } = new ObservableCollection<Supplier>();

	public ObservableCollection<StorageLocation> StorageLocations { get; } = new ObservableCollection<StorageLocation>();

	public ObservableCollection<Drug> Drugs { get; } = new ObservableCollection<Drug>();

	public ObservableCollection<BatchReturnOption> ReturnBatches { get; } = new ObservableCollection<BatchReturnOption>();

	public ObservableCollection<PurchaseReturnLineDraft> ReturnLines { get; } = new ObservableCollection<PurchaseReturnLineDraft>();

	public ObservableCollection<PurchaseLineDraft> Lines { get; } = new ObservableCollection<PurchaseLineDraft>();

	public ObservableCollection<string> SpreadsheetHeaders { get; } = new ObservableCollection<string> { string.Empty };

	public ObservableCollection<SpreadsheetColumnMapping> SpreadsheetMappings { get; } = new ObservableCollection<SpreadsheetColumnMapping>
	{
		new SpreadsheetColumnMapping("Medicine", isRequired: true, new ObservableCollection<string>()),
		new SpreadsheetColumnMapping("Batch", isRequired: true, new ObservableCollection<string>()),
		new SpreadsheetColumnMapping("Expiry", isRequired: true, new ObservableCollection<string>()),
		new SpreadsheetColumnMapping("Quantity", isRequired: true, new ObservableCollection<string>()),
		new SpreadsheetColumnMapping("Free quantity", isRequired: false, new ObservableCollection<string>()),
		new SpreadsheetColumnMapping("MRP", isRequired: true, new ObservableCollection<string>()),
		new SpreadsheetColumnMapping("Rate", isRequired: true, new ObservableCollection<string>()),
		new SpreadsheetColumnMapping("GST", isRequired: false, new ObservableCollection<string>()),
		new SpreadsheetColumnMapping("Discount", isRequired: false, new ObservableCollection<string>()),
		new SpreadsheetColumnMapping("Amount", isRequired: true, new ObservableCollection<string>())
	};

	public decimal Subtotal => Lines.Sum((PurchaseLineDraft line) => line.BaseAmount);

	public decimal TaxTotal => Lines.Sum((PurchaseLineDraft line) => line.TaxAmount);

	public decimal GrandTotal => Subtotal + TaxTotal;

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
	public StorageLocation? SelectedStorageLocation
	{
		get
		{
			return _selectedStorageLocation;
		}
		set
		{
			if (!EqualityComparer<StorageLocation>.Default.Equals(_selectedStorageLocation, value))
			{
				OnPropertyChanging(nameof(SelectedStorageLocation));
				_selectedStorageLocation = value;
				OnPropertyChanged(nameof(SelectedStorageLocation));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string InvoiceNo
	{
		get
		{
			return _invoiceNo;
		}
		[MemberNotNull("_invoiceNo")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_invoiceNo, value))
			{
				OnPropertyChanging(nameof(InvoiceNo));
				_invoiceNo = value;
				OnPropertyChanged(nameof(InvoiceNo));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public DateTime InvoiceDate
	{
		get
		{
			return _invoiceDate;
		}
		set
		{
			if (!EqualityComparer<DateTime>.Default.Equals(_invoiceDate, value))
			{
				OnPropertyChanging(nameof(InvoiceDate));
				_invoiceDate = value;
				OnPropertyChanged(nameof(InvoiceDate));
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
	public BatchReturnOption? SelectedReturnBatch
	{
		get
		{
			return _selectedReturnBatch;
		}
		set
		{
			if (!EqualityComparer<BatchReturnOption>.Default.Equals(_selectedReturnBatch, value))
			{
				OnPropertyChanging(nameof(SelectedReturnBatch));
				_selectedReturnBatch = value;
				OnPropertyChanged(nameof(SelectedReturnBatch));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string ReturnNo
	{
		get
		{
			return _returnNo;
		}
		[MemberNotNull("_returnNo")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_returnNo, value))
			{
				OnPropertyChanging(nameof(ReturnNo));
				_returnNo = value;
				OnPropertyChanged(nameof(ReturnNo));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public decimal ReturnQuantity
	{
		get
		{
			return _returnQuantity;
		}
		set
		{
			if (!EqualityComparer<decimal>.Default.Equals(_returnQuantity, value))
			{
				OnPropertyChanging(nameof(ReturnQuantity));
				_returnQuantity = value;
				OnPropertyChanged(nameof(ReturnQuantity));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string ReturnReason
	{
		get
		{
			return _returnReason;
		}
		[MemberNotNull("_returnReason")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_returnReason, value))
			{
				OnPropertyChanging(nameof(ReturnReason));
				_returnReason = value;
				OnPropertyChanged(nameof(ReturnReason));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string Remarks
	{
		get
		{
			return _remarks;
		}
		[MemberNotNull("_remarks")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_remarks, value))
			{
				OnPropertyChanging(nameof(Remarks));
				_remarks = value;
				OnPropertyChanged(nameof(Remarks));
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
	public string InvoiceNoError
	{
		get
		{
			return _invoiceNoError;
		}
		[MemberNotNull("_invoiceNoError")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_invoiceNoError, value))
			{
				OnPropertyChanging(nameof(InvoiceNoError));
				_invoiceNoError = value;
				OnPropertyChanged(nameof(InvoiceNoError));
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
	public bool HasSpreadsheet
	{
		get
		{
			return _hasSpreadsheet;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_hasSpreadsheet, value))
			{
				OnPropertyChanging(nameof(HasSpreadsheet));
				_hasSpreadsheet = value;
				OnPropertyChanged(nameof(HasSpreadsheet));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public DataView? SpreadsheetPreview
	{
		get
		{
			return _spreadsheetPreview;
		}
		set
		{
			if (!EqualityComparer<DataView>.Default.Equals(_spreadsheetPreview, value))
			{
				OnPropertyChanging(nameof(SpreadsheetPreview));
				_spreadsheetPreview = value;
				OnPropertyChanged(nameof(SpreadsheetPreview));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool IsImporting
	{
		get
		{
			return _isImporting;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_isImporting, value))
			{
				OnPropertyChanging(nameof(IsImporting));
				_isImporting = value;
				OnPropertyChanged(nameof(IsImporting));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string ImportStatus
	{
		get
		{
			return _importStatus;
		}
		[MemberNotNull("_importStatus")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_importStatus, value))
			{
				OnPropertyChanging(nameof(ImportStatus));
				_importStatus = value;
				OnPropertyChanged(nameof(ImportStatus));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string BillTotalCheck
	{
		get
		{
			return _billTotalCheck;
		}
		[MemberNotNull("_billTotalCheck")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_billTotalCheck, value))
			{
				OnPropertyChanging(nameof(BillTotalCheck));
				_billTotalCheck = value;
				OnPropertyChanged(nameof(BillTotalCheck));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool BillTotalMatches
	{
		get
		{
			return _billTotalMatches;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_billTotalMatches, value))
			{
				OnPropertyChanging(nameof(BillTotalMatches));
				_billTotalMatches = value;
				OnPropertyChanged(nameof(BillTotalMatches));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool IsAddItemOpen
	{
		get
		{
			return _isAddItemOpen;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_isAddItemOpen, value))
			{
				OnPropertyChanging(nameof(IsAddItemOpen));
				_isAddItemOpen = value;
				OnPropertyChanged(nameof(IsAddItemOpen));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public Drug? NewItemDrug
	{
		get
		{
			return _newItemDrug;
		}
		set
		{
			if (!EqualityComparer<Drug>.Default.Equals(_newItemDrug, value))
			{
				OnPropertyChanging(nameof(NewItemDrug));
				_newItemDrug = value;
				OnPropertyChanged(nameof(NewItemDrug));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string NewItemBatch
	{
		get
		{
			return _newItemBatch;
		}
		[MemberNotNull("_newItemBatch")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_newItemBatch, value))
			{
				OnPropertyChanging(nameof(NewItemBatch));
				_newItemBatch = value;
				OnPropertyChanged(nameof(NewItemBatch));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string NewItemExpiry
	{
		get
		{
			return _newItemExpiry;
		}
		[MemberNotNull("_newItemExpiry")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_newItemExpiry, value))
			{
				OnPropertyChanging(nameof(NewItemExpiry));
				_newItemExpiry = value;
				OnPropertyChanged(nameof(NewItemExpiry));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string NewItemQuantity
	{
		get
		{
			return _newItemQuantity;
		}
		[MemberNotNull("_newItemQuantity")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_newItemQuantity, value))
			{
				OnPropertyChanging(nameof(NewItemQuantity));
				_newItemQuantity = value;
				OnPropertyChanged(nameof(NewItemQuantity));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string NewItemFree
	{
		get
		{
			return _newItemFree;
		}
		[MemberNotNull("_newItemFree")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_newItemFree, value))
			{
				OnPropertyChanging(nameof(NewItemFree));
				_newItemFree = value;
				OnPropertyChanged(nameof(NewItemFree));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string NewItemMrp
	{
		get
		{
			return _newItemMrp;
		}
		[MemberNotNull("_newItemMrp")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_newItemMrp, value))
			{
				OnPropertyChanging(nameof(NewItemMrp));
				_newItemMrp = value;
				OnPropertyChanged(nameof(NewItemMrp));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string NewItemRate
	{
		get
		{
			return _newItemRate;
		}
		[MemberNotNull("_newItemRate")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_newItemRate, value))
			{
				OnPropertyChanging(nameof(NewItemRate));
				_newItemRate = value;
				OnPropertyChanged(nameof(NewItemRate));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string NewItemGst
	{
		get
		{
			return _newItemGst;
		}
		[MemberNotNull("_newItemGst")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_newItemGst, value))
			{
				OnPropertyChanging(nameof(NewItemGst));
				_newItemGst = value;
				OnPropertyChanged(nameof(NewItemGst));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string NewItemRack
	{
		get
		{
			return _newItemRack;
		}
		[MemberNotNull("_newItemRack")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_newItemRack, value))
			{
				OnPropertyChanging(nameof(NewItemRack));
				_newItemRack = value;
				OnPropertyChanged(nameof(NewItemRack));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string NewItemError
	{
		get
		{
			return _newItemError;
		}
		[MemberNotNull("_newItemError")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_newItemError, value))
			{
				OnPropertyChanging(nameof(NewItemError));
				_newItemError = value;
				OnPropertyChanged(nameof(NewItemError));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand AddSupplierCommand => addSupplierCommand ?? (addSupplierCommand = new AsyncRelayCommand(AddSupplierAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand AddLineCommand => addLineCommand ?? (addLineCommand = new RelayCommand(AddLine));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand CancelAddItemCommand => cancelAddItemCommand ?? (cancelAddItemCommand = new RelayCommand(CancelAddItem));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand ConfirmAddItemCommand => confirmAddItemCommand ?? (confirmAddItemCommand = new RelayCommand(ConfirmAddItem));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand<PurchaseLineDraft?> DeleteLineCommand => deleteLineCommand ?? (deleteLineCommand = new RelayCommand<PurchaseLineDraft>(DeleteLine));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand CancelSpreadsheetCommand => cancelSpreadsheetCommand ?? (cancelSpreadsheetCommand = new RelayCommand(CancelSpreadsheet));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand ImportDocumentCommand => importDocumentCommand ?? (importDocumentCommand = new AsyncRelayCommand(ImportDocumentAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand LoadSpreadsheetCommand => loadSpreadsheetCommand ?? (loadSpreadsheetCommand = new AsyncRelayCommand(LoadSpreadsheetAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand ApplySpreadsheetMappingCommand => applySpreadsheetMappingCommand ?? (applySpreadsheetMappingCommand = new RelayCommand(ApplySpreadsheetMapping));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand SaveCommand => saveCommand ?? (saveCommand = new AsyncRelayCommand(SaveAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand SavePurchaseReturnCommand => savePurchaseReturnCommand ?? (savePurchaseReturnCommand = new AsyncRelayCommand(SavePurchaseReturnAsync));

	public async Task LoadAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		using IServiceScope scope = scopeFactory.CreateScope();
		PharmaBillDbContext context = scope.ServiceProvider.GetRequiredService<PharmaBillDbContext>();
		Suppliers.Clear();
		foreach (Supplier item in await (from item in context.Suppliers
			where item.IsActive
			orderby item.Name
			select item).ToListAsync(cancellationToken))
		{
			Suppliers.Add(item);
		}
		StorageLocationService requiredService = scope.ServiceProvider.GetRequiredService<StorageLocationService>();
		StorageLocations.Clear();
		foreach (StorageLocation item2 in await requiredService.GetActiveLocationsAsync(cancellationToken))
		{
			StorageLocations.Add(item2);
		}
		if (SelectedStorageLocation == null)
		{
			SelectedStorageLocation = StorageLocations.FirstOrDefault((StorageLocation item) => item.IsDefaultRetailLocation) ?? StorageLocations.FirstOrDefault();
		}
		Drugs.Clear();
		foreach (Drug item3 in await (from item in context.Drugs
			where item.IsActive
			orderby item.Name
			select item).ToListAsync(cancellationToken))
		{
			Drugs.Add(item3);
		}
		Dictionary<Guid, string> supplierNames = Suppliers.ToDictionary((Supplier supplier) => supplier.Id, (Supplier supplier) => supplier.Name);
		Dictionary<Guid, string> drugNames = Drugs.ToDictionary((Drug drug) => drug.Id, (Drug drug) => drug.Name);
		ReturnBatches.Clear();
		foreach (Batch item4 in await (from item in context.Batches
			where item.SupplierId.HasValue && item.Quantity > 0m
			orderby item.BatchNo
			select item).ToListAsync(cancellationToken))
		{
			Guid? supplierId = item4.SupplierId;
			if (supplierId.HasValue)
			{
				Guid valueOrDefault = supplierId.GetValueOrDefault();
				if (supplierNames.TryGetValue(valueOrDefault, out var value) && drugNames.TryGetValue(item4.DrugId, out var value2))
				{
					ReturnBatches.Add(new BatchReturnOption(item4.Id, valueOrDefault, item4.BatchNo, $"{value2} / {item4.BatchNo} / {value} / Qty {item4.Quantity}"));
				}
			}
		}
	}

	public async Task LoadShortageIndentAsync(PurchaseIndentGroup group, CancellationToken cancellationToken = default(CancellationToken))
	{
		await LoadAsync(cancellationToken);
		PurchaseLineDraft[] array = Lines.ToArray();
		foreach (PurchaseLineDraft purchaseLineDraft in array)
		{
			purchaseLineDraft.PropertyChanged -= OnLineChanged;
			Lines.Remove(purchaseLineDraft);
		}
		Guid? supplierId = group.SupplierId;
		if (supplierId.HasValue)
		{
			Guid supplierId2 = supplierId.GetValueOrDefault();
			SelectedSupplier = Suppliers.FirstOrDefault((Supplier supplier) => supplier.Id == supplierId2);
		}
		InvoiceNo = (string.IsNullOrWhiteSpace(InvoiceNo) ? $"INDENT-{DateTime.Now:yyyyMMdd-HHmm}" : InvoiceNo);
		InvoiceDate = DateTime.Today;
		Remarks = $"Auto indent from shortage list · {group.SupplierName} · {DateTime.Now:dd-MMM-yyyy HH:mm}";
		foreach (PurchaseIndentItem item in group.Items)
		{
			Drug drug = Drugs.FirstOrDefault((Drug candidate) => candidate.Id == item.DrugId);
			if (drug != null)
			{
				PurchaseLineDraft line = new PurchaseLineDraft(drug)
				{
					BatchNo = string.Empty,
					Expiry = DateTime.Today.AddMonths(12).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
					Quantity = item.SuggestedOrderQty,
					FreeQuantity = 0m,
					Mrp = (item.LastMrp ?? drug.Mrp.GetValueOrDefault()),
					Rate = item.LastPurchaseRate.GetValueOrDefault(),
					GstRate = drug.GstRate.GetValueOrDefault(),
					Rack = null
				};
				AddDraftLine(line);
			}
		}
		NotifyTotals();
		ErrorMessage = string.Empty;
		StatusMessage = $"Loaded {Lines.Count} shortage line(s) as a draft purchase order for {group.SupplierName}. " + "Fill batch numbers and expiry from the supplier bill before saving.";
	}

	private async Task AddSupplierAsync()
	{
		if (currentSession.User == null)
		{
			ErrorMessage = "Sign in again to add a supplier.";
			return;
		}
		string text = promptService.AskText("Add new supplier", "Enter the supplier name.", "Supplier name");
		if (text == null)
		{
			return;
		}
		try
		{
			using IServiceScope scope = scopeFactory.CreateScope();
			Supplier supplier = await scope.ServiceProvider.GetRequiredService<AddStockService>().AddSupplierAsync(text, currentSession.User.Id, currentSession.User.Role);
			Supplier supplier2 = Suppliers.FirstOrDefault((Supplier item) => item.Id == supplier.Id);
			if (supplier2 == null)
			{
				Suppliers.Add(supplier);
				supplier2 = supplier;
			}
			SelectedSupplier = supplier2;
			ErrorMessage = string.Empty;
		}
		catch (Exception ex)
		{
			ErrorMessage = ex.Message;
		}
	}

	private void AddLine()
	{
		NewItemError = string.Empty;
		IsAddItemOpen = true;
	}

	private void CancelAddItem()
	{
		IsAddItemOpen = false;
		ClearNewItem();
	}

	private void ConfirmAddItem()
	{
		NewItemError = string.Empty;
		if (NewItemDrug == null)
		{
			NewItemError = "Medicine is required. To add a medicine that is not listed, use the search box above.";
			return;
		}
		if (string.IsNullOrWhiteSpace(NewItemBatch))
		{
			NewItemError = "Batch no. is required";
			return;
		}
		if (!AddStockViewModel.TryParseExpiry(NewItemExpiry, out var lastDay))
		{
			NewItemError = "Expiry (month/year) is required, for example 08/2027";
			return;
		}
		if (lastDay < DateOnly.FromDateTime(DateTime.Today))
		{
			NewItemError = "This date has expired; expired stock cannot be purchased";
			return;
		}
		if (!TryDecimal(NewItemQuantity, out var result) || result <= 0m)
		{
			NewItemError = "Quantity must be more than 0";
			return;
		}
		if (!TryDecimal(NewItemMrp, out var result2) || result2 < 0m || !TryDecimal(NewItemRate, out var result3) || result3 < 0m)
		{
			NewItemError = "MRP and Rate are required numbers";
			return;
		}
		decimal num = OptionalDecimal(NewItemFree);
		decimal num2 = OptionalDecimal(NewItemGst);
		if (num < 0m || num2 < 0m || num2 > 100m)
		{
			NewItemError = "Free quantity and GST % must be valid numbers";
			return;
		}
		PurchaseLineDraft line = new PurchaseLineDraft(NewItemDrug)
		{
			BatchNo = NewItemBatch.Trim(),
			Expiry = lastDay.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
			Quantity = result,
			FreeQuantity = num,
			Mrp = result2,
			Rate = result3,
			GstRate = num2,
			Rack = (string.IsNullOrWhiteSpace(NewItemRack) ? null : NewItemRack.Trim())
		};
		AddDraftLine(line);
		ClearNewItem();
		IsAddItemOpen = false;
	}

	private void DeleteLine(PurchaseLineDraft? line)
	{
		if (line != null)
		{
			line.PropertyChanged -= OnLineChanged;
			Lines.Remove(line);
			NotifyTotals();
		}
	}

	private void CancelSpreadsheet()
	{
		_spreadsheet = null;
		SpreadsheetPreview = null;
		HasSpreadsheet = false;
		StatusMessage = string.Empty;
	}

	private void AddDraftLine(PurchaseLineDraft line)
	{
		line.PropertyChanged += OnLineChanged;
		Lines.Add(line);
		NotifyTotals();
	}

	private void ClearNewItem()
	{
		NewItemDrug = null;
		NewItemBatch = string.Empty;
		NewItemExpiry = string.Empty;
		NewItemQuantity = string.Empty;
		NewItemFree = string.Empty;
		NewItemMrp = string.Empty;
		NewItemRate = string.Empty;
		NewItemGst = string.Empty;
		NewItemRack = string.Empty;
		NewItemError = string.Empty;
	}

	private void NotifyTotals()
	{
		OnPropertyChanged("Subtotal");
		OnPropertyChanged("TaxTotal");
		OnPropertyChanged("GrandTotal");
	}

	private async Task ImportDocumentAsync()
	{
		string text = filePicker.PickPurchaseDocument();
		if (text != null)
		{
			await ImportDocumentFromPathAsync(text);
		}
	}

	public async Task ImportDocumentFromPathAsync(string path)
	{
		if (!confirmationService.Confirm("The selected supplier document will be sent to Google Gemini for extraction. Review the returned rows before saving.", "Send purchase document to Gemini"))
		{
			return;
		}
		IsImporting = true;
		ImportStatus = "Reading the bill...";
		BillTotalCheck = string.Empty;
		try
		{
			await ImportCoreAsync(path);
			ImportStatus = ((ErrorMessage.Length > 0 && Lines.Count == 0 && ReturnLines.Count == 0) ? ErrorMessage : "Read by: Gemini");
		}
		finally
		{
			IsImporting = false;
		}
	}

	private async Task ImportCoreAsync(string path)
	{
		try
		{
			using IServiceScope scope = scopeFactory.CreateScope();
			ExtractedPurchaseInvoice extracted = await scope.ServiceProvider.GetRequiredService<GeminiPurchaseImportService>().ExtractAsync(path);
			if (extracted.DocumentType == "PURCHASE_RETURN")
			{
				_importedExpectedGrandTotal = null;
				if (ReturnLines.Count > 0 && !confirmationService.Confirm("Replace the purchase-return lines currently staged?", "Replace return lines"))
				{
					return;
				}
				SelectedSupplier = Suppliers.FirstOrDefault((Supplier supplier) => string.Equals(supplier.Name.Trim(), extracted.Supplier.Trim(), StringComparison.OrdinalIgnoreCase));
				ReturnNo = extracted.InvoiceNo;
				ReturnLines.Clear();
				foreach (ExtractedPurchaseLine extractedLine in extracted.Items)
				{
					BatchReturnOption batch = ReturnBatches.FirstOrDefault((BatchReturnOption option) =>
					{
						Guid supplierId = option.SupplierId;
						Guid? guid = SelectedSupplier?.Id;
						return supplierId == guid && string.Equals(option.BatchNo, extractedLine.Batch, StringComparison.OrdinalIgnoreCase);
					});
					ReturnLines.Add(new PurchaseReturnLineDraft(batch, extractedLine.Quantity));
				}
				ErrorMessage = ((SelectedSupplier == null || ReturnLines.Any((PurchaseReturnLineDraft line) => (object)line.Batch == null)) ? "Review the return rows, select the matching supplier and match every extracted batch to existing stock." : string.Empty);
				StatusMessage = $"Extracted {ReturnLines.Count} return rows. Review each batch and quantity, enter a reason, then post.";
				return;
			}
			if (extracted.DocumentType != "PURCHASE_INVOICE")
			{
				ErrorMessage = "The document type is not supported.";
				return;
			}
			if (Lines.Count > 0 && !confirmationService.Confirm("Replace the purchase lines currently in the review grid?", "Replace purchase lines"))
			{
				return;
			}
			Lines.Clear();
			SelectedSupplier = Suppliers.FirstOrDefault((Supplier supplier) => string.Equals(supplier.Name.Trim(), extracted.Supplier.Trim(), StringComparison.OrdinalIgnoreCase));
			InvoiceNo = extracted.InvoiceNo;
			if (DateOnly.TryParse(extracted.InvoiceDate, CultureInfo.InvariantCulture, out var result))
			{
				InvoiceDate = result.ToDateTime(TimeOnly.MinValue);
			}
			foreach (ExtractedPurchaseLine extractedLine2 in extracted.Items)
			{
				PurchaseLineDraft purchaseLineDraft = new PurchaseLineDraft(Drugs.FirstOrDefault((Drug drug) => string.Equals(drug.Name.Trim(), extractedLine2.ItemName.Trim(), StringComparison.OrdinalIgnoreCase) || string.Equals(drug.BrandName?.Trim(), extractedLine2.ItemName.Trim(), StringComparison.OrdinalIgnoreCase)))
				{
					BatchNo = extractedLine2.Batch,
					Expiry = (DateOnly.TryParse(extractedLine2.Expiry, CultureInfo.InvariantCulture, out var result2) ? result2.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : extractedLine2.Expiry),
					Quantity = extractedLine2.Quantity,
					FreeQuantity = extractedLine2.Free,
					Mrp = extractedLine2.Mrp,
					Rate = extractedLine2.Rate,
					GstRate = extractedLine2.Gst,
					Rack = null
				};
				purchaseLineDraft.PropertyChanged += OnLineChanged;
				Lines.Add(purchaseLineDraft);
			}
			OnPropertyChanged("Subtotal");
			OnPropertyChanged("TaxTotal");
			OnPropertyChanged("GrandTotal");
			ErrorMessage = ((SelectedSupplier == null) ? ("Review the extracted rows and select the matching supplier. Extracted supplier: " + extracted.Supplier + ".") : string.Empty);
			StatusMessage = $"Extracted {Lines.Count} rows. Confirm medicine matches, expiry dates, and totals before saving.";
			if (decimal.Round(GrandTotal, 2, MidpointRounding.AwayFromZero) != decimal.Round(extracted.GrandTotal, 2, MidpointRounding.AwayFromZero))
			{
				_importedExpectedGrandTotal = extracted.GrandTotal;
				BillTotalMatches = false;
				BillTotalCheck = $"Bill total {extracted.GrandTotal:F2} does not match the calculated total {GrandTotal:F2}. Check the rows.";
				ErrorMessage = $"Extracted bill total {extracted.GrandTotal:F2} differs from the calculated total {GrandTotal:F2}. Review and correct before saving.";
			}
			else
			{
				_importedExpectedGrandTotal = extracted.GrandTotal;
				BillTotalMatches = true;
				BillTotalCheck = "Bill totals match";
			}
		}
		catch (Exception ex)
		{
			ErrorMessage = ex.Message;
		}
	}

	private async Task LoadSpreadsheetAsync()
	{
		string text = filePicker.PickPurchaseSpreadsheet();
		if (text == null)
		{
			return;
		}
		try
		{
			_spreadsheet = await spreadsheetReader.ReadAsync(text);
			SpreadsheetHeaders.Clear();
			SpreadsheetHeaders.Add(string.Empty);
			foreach (string header in _spreadsheet.Headers)
			{
				SpreadsheetHeaders.Add(header);
			}
			foreach (SpreadsheetColumnMapping spreadsheetMapping in SpreadsheetMappings)
			{
				spreadsheetMapping.AvailableHeaders = SpreadsheetHeaders;
				spreadsheetMapping.SourceHeader = FindLikelyHeader(spreadsheetMapping.Target, _spreadsheet.Headers);
			}
			StatusMessage = $"Loaded {_spreadsheet.Rows.Count} rows. Map the columns and apply them to the review grid.";
			ErrorMessage = string.Empty;
			SpreadsheetPreview = BuildPreview(_spreadsheet);
			HasSpreadsheet = true;
		}
		catch (Exception ex)
		{
			ErrorMessage = ex.Message;
		}
	}

	private void ApplySpreadsheetMapping()
	{
		if ((object)_spreadsheet == null)
		{
			ErrorMessage = "Choose a CSV or XLSX file first.";
		}
		else
		{
			if (Lines.Count > 0 && !confirmationService.Confirm("Replace the purchase lines currently in the review grid?", "Replace purchase lines"))
			{
				return;
			}
			Dictionary<string, string> mappings = SpreadsheetMappings.ToDictionary((SpreadsheetColumnMapping mapping) => mapping.Target, (SpreadsheetColumnMapping mapping) => mapping.SourceHeader, StringComparer.Ordinal);
			if (SpreadsheetMappings.Any((SpreadsheetColumnMapping mapping) => mapping.IsRequired && string.IsNullOrWhiteSpace(mapping.SourceHeader)))
			{
				ErrorMessage = "Map every required purchase field before applying the spreadsheet.";
				return;
			}
			List<PurchaseLineDraft> list = new List<PurchaseLineDraft>();
			for (int num = 0; num < _spreadsheet.Rows.Count; num++)
			{
				IReadOnlyDictionary<string, string> row = _spreadsheet.Rows[num];
				string itemName = Read(row, mappings, "Medicine");
				if (!TryDecimal(Read(row, mappings, "Quantity"), out var result) || !TryDecimal(Read(row, mappings, "MRP"), out var result2) || !TryDecimal(Read(row, mappings, "Rate"), out var result3) || !TryDecimal(Read(row, mappings, "Amount"), out var result4))
				{
					ErrorMessage = $"Row {num + 2} has an invalid quantity, MRP, rate or amount.";
					return;
				}
				decimal num2 = OptionalDecimal(Read(row, mappings, "Free quantity"));
				decimal num3 = OptionalDecimal(Read(row, mappings, "GST"));
				decimal num4 = OptionalDecimal(Read(row, mappings, "Discount"));
				if (num2 < 0m || num3 < 0m || num4 < 0m)
				{
					ErrorMessage = $"Row {num + 2} has an invalid free quantity, GST or discount.";
					return;
				}
				if (decimal.Round(result * result3 - num4, 2, MidpointRounding.AwayFromZero) != decimal.Round(result4, 2, MidpointRounding.AwayFromZero))
				{
					ErrorMessage = $"Row {num + 2}: quantity × rate less discount does not equal amount.";
					return;
				}
				string text = Read(row, mappings, "Expiry");
				PurchaseLineDraft purchaseLineDraft = new PurchaseLineDraft(Drugs.FirstOrDefault((Drug item) => string.Equals(item.Name.Trim(), itemName.Trim(), StringComparison.OrdinalIgnoreCase) || string.Equals(item.BrandName?.Trim(), itemName.Trim(), StringComparison.OrdinalIgnoreCase)))
				{
					BatchNo = Read(row, mappings, "Batch"),
					Expiry = (DateOnly.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.None, out var result5) ? result5.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : text),
					Quantity = result,
					FreeQuantity = num2,
					Mrp = result2,
					Rate = result3,
					GstRate = num3,
					DiscountAmount = num4
				};
				purchaseLineDraft.PropertyChanged += OnLineChanged;
				list.Add(purchaseLineDraft);
			}
			Lines.Clear();
			_importedExpectedGrandTotal = null;
			foreach (PurchaseLineDraft item in list)
			{
				Lines.Add(item);
			}
			OnPropertyChanged("Subtotal");
			OnPropertyChanged("TaxTotal");
			OnPropertyChanged("GrandTotal");
			ErrorMessage = string.Empty;
			StatusMessage = $"Mapped {Lines.Count} rows. Confirm each medicine and expiry before saving.";
			HasSpreadsheet = false;
			SpreadsheetPreview = null;
			_spreadsheet = null;
		}
	}

	private static DataView BuildPreview(PurchaseSpreadsheetData data)
	{
		DataTable dataTable = new DataTable();
		List<string> list = data.Headers.Where((string header) => !string.IsNullOrWhiteSpace(header)).Distinct().ToList();
		foreach (string item in list)
		{
			dataTable.Columns.Add(item, typeof(string));
		}
		foreach (IReadOnlyDictionary<string, string> row in data.Rows.Take(5))
		{
			dataTable.Rows.Add(((IEnumerable<string>)list).Select((Func<string, object>)((string header) => row.GetValueOrDefault(header, string.Empty))).ToArray());
		}
		return dataTable.DefaultView;
	}

	private async Task SaveAsync()
	{
		ErrorMessage = string.Empty;
		StatusMessage = string.Empty;
		SupplierError = ((SelectedSupplier == null) ? "Supplier is required" : string.Empty);
		InvoiceNoError = (string.IsNullOrWhiteSpace(InvoiceNo) ? "Supplier invoice no. is required" : string.Empty);
		InvoiceDateError = string.Empty;
		if (SupplierError.Length > 0 || InvoiceNoError.Length > 0)
		{
			return;
		}
		if (SelectedSupplier == null || currentSession.User == null)
		{
			ErrorMessage = "Select a supplier and sign in again.";
			return;
		}
		if (!DateOnly.TryParse(InvoiceDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), out var invoiceDate))
		{
			ErrorMessage = "Enter a valid invoice date.";
			return;
		}
		if (Lines.Count == 0 || Lines.Any((PurchaseLineDraft line) => !line.TryCreateInput(out PurchaseLineInput _)))
		{
			ErrorMessage = "Complete each purchase line with a medicine, batch, expiry, quantity and rate.";
			return;
		}
		if (_importedExpectedGrandTotal.HasValue && decimal.Round(GrandTotal, 2, MidpointRounding.AwayFromZero) != decimal.Round(_importedExpectedGrandTotal.Value, 2, MidpointRounding.AwayFromZero))
		{
			ErrorMessage = $"Calculated total {GrandTotal:F2} still does not match the imported bill total {_importedExpectedGrandTotal.Value:F2}.";
			return;
		}
		try
		{
			using IServiceScope scope = scopeFactory.CreateScope();
			PurchaseService service = scope.ServiceProvider.GetRequiredService<PurchaseService>();
			if (await service.IsDuplicateInvoiceAsync(SelectedSupplier.Id, InvoiceNo))
			{
				ErrorMessage = "This supplier already has an invoice with that number.";
				return;
			}
			PurchaseLineInput[] items = Lines.Select((PurchaseLineDraft line) =>
			{
				line.TryCreateInput(out PurchaseLineInput input);
				return input;
			}).ToArray();
			await service.SavePurchaseAsync(new SavePurchaseInput(SelectedSupplier.Id, InvoiceNo, invoiceDate, Subtotal, 0m, TaxTotal, GrandTotal, items, Remarks, SelectedStorageLocation?.Id), currentSession.User.Id, currentSession.User.Role);
			Lines.Clear();
			_importedExpectedGrandTotal = null;
			InvoiceNo = string.Empty;
			StatusMessage = "Purchase invoice saved.";
			OnPropertyChanged("Subtotal");
			OnPropertyChanged("TaxTotal");
			OnPropertyChanged("GrandTotal");
		}
		catch (Exception ex)
		{
			ErrorMessage = ex.Message;
		}
	}

	private async Task SavePurchaseReturnAsync()
	{
		if (SelectedSupplier == null || currentSession.User == null || string.IsNullOrWhiteSpace(ReturnNo) || string.IsNullOrWhiteSpace(ReturnReason))
		{
			ErrorMessage = "Select a supplier, enter a return number and reason, and provide return lines.";
			return;
		}
		PurchaseReturnLineInput[] array;
		if (ReturnLines.Count > 0)
		{
			array = ReturnLines.Select((PurchaseReturnLineDraft line) => new PurchaseReturnLineInput(line.Batch?.BatchId ?? Guid.Empty, line.Quantity)).ToArray();
		}
		else
		{
			array = (((object)SelectedReturnBatch == null) ? Array.Empty<PurchaseReturnLineInput>() : new PurchaseReturnLineInput[1]
			{
				new PurchaseReturnLineInput(SelectedReturnBatch.BatchId, ReturnQuantity)
			});
		}
		if (array.Length == 0 || array.Any((PurchaseReturnLineInput line) => line.BatchId == Guid.Empty || line.Quantity <= 0m))
		{
			ErrorMessage = "Match every return line to an existing batch and enter positive quantities.";
		}
		else if (array.Any((PurchaseReturnLineInput line) => !ReturnBatches.Any((BatchReturnOption batch) => batch.BatchId == line.BatchId && batch.SupplierId == SelectedSupplier.Id)))
		{
			ErrorMessage = "Every selected return batch must belong to the selected supplier.";
		}
		else
		{
			if (!confirmationService.Confirm($"Post this purchase return for {array.Sum((PurchaseReturnLineInput line) => line.Quantity)} total units to {SelectedSupplier.Name}?", "Confirm purchase return"))
			{
				return;
			}
			try
			{
				using IServiceScope scope = scopeFactory.CreateScope();
				await scope.ServiceProvider.GetRequiredService<PurchaseService>().SavePurchaseReturnAsync(SelectedSupplier.Id, ReturnNo, DateOnly.FromDateTime(DateTime.Today), array, ReturnReason, currentSession.User.Id, currentSession.User.Role);
				ReturnNo = string.Empty;
				ReturnQuantity = 0m;
				ReturnReason = string.Empty;
				ReturnLines.Clear();
				SelectedReturnBatch = null;
				StatusMessage = "Purchase return posted and stock reduced.";
				await LoadAsync();
				ErrorMessage = string.Empty;
			}
			catch (Exception ex)
			{
				ErrorMessage = ex.Message;
			}
		}
	}

	private void OnLineChanged(object? sender, PropertyChangedEventArgs e)
	{
		OnPropertyChanged("Subtotal");
		OnPropertyChanged("TaxTotal");
		OnPropertyChanged("GrandTotal");
	}

	private static string FindLikelyHeader(string target, IReadOnlyList<string> headers)
	{
		string[] candidates = target switch
		{
			"Medicine" => new string[4] { "medicine", "drug", "item", "product" }, 
			"Batch" => new string[2] { "batch", "lot" }, 
			"Expiry" => new string[3] { "expiry", "expdate", "expiration" }, 
			"Quantity" => new string[2] { "quantity", "qty" }, 
			"Free quantity" => new string[3] { "freequantity", "freeqty", "free" }, 
			"MRP" => new string[1] { "mrp" }, 
			"Rate" => new string[3] { "rate", "ptr", "price" }, 
			"GST" => new string[2] { "gst", "tax" }, 
			"Discount" => new string[1] { "discount" }, 
			"Amount" => new string[3] { "amount", "linevalue", "total" }, 
			_ => Array.Empty<string>(), 
		};
		return headers.FirstOrDefault((string header) =>
		{
			string normalizedHeader = header.Replace(" ", string.Empty, StringComparison.Ordinal).Replace("_", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal);
			return candidates.Any((string candidate) => normalizedHeader.Contains(candidate, StringComparison.OrdinalIgnoreCase));
		}) ?? string.Empty;
	}

	private static string Read(IReadOnlyDictionary<string, string> row, IReadOnlyDictionary<string, string> mappings, string target)
	{
		string text = mappings[target];
		if (!string.IsNullOrWhiteSpace(text))
		{
			return row.GetValueOrDefault(text, string.Empty);
		}
		return string.Empty;
	}

	private static decimal OptionalDecimal(string value)
	{
		if (!string.IsNullOrWhiteSpace(value))
		{
			if (!TryDecimal(value, out var result))
			{
				return -1m;
			}
			return result;
		}
		return 0m;
	}

	private static bool TryDecimal(string value, out decimal result)
	{
		return decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out result);
	}
}
