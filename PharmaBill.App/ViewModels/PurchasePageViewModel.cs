using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PharmaBill.App.Services;
using PharmaBill.Core.Ai;
using PharmaBill.Core.Entities;
using PharmaBill.Data.Persistence;
using PharmaBill.Data.Services;
using PharmaBill.Sync;

namespace PharmaBill.App.ViewModels;

public sealed class PurchasePageViewModel(IServiceScopeFactory scopeFactory, CurrentSession currentSession, IFilePickerService filePicker, IConfirmationService confirmationService, PurchaseSpreadsheetReader spreadsheetReader, IPromptService promptService, IQuickAddMedicineDialog quickAdd, ISmartDrugLookupService smartLookup, IPrescriptionDialogService prescriptionDialogs, IOcrTextRecognizer ocr) : ObservableObject, ILoadablePage
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

	private string _newItemDiscountPercent = string.Empty;

	private string _rateSuggestionHint = string.Empty;

	private string _newItemRack = string.Empty;

	private string _newItemError = string.Empty;

	private string _newItemFormulation = string.Empty;

	private string _newItemHsn = string.Empty;

	private string _newItemPackLabel = string.Empty;

	private CancellationTokenSource? _defaultsEnrichCts;

	private string _medicineSearchText = string.Empty;

	private PurchaseMedicinePickerItem? _selectedMedicineItem;

	private bool _commitMedicineSelection;

	private bool _isMedicineDropDownOpen;

	private bool _showManualDrugFields;

	private bool _suppressMedicineSearchSync;

	private string _focusRequest = string.Empty;

	private PurchaseLineDraft? _selectedLine;

	private string? _invoiceImagePath;

	private string? _originalInvoiceFileName;

	private bool _isPdfAttachment;

	private ImageSource? _invoiceThumbnail;

	private string _invoiceOcrText = string.Empty;

	private CancellationTokenSource? _medicineSearchCts;

	private decimal? _importedExpectedGrandTotal;

	private string? _pendingSupplierName;

	private int _loadedDocumentCount;

	private PurchaseSpreadsheetData? _spreadsheet;

	/// <summary>Stable id for the last loaded CSV/Excel content — blocks double Transfer of the same file.</summary>
	private string? _spreadsheetFingerprint;

	private string? _spreadsheetFileName;

	private IReadOnlyList<Drug> _drugIndex = Array.Empty<Drug>();

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? addSupplierCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? addLineCommand;

	private RelayCommand? openMedicineSuggestionsCommand;

	private AsyncRelayCommand? quickAddMedicineCommand;

	private AsyncRelayCommand? searchOnlineCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? cancelAddItemCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? confirmAddItemCommand;

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

	public ObservableCollection<PurchaseMedicinePickerItem> MedicinePickerItems { get; } = new ObservableCollection<PurchaseMedicinePickerItem>();

	public ObservableCollection<BatchReturnOption> ReturnBatches { get; } = new ObservableCollection<BatchReturnOption>();

	public ObservableCollection<PurchaseReturnLineDraft> ReturnLines { get; } = new ObservableCollection<PurchaseReturnLineDraft>();

	public const int PurchaseLinesPerPage = 20;

	public ObservableCollection<PurchaseLineDraft> Lines { get; } = new ObservableCollection<PurchaseLineDraft>();

	public ObservableCollection<PurchaseLineDraft> PagedLines { get; } = new ObservableCollection<PurchaseLineDraft>();

	private bool _linePagingHooked;

	private int _currentPage = 1;

	private int _pagingSuspend;

	private bool _dateFilterActive;

	private DateTime? _filterFromDate;

	private DateTime? _filterToDate;

	private PurchaseLineDraft? _jumpToLine;

	private RelayCommand? _previousPageCommand;

	private RelayCommand? _nextPageCommand;

	private RelayCommand? _filterByDateCommand;

	private RelayCommand? _clearDateFilterCommand;

	public ObservableCollection<StagedPurchaseLine> StagedItems { get; } = new ObservableCollection<StagedPurchaseLine>();

	public ObservableCollection<PhoneIncomingBill> PhoneBills { get; } = new ObservableCollection<PhoneIncomingBill>();

	public bool HasPhoneBills => PhoneBills.Count > 0;

	public bool HasStagedItems => StagedItems.Count > 0;

	public PurchaseLineDraft? SelectedLine
	{
		get => _selectedLine;
		set
		{
			if (!EqualityComparer<PurchaseLineDraft?>.Default.Equals(_selectedLine, value))
			{
				_selectedLine = value;
				OnPropertyChanged(nameof(SelectedLine));
			}
		}
	}

	public string? InvoiceImagePath
	{
		get => _invoiceImagePath;
		private set
		{
			if (!EqualityComparer<string?>.Default.Equals(_invoiceImagePath, value))
			{
				_invoiceImagePath = value;
				OnPropertyChanged(nameof(InvoiceImagePath));
				OnPropertyChanged(nameof(HasInvoiceAttachment));
				OnPropertyChanged(nameof(InvoiceFileName));
				OnPropertyChanged(nameof(PdfBadgeText));
				OnPropertyChanged(nameof(AttachmentSummary));
				viewInvoiceImageCommand?.NotifyCanExecuteChanged();
				extractInvoiceTextCommand?.NotifyCanExecuteChanged();
			}
		}
	}

	public string? OriginalInvoiceFileName
	{
		get => _originalInvoiceFileName;
		private set
		{
			if (!EqualityComparer<string?>.Default.Equals(_originalInvoiceFileName, value))
			{
				_originalInvoiceFileName = value;
				OnPropertyChanged(nameof(OriginalInvoiceFileName));
				OnPropertyChanged(nameof(InvoiceFileName));
				OnPropertyChanged(nameof(PdfBadgeText));
				OnPropertyChanged(nameof(AttachmentSummary));
			}
		}
	}

	public bool IsPdfAttachment
	{
		get => _isPdfAttachment;
		private set
		{
			if (_isPdfAttachment != value)
			{
				_isPdfAttachment = value;
				OnPropertyChanged(nameof(IsPdfAttachment));
				OnPropertyChanged(nameof(ShowPdfTile));
			}
		}
	}

	public ImageSource? InvoiceThumbnail
	{
		get => _invoiceThumbnail;
		private set
		{
			if (!EqualityComparer<ImageSource?>.Default.Equals(_invoiceThumbnail, value))
			{
				_invoiceThumbnail = value;
				OnPropertyChanged(nameof(InvoiceThumbnail));
				OnPropertyChanged(nameof(HasInvoiceThumbnail));
				OnPropertyChanged(nameof(ShowPdfTile));
				extractInvoiceTextCommand?.NotifyCanExecuteChanged();
			}
		}
	}

	public bool HasInvoiceAttachment => !string.IsNullOrWhiteSpace(InvoiceImagePath);

	public bool HasInvoiceThumbnail => InvoiceThumbnail != null;

	public string InvoiceFileName => !string.IsNullOrWhiteSpace(OriginalInvoiceFileName)
		? OriginalInvoiceFileName
		: (string.IsNullOrWhiteSpace(InvoiceImagePath) ? string.Empty : Path.GetFileName(InvoiceImagePath));

	public int LoadedDocumentCount
	{
		get => _loadedDocumentCount;
		private set
		{
			if (_loadedDocumentCount != value)
			{
				_loadedDocumentCount = value;
				OnPropertyChanged(nameof(LoadedDocumentCount));
				OnPropertyChanged(nameof(AttachmentSummary));
			}
		}
	}

	public string AttachmentSummary
	{
		get
		{
			if (!HasInvoiceAttachment)
			{
				return string.Empty;
			}

			string files = LoadedDocumentCount <= 1 ? "1 file loaded" : LoadedDocumentCount + " files loaded";
			return "📄 " + InvoiceFileName + "  |  " + files;
		}
	}

	public bool HasTotalMismatch
	{
		get
		{
			if (!_importedExpectedGrandTotal.HasValue)
			{
				return false;
			}

			return decimal.Round(GrandTotal, 2, MidpointRounding.AwayFromZero) != decimal.Round(_importedExpectedGrandTotal.Value, 2, MidpointRounding.AwayFromZero);
		}
	}

	public string TotalMismatchNote => HasTotalMismatch
		? string.Format(CultureInfo.GetCultureInfo("en-IN"), "Note: Extracted lines total (₹{0:N2}) differs from bill total (₹{1:N2}). Items can still be committed to inventory.", GrandTotal, _importedExpectedGrandTotal)
		: string.Empty;

	public string PdfBadgeText => "📄 PDF Document: " + InvoiceFileName;

	public bool ShowPdfTile => IsPdfAttachment && !HasInvoiceThumbnail;

	public string InvoiceOcrText
	{
		get => _invoiceOcrText;
		private set
		{
			if (!EqualityComparer<string>.Default.Equals(_invoiceOcrText, value))
			{
				_invoiceOcrText = value;
				OnPropertyChanged(nameof(InvoiceOcrText));
				OnPropertyChanged(nameof(HasInvoiceOcrText));
			}
		}
	}

	public bool HasInvoiceOcrText => !string.IsNullOrWhiteSpace(InvoiceOcrText);

	public ObservableCollection<string> SpreadsheetHeaders { get; } = new ObservableCollection<string> { string.Empty };

	public ObservableCollection<SpreadsheetColumnMapping> SpreadsheetMappings { get; } = new ObservableCollection<SpreadsheetColumnMapping>
	{
		new SpreadsheetColumnMapping("Medicine", isRequired: true, new ObservableCollection<string>()),
		new SpreadsheetColumnMapping("Batch", isRequired: true, new ObservableCollection<string>()),
		new SpreadsheetColumnMapping("Expiry", isRequired: true, new ObservableCollection<string>()),
		new SpreadsheetColumnMapping("Quantity", isRequired: true, new ObservableCollection<string>()),
		new SpreadsheetColumnMapping("Free quantity", isRequired: false, new ObservableCollection<string>()),
		new SpreadsheetColumnMapping("MRP", isRequired: false, new ObservableCollection<string>()),
		new SpreadsheetColumnMapping("Rate", isRequired: true, new ObservableCollection<string>()),
		new SpreadsheetColumnMapping("GST", isRequired: false, new ObservableCollection<string>()),
		new SpreadsheetColumnMapping("Discount", isRequired: false, new ObservableCollection<string>()),
		new SpreadsheetColumnMapping("Amount", isRequired: false, new ObservableCollection<string>())
	};

	public int CurrentPage => _currentPage;

	public int TotalItems => Lines?.Count(MatchesDateFilter) ?? 0;

	public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalItems / (double)PurchaseLinesPerPage));

	public string PageSummary => TotalItems == 0
		? "No purchase lines yet"
		: $"{TotalItems} line(s) in the purchase table — scroll to review all";

	public bool CanGoPrevious => _currentPage > 1;

	public bool CanGoNext => _currentPage < TotalPages;

	public bool ShowDateFilterEmpty => _dateFilterActive && (Lines?.Count ?? 0) > 0 && (PagedLines?.Count ?? 0) == 0;

	public DateTime? FilterFromDate
	{
		get => _filterFromDate;
		set
		{
			if (_filterFromDate != value)
			{
				_filterFromDate = value;
				OnPropertyChanged(nameof(FilterFromDate));
			}
		}
	}

	public DateTime? FilterToDate
	{
		get => _filterToDate;
		set
		{
			if (_filterToDate != value)
			{
				_filterToDate = value;
				OnPropertyChanged(nameof(FilterToDate));
			}
		}
	}

	public IRelayCommand PreviousPageCommand => _previousPageCommand ??= new RelayCommand(GoToPreviousPage, () => CanGoPrevious);

	public IRelayCommand NextPageCommand => _nextPageCommand ??= new RelayCommand(GoToNextPage, () => CanGoNext);

	public IRelayCommand FilterByDateCommand => _filterByDateCommand ??= new RelayCommand(ApplyDateFilter);

	public IRelayCommand ClearDateFilterCommand => _clearDateFilterCommand ??= new RelayCommand(ClearDateFilter);

	public decimal Subtotal => Lines?.Sum((PurchaseLineDraft line) => line.BaseAmount) ?? 0m;

	public decimal TaxTotal => Lines?.Sum((PurchaseLineDraft line) => line.TaxAmount) ?? 0m;

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
				DateTime previous = _invoiceDate;
				_invoiceDate = value;
				OnPropertyChanged(nameof(InvoiceDate));
				foreach (PurchaseLineDraft line in Lines)
				{
					if (line.InwardDate.Date == previous.Date)
					{
						line.InwardDate = value.Date;
					}
				}

				if (_dateFilterActive)
				{
					RefreshPaging();
				}
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
				OnPropertyChanged(nameof(ReturnCreditRate));
				OnPropertyChanged(nameof(ShowReturnBatchPrompt));
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
				OnPropertyChanged(nameof(ShowReturnNoPrompt));
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

	public string ReturnCreditRate => SelectedReturnBatch == null ? string.Empty : SelectedReturnBatch.PurchasePrice.ToString("0.00", CultureInfo.InvariantCulture);

	public bool ShowReturnBatchPrompt => SelectedReturnBatch == null;

	public bool ShowReturnNoPrompt => string.IsNullOrWhiteSpace(ReturnNo);

	public bool ShowReturnReasonPrompt => string.IsNullOrWhiteSpace(ReturnReason);

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
				OnPropertyChanged(nameof(ShowReturnReasonPrompt));
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

	public string MedicineSearchText
	{
		get => _medicineSearchText;
		set
		{
			string next = value ?? string.Empty;
			if (!EqualityComparer<string>.Default.Equals(_medicineSearchText, next))
			{
				OnPropertyChanging(nameof(MedicineSearchText));
				_medicineSearchText = next;
				OnPropertyChanged(nameof(MedicineSearchText));
				OnPropertyChanged(nameof(ShowMedicineSearchPlaceholder));
				OnPropertyChanged(nameof(AddMedicineCaption));
				OnPropertyChanged(nameof(ShowNewMedicineButton));
				OnPropertyChanged(nameof(ShowCreateUnlisted));
				OnPropertyChanged(nameof(CreateUnlistedCaption));
				OnPropertyChanged(nameof(ShowOnlineLookup));
				if (!_suppressMedicineSearchSync)
				{
					_ = SearchMedicinesAsync(next);
				}
			}
		}
	}

	public bool ShowMedicineSearchPlaceholder => string.IsNullOrWhiteSpace(MedicineSearchText) && SelectedMedicineItem == null;

	public string AddMedicineCaption => "+ New Medicine";

	public bool ShowCreateUnlisted
	{
		get
		{
			string term = MedicineSearchText.Trim();
			return term.Length >= 2 && (MedicinePickerItems.Count == 0 || MedicinePickerItems.All(item => item.IsAddManually));
		}
	}

	public string CreateUnlistedCaption => "+ Create '" + MedicineSearchText.Trim() + "' as New Medicine";

	public bool ShowNewMedicineButton
	{
		get
		{
			string term = MedicineSearchText.Trim();
			if (term.Length == 0)
			{
				return true;
			}

			if (term.Length < 2)
			{
				return false;
			}

			return MedicinePickerItems.Count == 0 || MedicinePickerItems.All(item => item.IsAddManually);
		}
	}

	public IAsyncRelayCommand QuickAddMedicineCommand => quickAddMedicineCommand ?? (quickAddMedicineCommand = new AsyncRelayCommand(() => QuickAddAndSelectAsync(MedicineSearchText)));

	public IAsyncRelayCommand SearchOnlineCommand => searchOnlineCommand ?? (searchOnlineCommand = new AsyncRelayCommand(SearchOnlineAndSelectAsync));

	public bool ShowOnlineLookup =>
		MedicineSearchText.Trim().Length >= 2
		&& MedicinePickerItems.Count > 0
		&& MedicinePickerItems.All(item => item.IsAddManually);

	public string OnlineLookupCaption => SmartDrugLookupPrompt.Caption;

	public bool IsMedicineDropDownOpen
	{
		get => _isMedicineDropDownOpen;
		set
		{
			if (_isMedicineDropDownOpen != value)
			{
				_isMedicineDropDownOpen = value;
				OnPropertyChanged(nameof(IsMedicineDropDownOpen));
			}
		}
	}

	public PurchaseMedicinePickerItem? SelectedMedicineItem
	{
		get => _selectedMedicineItem;
		set
		{
			if (!EqualityComparer<PurchaseMedicinePickerItem>.Default.Equals(_selectedMedicineItem, value))
			{
				OnPropertyChanging(nameof(SelectedMedicineItem));
				_selectedMedicineItem = value;
				OnPropertyChanged(nameof(SelectedMedicineItem));
				OnPropertyChanged(nameof(ShowMedicineSearchPlaceholder));
				if (value != null && _commitMedicineSelection)
				{
					ApplyMedicineSelection(value);
				}
			}
		}
	}

	public bool ShowManualDrugFields
	{
		get => _showManualDrugFields;
		private set
		{
			if (_showManualDrugFields != value)
			{
				_showManualDrugFields = value;
				OnPropertyChanged(nameof(ShowManualDrugFields));
			}
		}
	}

	public string NewItemFormulation
	{
		get => _newItemFormulation;
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_newItemFormulation, value))
			{
				_newItemFormulation = value ?? string.Empty;
				OnPropertyChanged(nameof(NewItemFormulation));
			}
		}
	}

	public string NewItemHsn
	{
		get => _newItemHsn;
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_newItemHsn, value))
			{
				_newItemHsn = value ?? string.Empty;
				OnPropertyChanged(nameof(NewItemHsn));
			}
		}
	}

	/// <summary>Drug Bank pack / packaging description (e.g. "100ml Bottle", "10 Tablets / Strip").</summary>
	public string NewItemPackLabel
	{
		get => _newItemPackLabel;
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_newItemPackLabel, value))
			{
				_newItemPackLabel = value ?? string.Empty;
				OnPropertyChanged(nameof(NewItemPackLabel));
			}
		}
	}

	public string FocusRequest
	{
		get => _focusRequest;
		private set
		{
			_focusRequest = value;
			OnPropertyChanged(nameof(FocusRequest));
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

	public string NewItemDiscountPercent
	{
		get => _newItemDiscountPercent;
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_newItemDiscountPercent, value))
			{
				_newItemDiscountPercent = value ?? string.Empty;
				OnPropertyChanged(nameof(NewItemDiscountPercent));
			}
		}
	}

	public string RateSuggestionHint
	{
		get => _rateSuggestionHint;
		private set
		{
			if (!EqualityComparer<string>.Default.Equals(_rateSuggestionHint, value))
			{
				_rateSuggestionHint = value ?? string.Empty;
				OnPropertyChanged(nameof(RateSuggestionHint));
				OnPropertyChanged(nameof(HasRateSuggestion));
			}
		}
	}

	public bool HasRateSuggestion => RateSuggestionHint.Length > 0;

	public void NormalizeNewItemExpiry()
	{
		string formatted = FormatExpiryInput(NewItemExpiry);
		if (!string.Equals(formatted, NewItemExpiry, StringComparison.Ordinal))
		{
			NewItemExpiry = formatted;
		}
	}

	/// <summary>Turns 1026, 10/26, or 10-2026 into MM/YYYY. Unrecognized text is left unchanged.</summary>
	public static string FormatExpiryInput(string? raw)
	{
		if (string.IsNullOrWhiteSpace(raw))
		{
			return raw ?? string.Empty;
		}

		string text = raw.Trim().Replace(" ", string.Empty);
		string[] parts = text.Split(new[] { '/', '-', '.' }, StringSplitOptions.RemoveEmptyEntries);
		if (parts.Length == 2 && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int month) && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int year))
		{
			year = ExpandExpiryYear(year, parts[1].Length);
			if (month is >= 1 and <= 12 && year is >= 2000 and <= 2099)
			{
				return month.ToString("00", CultureInfo.InvariantCulture) + "/" + year.ToString(CultureInfo.InvariantCulture);
			}
		}

		string digits = new string(text.Where(char.IsDigit).ToArray());
		if (digits.Length is 4 or 6 && int.TryParse(digits[..2], NumberStyles.None, CultureInfo.InvariantCulture, out month))
		{
			year = digits.Length == 4
				? 2000 + int.Parse(digits[2..], CultureInfo.InvariantCulture)
				: int.Parse(digits[2..], CultureInfo.InvariantCulture);
			if (month is >= 1 and <= 12 && year is >= 2000 and <= 2099)
			{
				return month.ToString("00", CultureInfo.InvariantCulture) + "/" + year.ToString(CultureInfo.InvariantCulture);
			}
		}

		return raw.Trim();
	}

	private static int ExpandExpiryYear(int year, int digitLength)
	{
		if (digitLength <= 2 && year < 100)
		{
			return 2000 + year;
		}

		return year;
	}

	private static decimal StandardGstSlab(decimal rate)
	{
		decimal[] slabs = new decimal[] { 0m, 5m, 12m, 18m, 28m };
		decimal nearest = slabs[0];
		decimal distance = Math.Abs(rate - nearest);
		foreach (decimal slab in slabs)
		{
			decimal next = Math.Abs(rate - slab);
			if (next < distance)
			{
				distance = next;
				nearest = slab;
			}
		}

		return distance <= 0.5m ? nearest : decimal.Round(rate, 2, MidpointRounding.AwayFromZero);
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

	private RelayCommand? _addBlankRowCommand;

	public IRelayCommand AddBlankRowCommand => _addBlankRowCommand ??= new RelayCommand(AddBlankRow);

	public IRelayCommand OpenMedicineSuggestionsCommand => openMedicineSuggestionsCommand ?? (openMedicineSuggestionsCommand = new RelayCommand(OpenMedicineSuggestions));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand CancelAddItemCommand => cancelAddItemCommand ?? (cancelAddItemCommand = new RelayCommand(CancelAddItem));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand ConfirmAddItemCommand => confirmAddItemCommand ?? (confirmAddItemCommand = new AsyncRelayCommand(ConfirmAddItemAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand<PurchaseLineDraft?> DeleteLineCommand => deleteLineCommand ?? (deleteLineCommand = new RelayCommand<PurchaseLineDraft>(DeleteLine));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand CancelSpreadsheetCommand => cancelSpreadsheetCommand ?? (cancelSpreadsheetCommand = new RelayCommand(CancelSpreadsheet));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand ImportDocumentCommand => importDocumentCommand ?? (importDocumentCommand = new AsyncRelayCommand(ImportDocumentAsync));

	public IAsyncRelayCommand ExtractInvoicesCommand => extractInvoicesCommand ?? (extractInvoicesCommand = new AsyncRelayCommand(ExtractInvoicesAsync));

	private IAsyncRelayCommand? extractInvoicesCommand;

	public IRelayCommand ApplyStagedItemsCommand => applyStagedItemsCommand ?? (applyStagedItemsCommand = new RelayCommand(ApplyStagedItems, () => (StagedItems?.Count ?? 0) > 0));

	private RelayCommand? applyStagedItemsCommand;

	public IRelayCommand DiscardStagedItemsCommand => discardStagedItemsCommand ?? (discardStagedItemsCommand = new RelayCommand(DiscardStagedItems, () => (StagedItems?.Count ?? 0) > 0));

	private RelayCommand? discardStagedItemsCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand LoadSpreadsheetCommand => loadSpreadsheetCommand ?? (loadSpreadsheetCommand = new AsyncRelayCommand(LoadSpreadsheetAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand ApplySpreadsheetMappingCommand => applySpreadsheetMappingCommand ?? (applySpreadsheetMappingCommand = new RelayCommand(ApplySpreadsheetMapping));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand SaveCommand => saveCommand ?? (saveCommand = new AsyncRelayCommand(TransferToStockAsync));

	public IAsyncRelayCommand TransferToStockCommand => transferToStockCommand ?? (transferToStockCommand = new AsyncRelayCommand(TransferToStockAsync));

	private AsyncRelayCommand? transferToStockCommand;

	public IRelayCommand NewEntryCommand => newEntryCommand ?? (newEntryCommand = new RelayCommand(BeginNewEntry));

	/// <summary>Dual-binding alias for F2 / Ctrl+N / Ctrl+2 (same as New Entry).</summary>
	public IRelayCommand NewBillCommand => NewEntryCommand;

	public IRelayCommand FocusMedicineCommand => focusMedicineCommand ?? (focusMedicineCommand = new RelayCommand(RequestFocusMedicineSearch));

	public IRelayCommand FocusMedicineSearchCommand => FocusMedicineCommand;

	/// <summary>F3 focuses supplier search on purchases when available.</summary>
	public IRelayCommand FocusPatientCommand => focusPatientCommand ??= new RelayCommand(() =>
	{
		StatusMessage = "Purchases: choose a distributor, then press F4 (or Ctrl+M) to add items.";
	});

	private RelayCommand? focusPatientCommand;

	public IRelayCommand FocusPaymentCommand => focusPaymentCommand ??= new RelayCommand(() => { });

	private RelayCommand? focusPaymentCommand;

	public IRelayCommand HoldBillCommand => holdBillCommand ??= new RelayCommand(() =>
	{
		StatusMessage = "Purchase inward has no hold queue — use F2 for a new entry.";
	});

	private RelayCommand? holdBillCommand;

	public IRelayCommand CancelBillCommand => CancelAddItemCommand;

	public IAsyncRelayCommand SaveAndPrintCommand => TransferToStockCommand;

	public IAsyncRelayCommand ShowPaymentQrCommand => showPaymentQrCommand ??= new AsyncRelayCommand(() => Task.CompletedTask);

	private AsyncRelayCommand? showPaymentQrCommand;

	public IAsyncRelayCommand ShowSubstitutesCommand => showSubstitutesCommand ??= new AsyncRelayCommand(() => Task.CompletedTask);

	private AsyncRelayCommand? showSubstitutesCommand;

	private RelayCommand? focusMedicineCommand;

	private RelayCommand? newEntryCommand;

	public IAsyncRelayCommand ScanInvoiceCommand => scanInvoiceCommand ?? (scanInvoiceCommand = new AsyncRelayCommand(ScanInvoiceAsync));

	public IAsyncRelayCommand ScanPurchaseWithHandheldScannerCommand => scanPurchaseWithHandheldScannerCommand ?? (scanPurchaseWithHandheldScannerCommand = new AsyncRelayCommand(ScanPurchaseWithHandheldScannerAsync));

	private AsyncRelayCommand? scanPurchaseWithHandheldScannerCommand;

	public IAsyncRelayCommand ScanViaPhoneCommand => scanViaPhoneCommand ?? (scanViaPhoneCommand = new AsyncRelayCommand(ScanViaPhoneAsync));

	public IRelayCommand<PhoneIncomingBill?> OpenPhoneBillCommand => openPhoneBillCommand ?? (openPhoneBillCommand = new RelayCommand<PhoneIncomingBill>(LoadPhoneBill));

	private AsyncRelayCommand? scanViaPhoneCommand;

	private RelayCommand<PhoneIncomingBill>? openPhoneBillCommand;

	private PurchasePhoneBridge? _phoneBridge;

	private PhoneIncomingBill? _loadedPhoneBill;

	private PhoneIncomingBill? _selectedPhoneBill;

	private int _phoneBillSequence;

	private bool _inwardAllPhoneBills;

	private ImageSource? _phonePairingQr;

	private string _phoneListenStatus = "Phone scan is stopped.";

	private bool _isPhoneListening;

	private bool _isExtractingPhoneBill;

	public bool InwardAllPhoneBills
	{
		get
		{
			return _inwardAllPhoneBills;
		}
		set
		{
			if (_inwardAllPhoneBills != value)
			{
				_inwardAllPhoneBills = value;
				OnPropertyChanged(nameof(InwardAllPhoneBills));
			}
		}
	}

	public ImageSource? PhonePairingQr
	{
		get
		{
			return _phonePairingQr;
		}
		private set
		{
			_phonePairingQr = value;
			OnPropertyChanged(nameof(PhonePairingQr));
		}
	}

	public string PhoneListenStatus
	{
		get
		{
			return _phoneListenStatus;
		}
		private set
		{
			_phoneListenStatus = value;
			OnPropertyChanged(nameof(PhoneListenStatus));
		}
	}

	public bool IsPhoneListening
	{
		get => _isPhoneListening;
		private set
		{
			if (_isPhoneListening != value)
			{
				_isPhoneListening = value;
				OnPropertyChanged(nameof(IsPhoneListening));
			}
		}
	}

	/// <summary>Phone bill currently shown on the purchase grid.</summary>
	public PhoneIncomingBill? SelectedPhoneBill
	{
		get => _selectedPhoneBill;
		set
		{
			if (!ReferenceEquals(_selectedPhoneBill, value))
			{
				_selectedPhoneBill = value;
				OnPropertyChanged(nameof(SelectedPhoneBill));
			}
		}
	}

	public bool IsExtractingPhoneBill
	{
		get => _isExtractingPhoneBill;
		private set
		{
			if (_isExtractingPhoneBill != value)
			{
				_isExtractingPhoneBill = value;
				OnPropertyChanged(nameof(IsExtractingPhoneBill));
			}
		}
	}

	public IAsyncRelayCommand AttachInvoiceFileCommand => attachInvoiceFileCommand ?? (attachInvoiceFileCommand = new AsyncRelayCommand(AttachInvoiceFileAsync));

	private AsyncRelayCommand? attachInvoiceFileCommand;

	private AsyncRelayCommand? scanInvoiceCommand;

	public IRelayCommand ShowInvoiceHistoryCommand => showInvoiceHistoryCommand ?? (showInvoiceHistoryCommand = new RelayCommand(ShowInvoiceHistory));

	private RelayCommand? showInvoiceHistoryCommand;

	public IRelayCommand ViewInvoiceImageCommand => viewInvoiceImageCommand ?? (viewInvoiceImageCommand = new RelayCommand(ViewInvoiceImage, () => HasInvoiceAttachment));

	private RelayCommand? viewInvoiceImageCommand;

	public IAsyncRelayCommand ExtractInvoiceTextCommand => extractInvoiceTextCommand ?? (extractInvoiceTextCommand = new AsyncRelayCommand(ExtractInvoiceTextAsync, () => HasInvoiceThumbnail));

	private AsyncRelayCommand? extractInvoiceTextCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand SavePurchaseReturnCommand => savePurchaseReturnCommand ?? (savePurchaseReturnCommand = new AsyncRelayCommand(SavePurchaseReturnAsync));

	public async Task LoadAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		EnsureLinePaging();
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
		_ = LoadDrugIndexInBackgroundAsync(cancellationToken);
		ReturnBatches.Clear();
		foreach (var item4 in await (from batch in context.Batches
			join drug in context.Drugs on batch.DrugId equals drug.Id
			join supplier in context.Suppliers on batch.SupplierId equals supplier.Id
			where batch.SupplierId.HasValue && batch.Quantity > 0m
			orderby batch.BatchNo
			select new { Batch = batch, DrugName = drug.Name, SupplierName = supplier.Name }).ToListAsync(cancellationToken))
		{
			Guid supplierId = item4.Batch.SupplierId!.Value;
			ReturnBatches.Add(new BatchReturnOption(
				item4.Batch.Id,
				supplierId,
				item4.Batch.BatchNo,
				$"{item4.DrugName} / {item4.Batch.BatchNo} / {item4.SupplierName} / Qty {item4.Batch.Quantity}",
				item4.Batch.PurchasePrice));
		}
	}

	private async Task LoadDrugIndexInBackgroundAsync(CancellationToken cancellationToken)
	{
		try
		{
			List<Drug> loadedDrugs = await Task.Run(async () =>
			{
				Thread.CurrentThread.Priority = ThreadPriority.BelowNormal;
				using IServiceScope backgroundScope = scopeFactory.CreateScope();
				PharmaBillDbContext backgroundContext = backgroundScope.ServiceProvider.GetRequiredService<PharmaBillDbContext>();
				return await backgroundContext.Drugs.AsNoTracking()
					.Where(item => item.IsActive)
					.OrderBy(item => item.Name)
					.ToListAsync(cancellationToken);
			}, cancellationToken).ConfigureAwait(false);
			_drugIndex = loadedDrugs;
			await DispatchAsync(() => StatusMessage = $"Medicine master ready ({loadedDrugs.Count:N0} medicines).").ConfigureAwait(false);
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex)
		{
			await DispatchAsync(() => ErrorMessage = $"Medicine master could not be loaded: {ex.Message}").ConfigureAwait(false);
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
		_pagingSuspend++;
		try
		{
		foreach (PurchaseIndentItem item in group.Items)
		{
			Drug? drug = FindDrug(item.DrugId);
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
		}
		finally
		{
			_pagingSuspend = Math.Max(0, _pagingSuspend - 1);
			RefreshPaging(resetToFirst: true);
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
		string text = promptService.AskText("Add supplier / distributor", "Enter the distributor or wholesaler name (not a retail patient).", "Supplier name");
		if (text == null)
		{
			return;
		}
		string? gstin = promptService.AskText("Supplier GSTIN", "Enter GSTIN if printed on the bill (optional).", "GSTIN");
		string? drugLicence = promptService.AskText("Supplier drug licence", "Enter Form 20B / 21B (or state equivalent) licence number (optional).", "DL No. 20B/21B");
		try
		{
			using IServiceScope scope = scopeFactory.CreateScope();
			Supplier supplier = await scope.ServiceProvider.GetRequiredService<AddStockService>().AddSupplierAsync(text, currentSession.User.Id, currentSession.User.Role);
			if (!string.IsNullOrWhiteSpace(gstin) || !string.IsNullOrWhiteSpace(drugLicence))
			{
				PharmaBillDbContext context = scope.ServiceProvider.GetRequiredService<PharmaBillDbContext>();
				Supplier tracked = await context.Suppliers.SingleAsync((Supplier item) => item.Id == supplier.Id);
				if (!string.IsNullOrWhiteSpace(gstin))
				{
					tracked.Gstin = gstin.Trim();
					supplier.Gstin = tracked.Gstin;
				}

				if (!string.IsNullOrWhiteSpace(drugLicence))
				{
					tracked.DrugLicenceNumber = drugLicence.Trim();
					supplier.DrugLicenceNumber = tracked.DrugLicenceNumber;
				}

				await context.SaveChangesAsync();
			}

			Supplier supplier2 = Suppliers.FirstOrDefault((Supplier item) => item.Id == supplier.Id);
			if (supplier2 == null)
			{
				Suppliers.Add(supplier);
				supplier2 = supplier;
			}
			else
			{
				supplier2.Gstin = supplier.Gstin;
				supplier2.DrugLicenceNumber = supplier.DrugLicenceNumber;
			}
			SelectedSupplier = supplier2;
			ErrorMessage = string.Empty;
			StatusMessage = "Supplier saved under Purchases. Distributors are not retail customers.";
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
		RequestFocus("NewItemMedicine");
	}

	/// <summary>Insert an empty editable inward row (for OCR misses) without opening the add-item form.</summary>
	private void AddBlankRow()
	{
		PurchaseLineDraft line = new PurchaseLineDraft(null)
		{
			MedicineName = string.Empty,
			BatchNo = string.Empty,
			Expiry = string.Empty,
			Quantity = 1m,
			FreeQuantity = 0m,
			Mrp = 0m,
			Rate = 0m,
			GstRate = 0m,
			DiscountAmount = 0m,
			Rack = null,
			InwardDate = InvoiceDate.Date,
			AcceptMedicineEdits = true
		};
		AddDraftLine(line);
		SelectedLine = line;
		StatusMessage = "Blank row added — edit Medicine, Batch, Qty, and Rate in the grid.";
	}

	private void OpenMedicineSuggestions()
	{
		if (MedicinePickerItems.Count > 0)
		{
			IsMedicineDropDownOpen = true;
		}
		else if (!string.IsNullOrWhiteSpace(MedicineSearchText) && MedicineSearchText.Trim().Length >= 2)
		{
			_ = SearchMedicinesAsync(MedicineSearchText);
		}
	}

	public void SelectMedicineSuggestion(PurchaseMedicinePickerItem? item)
	{
		if (item == null)
		{
			return;
		}

		_commitMedicineSelection = true;
		try
		{
			if (!EqualityComparer<PurchaseMedicinePickerItem>.Default.Equals(_selectedMedicineItem, item))
			{
				SelectedMedicineItem = item;
				return;
			}

			ApplyMedicineSelection(item);
		}
		finally
		{
			_commitMedicineSelection = false;
		}
	}

	private void CancelAddItem()
	{
		IsAddItemOpen = false;
		ClearNewItem();
	}

	private async Task ConfirmAddItemAsync()
	{
		NewItemError = string.Empty;
		Drug? drug;
		try
		{
			drug = await EnsureDrugForNewItemAsync();
		}
		catch (Exception ex)
		{
			NewItemError = ex.Message;
			return;
		}

		if (drug == null)
		{
			NewItemError = "Search and select a medicine from the Drug Bank catalogue, or choose + Add Manually.";
			RequestFocus("NewItemMedicine");
			return;
		}

		if (string.IsNullOrWhiteSpace(NewItemBatch))
		{
			NewItemError = "Batch no. is required";
			RequestFocus("NewItemBatch");
			return;
		}
		NormalizeNewItemExpiry();
		if (!AddStockViewModel.TryParseExpiry(NewItemExpiry, out var lastDay))
		{
			NewItemError = "Expiry (month/year) is required, for example 08/2027";
			RequestFocus("NewItemExpiry");
			return;
		}
		if (lastDay < DateOnly.FromDateTime(DateTime.Today))
		{
			NewItemError = "This date has expired; expired stock cannot be purchased";
			RequestFocus("NewItemExpiry");
			return;
		}
		if (!TryDecimal(NewItemQuantity, out var result) || result <= 0m)
		{
			NewItemError = "Quantity must be more than 0";
			RequestFocus("NewItemQuantity");
			return;
		}
		if (!TryDecimal(NewItemMrp, out var result2) || result2 < 0m || !TryDecimal(NewItemRate, out var result3) || result3 < 0m)
		{
			NewItemError = "MRP and Rate are required numbers";
			RequestFocus("NewItemMrp");
			return;
		}
		decimal num = OptionalDecimal(NewItemFree);
		decimal num2 = OptionalDecimal(NewItemGst);
		decimal discountPercent = OptionalDecimal(NewItemDiscountPercent);
		if (num < 0m || num2 < 0m || num2 > 100m || discountPercent < 0m || discountPercent > 100m)
		{
			NewItemError = "Free quantity, GST % and trade discount % must be valid numbers";
			RequestFocus("NewItemGst");
			return;
		}

		PurchaseLineDraft line = new PurchaseLineDraft(drug)
		{
			MedicineName = string.IsNullOrWhiteSpace(drug.Name) ? MedicineSearchText.Trim() : drug.Name.Trim(),
			BatchNo = NewItemBatch.Trim(),
			Expiry = lastDay.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
			Quantity = result,
			FreeQuantity = num,
			Mrp = result2,
			Rate = result3,
			GstRate = num2,
			DiscountAmount = decimal.Round(result * result3 * discountPercent / 100m, 2, MidpointRounding.AwayFromZero),
			Rack = (string.IsNullOrWhiteSpace(NewItemRack) ? null : NewItemRack.Trim())
		};
		AddDraftLine(line);
		ClearNewItem();
		IsAddItemOpen = true;
		StatusMessage = $"{line.MedicineName} added to the purchase bill. Enter the next medicine.";
		RequestFocus("NewItemMedicine");
	}

	private async Task SearchOnlineAndSelectAsync()
	{
		string typed = MedicineSearchText.Trim();
		SmartDrugLookupOutcome outcome = await smartLookup.LookupAsync(typed);
		await QuickAddAndSelectAsync(outcome.Suggestion?.BrandName ?? typed, outcome.Suggestion, outcome.Message);
	}

	private async Task QuickAddAndSelectAsync(string typedName, SmartDrugSuggestion? suggestion = null, string? notice = null)
	{
		CustomMedicineResult? saved = await quickAdd.ShowAsync(typedName, suggestion, notice);
		if (saved == null)
		{
			if (SelectedMedicineItem?.IsAddManually == true)
			{
				SelectedMedicineItem = null;
			}

			return;
		}

		using IServiceScope scope = scopeFactory.CreateScope();
		Drug drug = await scope.ServiceProvider.GetRequiredService<PharmaBillDbContext>().Drugs.AsNoTracking()
			.FirstAsync(item => item.Id == saved.DrugId);
		if (Drugs.All(item => item.Id != drug.Id))
		{
			Drugs.Add(drug);
		}

		PurchaseMedicinePickerItem picker = PurchaseMedicinePickerItem.FromLocalDrug(drug);
		ShowManualDrugFields = false;
		_suppressMedicineSearchSync = true;
		try
		{
			MedicineSearchText = drug.Name;
		}
		finally
		{
			_suppressMedicineSearchSync = false;
		}

		NewItemDrug = drug;
		ApplyImmediateDefaults(picker);
		NewItemBatch = "COUNTER";
		NewItemExpiry = DateTime.Today.AddYears(1).ToString("MM/yyyy", CultureInfo.InvariantCulture);
		NewItemQuantity = "1";
		if (saved.Mrp is > 0m)
		{
			NewItemMrp = FormatAmount(saved.Mrp.Value);
		}
		else if (string.IsNullOrWhiteSpace(NewItemMrp))
		{
			NewItemMrp = "0";
		}

		if (saved.PurchaseRate is > 0m)
		{
			NewItemRate = FormatAmount(saved.PurchaseRate.Value);
		}
		else if (saved.Mrp is > 0m)
		{
			NewItemRate = FormatAmount(saved.Mrp.Value);
		}
		else if (string.IsNullOrWhiteSpace(NewItemRate))
		{
			NewItemRate = "0";
		}

		if (saved.GstRate >= 0m)
		{
			NewItemGst = FormatAmount(saved.GstRate);
		}

		IsAddItemOpen = true;
		IsMedicineDropDownOpen = false;
		NewItemError = string.Empty;
		await ConfirmAddItemAsync();
	}

	private void ApplyMedicineSelection(PurchaseMedicinePickerItem item)
	{
		ShowManualDrugFields = item.IsAddManually;
		IsMedicineDropDownOpen = false;
		_suppressMedicineSearchSync = true;
		try
		{
			MedicineSearchText = item.IsAddManually ? item.SearchText : item.DisplayTitle;
		}
		finally
		{
			_suppressMedicineSearchSync = false;
		}

		NewItemDrug = item.Drug;
		ApplyImmediateDefaults(item);

		if (item.IsAddManually)
		{
			ShowManualDrugFields = false;
			_ = QuickAddAndSelectAsync(item.SearchText);
			return;
		}

		_ = EnrichDefaultsFromMasterAsync(item);
		RequestFocus("NewItemQuantity");
	}

	private void ApplyImmediateDefaults(PurchaseMedicinePickerItem item)
	{
		if (item.SuggestedGst is > 0m)
		{
			NewItemGst = FormatAmount(item.SuggestedGst.Value);
		}
		else if (item.Drug?.GstRate is > 0m)
		{
			NewItemGst = FormatAmount(item.Drug.GstRate.Value);
		}

		if (!string.IsNullOrWhiteSpace(item.SuggestedHsn))
		{
			NewItemHsn = item.SuggestedHsn!;
		}
		else if (!string.IsNullOrWhiteSpace(item.Drug?.HsnCode))
		{
			NewItemHsn = item.Drug!.HsnCode!;
		}

		if (!string.IsNullOrWhiteSpace(item.DosageForm))
		{
			NewItemFormulation = item.DosageForm!;
		}
		else if (!string.IsNullOrWhiteSpace(item.Drug?.DosageForm))
		{
			NewItemFormulation = item.Drug!.DosageForm!;
		}
		else if (!string.IsNullOrWhiteSpace(item.Composition) && item.IsAddManually)
		{
			NewItemFormulation = item.Composition!;
		}

		decimal? mrp = item.SuggestedMrp is > 0m
			? item.SuggestedMrp
			: (item.Drug?.Mrp is > 0m ? item.Drug.Mrp : null);
		if (mrp is > 0m)
		{
			NewItemMrp = FormatAmount(mrp.Value);
		}

		if (!string.IsNullOrWhiteSpace(item.PackSizeLabel))
		{
			NewItemPackLabel = item.PackSizeLabel!;
		}
		else if (!string.IsNullOrWhiteSpace(item.Drug?.Unit))
		{
			NewItemPackLabel = item.Drug!.Unit!;
		}

		if (item.SuggestedPurchaseRate is > 0m)
		{
			NewItemRate = FormatAmount(item.SuggestedPurchaseRate.Value);
		}

		// Default packs received to 1 so the operator only overtypes the count.
		if (string.IsNullOrWhiteSpace(NewItemQuantity) && !item.IsAddManually)
		{
			NewItemQuantity = "1";
		}
	}

	private async Task EnrichDefaultsFromMasterAsync(PurchaseMedicinePickerItem item)
	{
		_defaultsEnrichCts?.Cancel();
		CancellationTokenSource cts = new CancellationTokenSource();
		_defaultsEnrichCts = cts;
		try
		{
			using IServiceScope scope = scopeFactory.CreateScope();
			AddStockService addStock = scope.ServiceProvider.GetRequiredService<AddStockService>();
			Guid? drugId = item.Drug?.Id;
			Guid? catalogId = item.CatalogMedicineId ?? item.Drug?.CatalogMedicineId;

			var catalogDefaults = await addStock.SuggestCatalogDefaultsAsync(drugId, catalogId, cts.Token);
			if (cts.IsCancellationRequested)
			{
				return;
			}

			if (string.IsNullOrWhiteSpace(NewItemPackLabel) && !string.IsNullOrWhiteSpace(catalogDefaults.PackSizeLabel))
			{
				NewItemPackLabel = catalogDefaults.PackSizeLabel!;
			}
			if (string.IsNullOrWhiteSpace(NewItemFormulation) && !string.IsNullOrWhiteSpace(catalogDefaults.DosageForm))
			{
				NewItemFormulation = catalogDefaults.DosageForm!;
			}
			if (string.IsNullOrWhiteSpace(NewItemGst) && catalogDefaults.GstRate is > 0m)
			{
				NewItemGst = FormatAmount(catalogDefaults.GstRate.Value);
			}
			if (string.IsNullOrWhiteSpace(NewItemHsn) && !string.IsNullOrWhiteSpace(catalogDefaults.HsnCode))
			{
				NewItemHsn = catalogDefaults.HsnCode!;
			}

			decimal? suggestedMrp = await addStock.SuggestMrpAsync(drugId, catalogId, cts.Token);
			if (cts.IsCancellationRequested)
			{
				return;
			}

			if (suggestedMrp is > 0m)
			{
				NewItemMrp = FormatAmount(suggestedMrp.Value);
			}
			else if (string.IsNullOrWhiteSpace(NewItemMrp) && catalogDefaults.ReferencePrice is > 0m)
			{
				NewItemMrp = FormatAmount(catalogDefaults.ReferencePrice.Value);
			}

			decimal? suggestedRate = await addStock.SuggestPurchaseRateAsync(drugId, catalogId, cts.Token);
			if (cts.IsCancellationRequested)
			{
				return;
			}

			if (suggestedRate is > 0m && string.IsNullOrWhiteSpace(NewItemRate))
			{
				NewItemRate = FormatAmount(suggestedRate.Value);
			}

			PharmaBillDbContext context = scope.ServiceProvider.GetRequiredService<PharmaBillDbContext>();
			await ApplyLastPurchaseSuggestionAsync(item, context, cts.Token);
		}
		catch (OperationCanceledException)
		{
		}
		catch
		{
			// Keep immediate picker defaults if enrichment fails (offline / missing catalog row).
		}
	}

	private async Task ApplyLastPurchaseSuggestionAsync(PurchaseMedicinePickerItem item, PharmaBillDbContext context, CancellationToken cancellationToken)
	{
		Guid? drugId = item.Drug?.Id;
		if (!drugId.HasValue && item.CatalogMedicineId is Guid catalogId)
		{
			drugId = await context.Drugs.AsNoTracking()
				.Where(drug => drug.IsActive && drug.CatalogMedicineId == catalogId)
				.Select(drug => (Guid?)drug.Id)
				.FirstOrDefaultAsync(cancellationToken);
		}

		if (!drugId.HasValue || cancellationToken.IsCancellationRequested)
		{
			return;
		}

		var history = context.PurchaseItems.AsNoTracking()
			.Where(line => !line.IsDeleted && line.DrugId == drugId && line.UnitPrice > 0m)
			.Join(
				context.PurchaseInvoices.AsNoTracking().Where(invoice => !invoice.IsDeleted),
				line => line.PurchaseInvoiceId,
				invoice => invoice.Id,
				(line, invoice) => new
				{
					line.UnitPrice,
					line.DiscountAmount,
					line.Quantity,
					line.TaxRate,
					line.CreatedAtUtc,
					invoice.InvoiceDate,
					invoice.InvoiceNo,
					invoice.SupplierId
				});
		var supplierHistory = SelectedSupplier == null
			? history
			: history.Where(row => row.SupplierId == SelectedSupplier.Id);
		string rateSnapshot = NewItemRate;
		string gstSnapshot = NewItemGst;
		var last = await supplierHistory
			.OrderByDescending(row => row.InvoiceDate)
			.ThenByDescending(row => row.CreatedAtUtc)
			.FirstOrDefaultAsync(cancellationToken);
		bool usedSupplier = last != null && SelectedSupplier != null;
		if (last == null && SelectedSupplier != null)
		{
			last = await history
				.OrderByDescending(row => row.InvoiceDate)
				.ThenByDescending(row => row.CreatedAtUtc)
				.FirstOrDefaultAsync(cancellationToken);
			usedSupplier = false;
		}

		if (last == null || cancellationToken.IsCancellationRequested)
		{
			return;
		}

		if (string.IsNullOrWhiteSpace(NewItemRate) || NewItemRate == rateSnapshot)
		{
			NewItemRate = FormatAmount(last.UnitPrice);
		}

		decimal slab = StandardGstSlab(last.TaxRate);
		if (string.IsNullOrWhiteSpace(NewItemGst) || NewItemGst == gstSnapshot)
		{
			NewItemGst = FormatAmount(slab);
		}

		decimal gross = last.Quantity * last.UnitPrice;
		decimal discountPercent = gross <= 0m ? 0m : decimal.Round(last.DiscountAmount * 100m / gross, 2, MidpointRounding.AwayFromZero);
		if (string.IsNullOrWhiteSpace(NewItemDiscountPercent))
		{
			NewItemDiscountPercent = FormatAmount(discountPercent);
		}

		string source = usedSupplier && SelectedSupplier != null
			? SelectedSupplier.Name
			: "a previous supplier";
		RateSuggestionHint = $"Suggested from last purchase {last.InvoiceNo} ({source}): rate {FormatAmount(last.UnitPrice)}, trade discount {FormatAmount(discountPercent)}%, GST {FormatAmount(slab)}%. You can edit any box.";
	}

	private static string FormatAmount(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);

	private async Task SearchMedicinesAsync(string query)
	{
		_medicineSearchCts?.Cancel();
		CancellationTokenSource cts = new CancellationTokenSource();
		_medicineSearchCts = cts;
		string term = query.Trim();
		if (term.Length < 2)
		{
			MedicinePickerItems.Clear();
			IsMedicineDropDownOpen = false;
			OnPropertyChanged(nameof(ShowOnlineLookup));
			OnPropertyChanged(nameof(ShowNewMedicineButton));
			return;
		}

		try
		{
			await Task.Delay(180, cts.Token);
		}
		catch (OperationCanceledException)
		{
			return;
		}

		try
		{
			using IServiceScope scope = scopeFactory.CreateScope();
			PharmaBillDbContext context = scope.ServiceProvider.GetRequiredService<PharmaBillDbContext>();
			CatalogSearchService catalog = scope.ServiceProvider.GetRequiredService<CatalogSearchService>();

			List<PurchaseMedicinePickerItem> results = new List<PurchaseMedicinePickerItem>();
			foreach (Drug drug in _drugIndex.Where(d =>
				         d.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
				         || (d.BrandName?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)
				         || (d.GenericName?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false))
			         .Take(12))
			{
				results.Add(PurchaseMedicinePickerItem.FromLocalDrug(drug));
			}

			MedicineSearchResults catalogResults = await catalog.SearchAsync(term, cts.Token);
			HashSet<Guid> localCatalogIds = results
				.Where(r => r.CatalogMedicineId.HasValue)
				.Select(r => r.CatalogMedicineId!.Value)
				.ToHashSet();

			foreach (MedicineSearchResult row in catalogResults.InStock.Concat(catalogResults.FromCatalog).Take(25))
			{
				if (localCatalogIds.Contains(row.CatalogMedicineId))
				{
					continue;
				}

				if (results.Any(r => r.Drug != null && row.DrugId.HasValue && r.Drug.Id == row.DrugId.Value))
				{
					continue;
				}

				if (row.DrugId is Guid linkedId)
				{
					Drug? linked = FindDrug(linkedId)
						?? await context.Drugs.AsNoTracking().FirstOrDefaultAsync(d => d.Id == linkedId, cts.Token);
					if (linked != null)
					{
						results.Add(PurchaseMedicinePickerItem.FromLocalDrug(linked));
						continue;
					}
				}

				decimal? gstHint = await context.Drugs.AsNoTracking()
					.Where(d => d.CatalogMedicineId == row.CatalogMedicineId && d.GstRate != null)
					.Select(d => d.GstRate)
					.FirstOrDefaultAsync(cts.Token);
				string? hsnHint = await context.Drugs.AsNoTracking()
					.Where(d => d.CatalogMedicineId == row.CatalogMedicineId && d.HsnCode != null && d.HsnCode != "")
					.Select(d => d.HsnCode)
					.FirstOrDefaultAsync(cts.Token);
				results.Add(PurchaseMedicinePickerItem.FromCatalogue(row, gstHint, hsnHint));
			}

			if (results.Count == 0)
			{
				results.Insert(0, PurchaseMedicinePickerItem.AddManually(term));
			}

			if (cts.IsCancellationRequested)
			{
				return;
			}

			MedicinePickerItems.Clear();
			foreach (PurchaseMedicinePickerItem item in results.Take(30))
			{
				MedicinePickerItems.Add(item);
			}
			IsMedicineDropDownOpen = MedicinePickerItems.Count > 0;
			OnPropertyChanged(nameof(ShowOnlineLookup));
			OnPropertyChanged(nameof(ShowNewMedicineButton));
			OnPropertyChanged(nameof(ShowCreateUnlisted));
			OnPropertyChanged(nameof(CreateUnlistedCaption));
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex)
		{
			NewItemError = ex.Message;
			OnPropertyChanged(nameof(ShowOnlineLookup));
			OnPropertyChanged(nameof(ShowNewMedicineButton));
			OnPropertyChanged(nameof(ShowCreateUnlisted));
			OnPropertyChanged(nameof(CreateUnlistedCaption));
		}
	}

	private async Task<Drug?> EnsureDrugForNewItemAsync()
	{
		if (NewItemDrug != null)
		{
			return NewItemDrug;
		}

		PurchaseMedicinePickerItem? selected = SelectedMedicineItem;
		if (selected == null)
		{
			return null;
		}

		if (currentSession.User == null)
		{
			throw new UnauthorizedAccessException("Sign in before adding medicines.");
		}

		using IServiceScope scope = scopeFactory.CreateScope();
		PharmaBillDbContext context = scope.ServiceProvider.GetRequiredService<PharmaBillDbContext>();

		if (selected.IsCatalogue && selected.CatalogMedicineId is Guid catalogId)
		{
			Drug? existing = await context.Drugs.FirstOrDefaultAsync(d => d.CatalogMedicineId == catalogId && d.IsActive);
			if (existing != null)
			{
				if (Drugs.All(d => d.Id != existing.Id))
				{
					Drugs.Add(existing);
				}
				NewItemDrug = existing;
				return existing;
			}

			CatalogMedicine catalog = await context.CatalogMedicines.AsNoTracking()
				.FirstAsync(m => m.Id == catalogId);
			decimal gst = OptionalDecimal(NewItemGst);
			if (gst < 0m || gst > 100m)
			{
				gst = selected.SuggestedGst ?? 12m;
			}

			Drug created = new Drug
			{
				Name = catalog.Name.Trim(),
				BrandName = catalog.BrandName,
				GenericName = catalog.GenericName ?? catalog.CompositionKey,
				Strength = catalog.Strength,
				DosageForm = string.IsNullOrWhiteSpace(NewItemFormulation) ? catalog.DosageForm : NewItemFormulation.Trim(),
				HsnCode = string.IsNullOrWhiteSpace(NewItemHsn) ? selected.SuggestedHsn : NewItemHsn.Trim(),
				GstRate = gst,
				Mrp = catalog.ReferencePrice,
				SalePrice = catalog.ReferencePrice,
				CatalogMedicineId = catalog.Id,
				IsActive = true
			};
			context.Drugs.Add(created);
			context.AuditLogs.Add(new AuditLog
			{
				UserId = currentSession.User.Id,
				Action = "DrugCreatedFromPurchase",
				EntityName = "Drug",
				EntityId = created.Id,
				Details = created.Name + " from Drug Bank catalogue"
			});
			await context.SaveChangesAsync();
			Drugs.Add(created);
			NewItemDrug = created;
			return created;
		}

		if (selected.IsAddManually)
		{
			string name = string.IsNullOrWhiteSpace(MedicineSearchText) ? selected.SearchText : MedicineSearchText.Trim();
			if (string.IsNullOrWhiteSpace(name))
			{
				throw new InvalidOperationException("Enter the new medicine name.");
			}

			decimal gst = 12m;
			if (TryDecimal(NewItemGst, out var parsedGst) && parsedGst >= 0m && parsedGst <= 100m)
			{
				gst = parsedGst;
			}

			decimal? mrp = TryDecimal(NewItemMrp, out var parsedMrp) && parsedMrp >= 0m ? parsedMrp : null;
			decimal? rate = TryDecimal(NewItemRate, out var parsedRate) && parsedRate >= 0m ? parsedRate : null;
			CustomMedicineResult saved = await scope.ServiceProvider.GetRequiredService<CustomMedicineService>().SaveAsync(
				new CustomMedicineRequest(
					name,
					null,
					null,
					string.IsNullOrWhiteSpace(NewItemPackLabel) ? NewItemFormulation : NewItemPackLabel,
					NewItemHsn,
					gst,
					mrp,
					rate),
				currentSession.User?.Id);
			Drug manual = await context.Drugs.FirstAsync(item => item.Id == saved.DrugId);
			if (Drugs.All(item => item.Id != manual.Id))
			{
				Drugs.Add(manual);
			}

			NewItemDrug = manual;
			return manual;
		}

		return null;
	}

	private void RequestFocus(string target)
	{
		FocusRequest = string.Empty;
		FocusRequest = target;
	}

	/// <summary>F4 — focus medicine search in the Add Item panel (opens panel if needed).</summary>
	public void RequestFocusMedicineSearch()
	{
		if (!IsAddItemOpen)
		{
			IsAddItemOpen = true;
		}
		RequestFocus("NewItemMedicine");
	}

	public void BeginNewEntry()
	{
		EnsureLinePaging();
		_dateFilterActive = false;
		_filterFromDate = null;
		_filterToDate = null;
		OnPropertyChanged(nameof(FilterFromDate));
		OnPropertyChanged(nameof(FilterToDate));
		foreach (PurchaseLineDraft line in Lines.ToList())
		{
			line.PropertyChanged -= OnLineChanged;
		}

		Lines.Clear();
		SelectedLine = null;
		InvoiceNo = string.Empty;
		InvoiceDate = DateTime.Today;
		Remarks = string.Empty;
		InvoiceImagePath = null;
		OriginalInvoiceFileName = null;
		IsPdfAttachment = false;
		InvoiceThumbnail = null;
		InvoiceOcrText = string.Empty;
		StagedItems.Clear();
		OnPropertyChanged(nameof(HasStagedItems));
		_importedExpectedGrandTotal = null;
		LoadedDocumentCount = 0;
		ClearNewItem();
		IsAddItemOpen = true;
		ErrorMessage = string.Empty;
		StatusMessage = "New purchase entry.";
		NotifyTotals();
		RequestFocus("NewItemMedicine");
	}

	public void NotifyHardwareStatus(string status)
	{
		StatusMessage = status;
	}

	public async Task ApplyScannedBarcodeAsync(string raw)
	{
		Gs1Scan scan = Gs1Scan.Parse(raw);
		string code = FirstCode(scan);
		if (code.Length < 3)
		{
			return;
		}

		try
		{
			using IServiceScope scope = scopeFactory.CreateScope();
			PharmaBillDbContext context = scope.ServiceProvider.GetRequiredService<PharmaBillDbContext>();
			string[] keys = scan.LookupKeys().ToArray();
			Drug? drug = keys.Length == 0
				? null
				: await context.Drugs.AsNoTracking().FirstOrDefaultAsync(item => item.IsActive && item.Barcode != null && keys.Contains(item.Barcode));
			Batch? batch = null;
			if (drug == null)
			{
				batch = await context.Batches.AsNoTracking().FirstOrDefaultAsync(item => item.BatchNo == code || (scan.Batch != null && item.BatchNo == scan.Batch));
				if (batch != null)
				{
					drug = await context.Drugs.AsNoTracking().FirstOrDefaultAsync(item => item.Id == batch.DrugId && item.IsActive);
				}
			}
			else if (!string.IsNullOrWhiteSpace(scan.Batch))
			{
				batch = await context.Batches.AsNoTracking().FirstOrDefaultAsync(item => item.DrugId == drug.Id && item.BatchNo == scan.Batch);
			}

			if (drug == null)
			{
				StatusMessage = "Barcode " + code + " is not in the medicine master.";
				CustomMedicineResult? saved = await quickAdd.ShowAsync(null, null, "Scanned barcode " + code + " was not found. Save it as a new medicine.", code);
				if (saved == null)
				{
					return;
				}

				drug = await context.Drugs.AsNoTracking().FirstAsync(item => item.Id == saved.DrugId);
			}

			string batchNo = scan.Batch ?? batch?.BatchNo ?? string.Empty;
			PurchaseLineDraft? existing = Lines.FirstOrDefault(line => line.Drug?.Id == drug.Id && (batchNo.Length == 0 || string.Equals(line.BatchNo, batchNo, StringComparison.OrdinalIgnoreCase)));
			if (existing != null)
			{
				SelectedLine = existing;
				StatusMessage = drug.Name + " is already on this purchase. Adjust the quantity.";
				return;
			}

			IsAddItemOpen = true;
			ShowManualDrugFields = false;
			_suppressMedicineSearchSync = true;
			try
			{
				MedicineSearchText = drug.Name;
			}
			finally
			{
				_suppressMedicineSearchSync = false;
			}

			NewItemDrug = drug;
			NewItemBatch = batchNo;
			DateOnly? expiry = scan.Expiry ?? batch?.ExpiryDate;
			NewItemExpiry = expiry.HasValue ? expiry.Value.ToString("MM/yyyy", CultureInfo.InvariantCulture) : NewItemExpiry;
			if (string.IsNullOrWhiteSpace(NewItemQuantity))
			{
				NewItemQuantity = "1";
			}

			if (drug.Mrp is > 0m && string.IsNullOrWhiteSpace(NewItemMrp))
			{
				NewItemMrp = FormatAmount(drug.Mrp.Value);
			}

			if (drug.GstRate is > 0m && string.IsNullOrWhiteSpace(NewItemGst))
			{
				NewItemGst = FormatAmount(drug.GstRate.Value);
			}

			ErrorMessage = string.Empty;
			bool matrix = !string.IsNullOrWhiteSpace(scan.Gtin) && (!string.IsNullOrWhiteSpace(scan.Batch) || scan.Expiry.HasValue);
			if (matrix)
			{
				StatusMessage = drug.Name + " scanned from a 2D code. Batch " + NewItemBatch + " and expiry " + NewItemExpiry + " are filled. Check the quantity and rate.";
				RequestFocus(string.IsNullOrWhiteSpace(NewItemQuantity) ? "NewItemQuantity" : "NewItemRate");
			}
			else
			{
				StatusMessage = drug.Name + " scanned. Enter the batch and quantity.";
				RequestFocus(string.IsNullOrWhiteSpace(NewItemBatch) ? "NewItemBatch" : "NewItemQuantity");
			}
		}
		catch (Exception ex)
		{
			ErrorMessage = ex.Message;
		}
	}

	private async Task ScanViaPhoneAsync()
	{
		_phoneBridge ??= new PurchasePhoneBridge();
		_phoneBridge.BillReceived -= OnPhoneBillReceived;
		_phoneBridge.BillReceived += OnPhoneBillReceived;
		_phoneBridge.StatusChanged -= OnPhoneStatusChanged;
		_phoneBridge.StatusChanged += OnPhoneStatusChanged;
		await _phoneBridge.StartAsync();
		IsPhoneListening = _phoneBridge.IsListening;
		PhoneListenStatus = _phoneBridge.StatusText;
		PhonePairingQr = QrCodeImage.FromText(_phoneBridge.BuildQrPayload(), 520);
		if (!_phoneBridge.IsListening)
		{
			ErrorMessage = PhoneListenStatus;
			return;
		}

		StatusMessage = PhoneListenStatus;
		PhoneBillPairingWindow window = new PhoneBillPairingWindow
		{
			DataContext = this
		};
		Window? owner = Application.Current?.MainWindow;
		if (owner != null && owner.IsLoaded)
		{
			window.Owner = owner;
		}

		// Modal pumps the dispatcher so BillReceived UI updates appear while the QR is open.
		window.ShowDialog();
		IsPhoneListening = _phoneBridge.IsListening;
		// Prefer live extraction status over a stale bridge banner left on "Processing extraction...".
		if (IsExtractingPhoneBill)
		{
			PhoneListenStatus = "Extracting items from a phone bill via AI...";
		}
		else if (_phoneBridge.IsListening)
		{
			string bridge = _phoneBridge.StatusText ?? string.Empty;
			if (bridge.Contains("Processing extraction", StringComparison.OrdinalIgnoreCase)
				|| bridge.Contains("extracting items", StringComparison.OrdinalIgnoreCase))
			{
				_phoneBridge.ReportExtractionStatus(
					PhoneBills.Count > 0
						? PhoneBills.Count + " phone bill(s) on this screen. Listening for more..."
						: string.Empty);
			}

			PhoneListenStatus = _phoneBridge.StatusText;
		}
		else
		{
			PhoneListenStatus = _phoneBridge.StatusText;
		}
	}

	private void OnPhoneStatusChanged(string status)
	{
		Dispatch(() =>
		{
			// Do not clobber an in-progress / completed extraction banner with an older bridge ping.
			if (IsExtractingPhoneBill
				&& !string.IsNullOrWhiteSpace(status)
				&& status.Contains("Listening at", StringComparison.OrdinalIgnoreCase))
			{
				IsPhoneListening = _phoneBridge?.IsListening == true;
				return;
			}

			PhoneListenStatus = status;
			IsPhoneListening = _phoneBridge?.IsListening == true;
		});
	}

	private void OnPhoneBillReceived(PhonePurchaseUpload upload)
	{
		Dispatch(() =>
		{
			PhoneListenStatus = "Extracting items from Bill via AI...";
			StatusMessage = PhoneListenStatus;
			_ = AcceptPhoneBillAsync(upload);
		});
	}

	private static void Dispatch(Action action)
	{
		System.Windows.Threading.Dispatcher? dispatcher = Application.Current?.Dispatcher;
		if (dispatcher == null || dispatcher.CheckAccess())
		{
			action();
			return;
		}

		// Synchronous Invoke so the Purchases table refreshes immediately on the UI thread.
		dispatcher.Invoke(action);
	}

	private static Task DispatchAsync(Action action)
	{
		System.Windows.Threading.Dispatcher? dispatcher = Application.Current?.Dispatcher;
		if (dispatcher == null || dispatcher.CheckAccess())
		{
			action();
			return Task.CompletedTask;
		}

		return dispatcher.InvokeAsync(action).Task;
	}

	private async Task AcceptPhoneBillAsync(PhonePurchaseUpload upload)
	{
		PhoneIncomingBill bill = new PhoneIncomingBill
		{
			Index = ++_phoneBillSequence,
			SupplierName = upload.Supplier?.Trim() ?? string.Empty,
			InvoiceNo = upload.InvoiceNo?.Trim() ?? string.Empty,
			OriginalFileName = upload.FileName,
			FilePath = upload.SavedFilePath,
			Status = "Extracting"
		};
		if (DateOnly.TryParse(upload.InvoiceDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly parsed)
			|| DateOnly.TryParse(upload.InvoiceDate, CultureInfo.CurrentCulture, DateTimeStyles.None, out parsed))
		{
			bill.InvoiceDate = parsed.ToDateTime(TimeOnly.MinValue);
		}

		Dispatch(() =>
		{
			PhoneBills.Add(bill);
			OnPropertyChanged(nameof(HasPhoneBills));
			IsExtractingPhoneBill = true;
			SelectedPhoneBill = bill;
			string extracting = "Extracting items from Bill " + bill.Index + " via AI...";
			PhoneListenStatus = extracting;
			StatusMessage = extracting;
			ErrorMessage = string.Empty;
			_phoneBridge?.ReportExtractionStatus(extracting);
		});

		try
		{
			if (!string.IsNullOrWhiteSpace(upload.SavedFilePath) && File.Exists(upload.SavedFilePath))
			{
				Debug.WriteLine($"Phone bill: image upload selected for PC extraction; Android supplied {upload.Items.Count} parsed item(s). File='{upload.SavedFilePath}'.");
				string workingPath = await InvoiceImageNormalizer.EnsureSupportedAsync(upload.SavedFilePath).ConfigureAwait(false);
				bill.FilePath = workingPath;
				using IServiceScope scope = scopeFactory.CreateScope();
				InvoiceDocumentExtractor extractor = scope.ServiceProvider.GetRequiredService<InvoiceDocumentExtractor>();
				ExtractedPurchaseInvoice extracted;
				try
				{
					// Gemini vision first (reads multi-column tables); falls back to local OCR inside ExtractAsync.
					extracted = await extractor.ExtractAsync(workingPath).ConfigureAwait(false);
				}
				catch (Exception remoteError) when (remoteError is not OperationCanceledException)
				{
					System.Diagnostics.Debug.WriteLine("Phone bill extract failed, forcing local OCR: " + remoteError);
					Dispatch(() =>
					{
						StatusMessage = "Online AI failed (" + remoteError.Message + "). Trying offline OCR...";
						PhoneListenStatus = StatusMessage;
					});
					extracted = await extractor.ExtractLocalAsync(workingPath).ConfigureAwait(false);
				}

				Debug.WriteLine($"Phone bill: extracted {extracted.Items.Count} row(s) via PC extractor.");
				foreach (ExtractedPurchaseLine item in extracted.Items)
				{
					Debug.WriteLine($"Phone bill row: Name='{item.ItemName}'; Batch='{item.Batch}'; Expiry='{item.Expiry}'; Qty={item.Quantity}; Rate={item.Rate}; Amount={item.Amount}; Warning='{item.ValidationWarning}'.");
				}
				ApplyExtractedPhoneBill(bill, extracted);
			}
			else if (upload.Items.Count > 0)
			{
				Debug.WriteLine($"Phone bill: no image attachment; using {upload.Items.Count} Android-supplied parsed item(s).");
				foreach (PhonePurchaseLineUpload item in upload.Items)
				{
					Debug.WriteLine($"Phone Android row: Name='{item.Name}'; Batch='{item.Batch}'; Expiry='{item.Expiry}'; Qty={item.Quantity}; Rate={item.Rate}; MRP={item.Mrp}; GST={item.Gst}.");
					bill.Lines.Add(ToSnapshot(item.Name, item.Batch, item.Expiry, item.Quantity, item.Free, item.Mrp, item.Rate, item.Gst));
				}
			}
			else
			{
				throw new InvalidOperationException("The phone bill had no image attachment and no line items to import.");
			}

			bill.NotifyLinesChanged();
			await DispatchAsync(() => FinishPhoneBill(bill)).ConfigureAwait(false);
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine("Phone bill extraction failed: " + ex.ToString());
			await DispatchAsync(() =>
			{
				IsExtractingPhoneBill = false;
				string failure = "Extraction failed: " + ex.Message;
				ErrorMessage = failure;
				bill.Status = bill.Lines.Count > 0 ? "Received" : "Needs review";
				bill.NotifyLinesChanged();
				PhoneListenStatus = failure;
				StatusMessage = failure;
				_phoneBridge?.ReportExtractionStatus(
					bill.Lines.Count > 0
						? "Bill " + bill.Index + " partial (" + bill.Lines.Count + " lines). " + failure
						: failure + " Listening for more...");
				LoadPhoneBill(bill);
			}).ConfigureAwait(false);
		}
	}

	private void ApplyExtractedPhoneBill(PhoneIncomingBill bill, ExtractedPurchaseInvoice extracted)
	{
		if (string.IsNullOrWhiteSpace(bill.SupplierName)
			|| GeminiPurchaseImportService.LooksLikeFileOrDeviceId(bill.SupplierName))
		{
			bill.SupplierName = GeminiPurchaseImportService.SanitizeSupplierName(extracted.Supplier);
		}

		if (string.IsNullOrWhiteSpace(bill.InvoiceNo)
			|| GeminiPurchaseImportService.LooksLikeFileOrDeviceId(bill.InvoiceNo))
		{
			bill.InvoiceNo = GeminiPurchaseImportService.SanitizeInvoiceNo(extracted.InvoiceNo);
		}

		if (bill.InvoiceDate.Date == DateTime.Today && DateOnly.TryParse(extracted.InvoiceDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly extractedDate))
		{
			bill.InvoiceDate = extractedDate.ToDateTime(TimeOnly.MinValue);
		}

		// Replace prior partial lines so a re-extract never appends duplicates.
		bill.Lines.Clear();
		foreach (ExtractedPurchaseLine item in extracted.Items)
		{
			if (string.IsNullOrWhiteSpace(item.ItemName) || InvoiceTextParser.IsNonMedicineNoise(item.ItemName))
			{
				continue;
			}

			// Keep every extracted medicine row — do not drop for missing batch/expiry/rate.
			// Reject strength-only fragments like "5m 125 mg" unless a real product form/name is present.
			if (!InvoiceTextParser.LooksLikeMedicineProduct(item.ItemName)
				&& !InvoiceTextParser.HasPlausibleBatchToken(item.Batch)
				&& item.ItemName.Count(char.IsLetter) < 8)
			{
				continue;
			}

			string batch = item.Batch;
			if (string.IsNullOrWhiteSpace(batch)
				|| string.Equals(batch, "BATCH", StringComparison.OrdinalIgnoreCase)
				|| InvoiceTextParser.IsNonMedicineNoise(batch))
			{
				batch = "NA";
			}

			bill.Lines.Add(ToSnapshot(item.ItemName, batch, item.Expiry, item.Quantity, item.Free, item.Mrp, item.Rate, item.Gst));
		}

		bill.NotifyLinesChanged();
	}

	private void FinishPhoneBill(PhoneIncomingBill bill)
	{
		IsExtractingPhoneBill = false;
		bill.Status = bill.Lines.Count > 0 ? "Received" : "Needs review";
		bill.NotifyLinesChanged();
		OnPropertyChanged(nameof(HasPhoneBills));
		string done = bill.Lines.Count > 0
			? "Bill " + bill.Index + ": Successfully extracted " + bill.Lines.Count + " items."
			: "Bill " + bill.Index + ": extraction finished with 0 items. Re-send a clearer photo or enter lines manually.";
		PhoneListenStatus = done + " Listening for more...";
		StatusMessage = done;
		ErrorMessage = bill.Lines.Count == 0 ? done : string.Empty;
		_phoneBridge?.ReportExtractionStatus(PhoneListenStatus);

		// Always put the newest bill on the grid so items appear without a manual chip click.
		LoadPhoneBill(bill);
	}

	private PhoneBillLineSnapshot ToSnapshot(string? name, string? batch, string? expiry, decimal quantity, decimal free, decimal mrp, decimal rate, decimal gst)
	{
		string expiryText = expiry ?? string.Empty;
		if (AddStockViewModel.TryParseExpiry(FormatExpiryInput(expiryText), out DateOnly lastDay) || DateOnly.TryParse(expiryText, CultureInfo.InvariantCulture, DateTimeStyles.None, out lastDay))
		{
			expiryText = lastDay.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
		}

		Drug? drug = string.IsNullOrWhiteSpace(name) ? null : MatchDrug(name);
		return new PhoneBillLineSnapshot
		{
			MedicineName = string.IsNullOrWhiteSpace(name) ? drug?.Name ?? string.Empty : name.Trim(),
			DrugId = drug?.Id,
			Batch = batch?.Trim() ?? string.Empty,
			Expiry = expiryText,
			Quantity = quantity,
			Free = free,
			Mrp = mrp,
			Rate = rate,
			Gst = StandardGstSlab(gst)
		};
	}

	private void LoadPhoneBill(PhoneIncomingBill? bill)
	{
		if (bill == null || bill.IsCommitted)
		{
			return;
		}

		// Still extracting and nothing parsed yet — keep waiting; chip stays on Extracting...
		if (bill.IsExtracting && bill.Lines.Count == 0)
		{
			StatusMessage = "Extracting items from Bill " + bill.Index + " via AI...";
			PhoneListenStatus = StatusMessage;
			return;
		}

		// Snapshot before any UI mutation so RememberLoadedPhoneBill cannot wipe fresh OCR rows.
		List<PhoneBillLineSnapshot> snapshots = bill.Lines.ToList();

		void ApplyToGrid()
		{
			// Persist edits on a *different* previously loaded bill only — never overwrite the bill we are loading.
			if (_loadedPhoneBill != null && !ReferenceEquals(_loadedPhoneBill, bill))
			{
				RememberLoadedPhoneBill();
			}

			// Show every extracted row (date filter would hide OCR lines with mismatched inward dates).
			_dateFilterActive = false;
			FilterFromDate = null;
			FilterToDate = null;
			OnPropertyChanged(nameof(FilterFromDate));
			OnPropertyChanged(nameof(FilterToDate));
			OnPropertyChanged(nameof(ShowDateFilterEmpty));

			EnsureLinePaging();
			_pagingSuspend++;
			try
			{
				foreach (PurchaseLineDraft existing in Lines.ToList())
				{
					existing.PropertyChanged -= OnLineChanged;
				}

				Lines.Clear();
				PagedLines.Clear();
				foreach (PhoneBillLineSnapshot snapshot in snapshots)
				{
					Drug? drug = snapshot.DrugId.HasValue
						? FindDrug(snapshot.DrugId.Value)
						: MatchDrug(snapshot.MedicineName);
					PurchaseLineDraft line = new PurchaseLineDraft(drug)
					{
						MedicineName = snapshot.MedicineName,
						BatchNo = snapshot.Batch,
						Expiry = snapshot.Expiry,
						Quantity = snapshot.Quantity > 0m ? snapshot.Quantity : 1m,
						FreeQuantity = snapshot.Free,
						Mrp = snapshot.Mrp,
						Rate = snapshot.Rate,
						GstRate = snapshot.Gst,
						DiscountAmount = snapshot.Discount,
						InwardDate = bill.InvoiceDate.Date == default ? DateTime.Today : bill.InvoiceDate.Date,
						AcceptMedicineEdits = true
					};
					line.PropertyChanged += OnLineChanged;
					Lines.Add(line);
				}
			}
			finally
			{
				_pagingSuspend = Math.Max(0, _pagingSuspend - 1);
				RefreshPaging(resetToFirst: true);
			}

			_loadedPhoneBill = bill;
			SelectedPhoneBill = bill;
			if (!bill.IsExtracting)
			{
				bill.Status = "Loaded";
			}

			bill.NotifyLinesChanged();
			if (!string.IsNullOrWhiteSpace(bill.InvoiceNo))
			{
				InvoiceNo = bill.InvoiceNo;
			}

			InvoiceDate = bill.InvoiceDate == default ? DateTime.Today : bill.InvoiceDate;
			_pendingSupplierName = bill.SupplierName;
			_ = EnsureExtractedSupplierAsync(bill.SupplierName);
			if (!string.IsNullOrWhiteSpace(bill.FilePath))
			{
				_ = TryAttachInvoicePreview(bill.FilePath);
			}

			NotifyTotals();
			string loaded = snapshots.Count > 0
				? "Bill " + bill.Index + ": Successfully extracted " + snapshots.Count + " items."
				: "Bill " + bill.Index + " has no extracted lines yet. Add items manually or re-send a clearer photo.";
			StatusMessage = loaded;
			if (!IsExtractingPhoneBill)
			{
				PhoneListenStatus = loaded + (_phoneBridge?.IsListening == true ? " Listening for more..." : string.Empty);
			}
		}

		Dispatch(ApplyToGrid);
	}

	private void RememberLoadedPhoneBill()
	{
		if (_loadedPhoneBill == null || _loadedPhoneBill.IsCommitted)
		{
			return;
		}

		// Never wipe already-parsed phone lines with an empty grid (failed bind / mid-reload).
		if (Lines.Count == 0 && _loadedPhoneBill.Lines.Count > 0)
		{
			return;
		}

		_loadedPhoneBill.Lines.Clear();
		foreach (PurchaseLineDraft line in Lines)
		{
			_loadedPhoneBill.Lines.Add(new PhoneBillLineSnapshot
			{
				MedicineName = line.MedicineName,
				DrugId = line.Drug?.Id,
				Batch = line.BatchNo,
				Expiry = line.Expiry,
				Quantity = line.Quantity,
				Free = line.FreeQuantity,
				Mrp = line.Mrp,
				Rate = line.Rate,
				Gst = line.GstRate,
				Discount = line.DiscountAmount
			});
		}

		_loadedPhoneBill.InvoiceNo = InvoiceNo;
		if (SelectedSupplier != null)
		{
			_loadedPhoneBill.SupplierName = SelectedSupplier.Name;
		}

		_loadedPhoneBill.InvoiceDate = InvoiceDate;
		if (string.Equals(_loadedPhoneBill.Status, "Loaded", StringComparison.Ordinal))
		{
			_loadedPhoneBill.Status = "Received";
		}
	}

	private async Task CommitAllPhoneBillsAsync()
	{
		ErrorMessage = string.Empty;
		RememberLoadedPhoneBill();
		int committed = 0;
		foreach (PhoneIncomingBill bill in PhoneBills.Where((PhoneIncomingBill item) => !item.IsCommitted).ToList())
		{
			if (!await CommitPhoneBillAsync(bill))
			{
				StatusMessage = committed + " phone bill(s) inwarded. " + ErrorMessage;
				return;
			}

			committed++;
		}

		Lines.Clear();
		_loadedPhoneBill = null;
		SelectedPhoneBill = null;
		NotifyTotals();
		StatusMessage = "Success! " + committed + " phone bill(s) have been added to inventory.";
		confirmationService.Notify("Success", StatusMessage);
		if (PurchasePosted != null)
		{
			await PurchasePosted.Invoke();
		}
	}

	private async Task<bool> CommitPhoneBillAsync(PhoneIncomingBill bill)
	{
		if (currentSession.User == null)
		{
			ErrorMessage = "Sign in before transferring stock.";
			return false;
		}

		if (bill.Lines.Count == 0)
		{
			ErrorMessage = "Bill " + bill.Index + " has no medicine lines to inward.";
			return false;
		}

		if (string.IsNullOrWhiteSpace(bill.InvoiceNo))
		{
			bill.InvoiceNo = "PHONE-" + bill.Index + "-" + DateTime.Now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
		}

		_pendingSupplierName = bill.SupplierName;
		SelectedSupplier = Suppliers.FirstOrDefault((Supplier supplier) => string.Equals(supplier.Name.Trim(), bill.SupplierName.Trim(), StringComparison.OrdinalIgnoreCase));
		await EnsureExtractedSupplierAsync(bill.SupplierName);
		if (SelectedSupplier == null)
		{
			ErrorMessage = "Select or add the supplier for bill " + bill.Index + " before inwarding.";
			return false;
		}

		using IServiceScope scope = scopeFactory.CreateScope();
		CustomMedicineService medicines = scope.ServiceProvider.GetRequiredService<CustomMedicineService>();
		PharmaBillDbContext context = scope.ServiceProvider.GetRequiredService<PharmaBillDbContext>();
		PurchaseService purchases = scope.ServiceProvider.GetRequiredService<PurchaseService>();
		List<PurchaseLineInput> inputs = new List<PurchaseLineInput>();
		decimal subtotal = 0m;
		decimal tax = 0m;
		foreach (PhoneBillLineSnapshot line in bill.Lines)
		{
			Drug? drug = line.DrugId.HasValue ? await context.Drugs.FirstOrDefaultAsync((Drug item) => item.Id == line.DrugId.Value) : null;
			if (drug == null && !string.IsNullOrWhiteSpace(line.MedicineName))
			{
				drug = MatchDrug(line.MedicineName);
			}

			if (drug == null && !string.IsNullOrWhiteSpace(line.MedicineName))
			{
				CustomMedicineResult created = await medicines.SaveAsync(new CustomMedicineRequest(line.MedicineName.Trim(), null, null, null, "3004", line.Gst, line.Mrp > 0m ? line.Mrp : null, line.Rate), currentSession.User.Id);
				drug = await context.Drugs.FirstOrDefaultAsync((Drug item) => item.Id == created.DrugId);
				if (drug != null && Drugs.All((Drug item) => item.Id != drug.Id))
				{
					Drugs.Add(drug);
				}

				line.DrugId = drug?.Id;
			}

			if (drug == null || string.IsNullOrWhiteSpace(line.Batch) || line.Quantity <= 0m)
			{
				ErrorMessage = "Bill " + bill.Index + " still needs a medicine, batch, and quantity on every line.";
				return false;
			}

			if (!DateOnly.TryParse(line.Expiry, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly expiry)
				&& !DateOnly.TryParse(line.Expiry, CultureInfo.CurrentCulture, DateTimeStyles.None, out expiry))
			{
				ErrorMessage = "Bill " + bill.Index + ": enter a valid expiry for " + line.MedicineName + ".";
				return false;
			}

			decimal amount = decimal.Round(line.Quantity * line.Rate - line.Discount, 2, MidpointRounding.AwayFromZero);
			decimal lineTax = decimal.Round(amount * line.Gst / 100m, 2, MidpointRounding.AwayFromZero);
			subtotal += amount;
			tax += lineTax;
			inputs.Add(new PurchaseLineInput(drug.Id, line.Batch, expiry, line.Quantity, line.Free, line.Mrp, line.Rate, line.Gst, line.Discount, amount, null));
		}

		if (await purchases.IsDuplicateInvoiceAsync(SelectedSupplier.Id, bill.InvoiceNo))
		{
			ErrorMessage = "Bill " + bill.Index + " is already saved for this supplier (" + bill.InvoiceNo + ").";
			return false;
		}

		string? attached = bill.FilePath;
		if (!string.IsNullOrWhiteSpace(attached) && File.Exists(attached))
		{
			attached = PurchaseAttachmentStore.Copy(attached, bill.InvoiceDate.Year, SelectedSupplier.Id);
			bill.FilePath = attached;
		}

		DateOnly invoiceDate = DateOnly.FromDateTime(bill.InvoiceDate);
		await purchases.SavePurchaseAsync(new SavePurchaseInput(SelectedSupplier.Id, bill.InvoiceNo, invoiceDate, subtotal, 0m, tax, subtotal + tax, inputs, Remarks, SelectedStorageLocation?.Id, attached, bill.OriginalFileName), currentSession.User.Id, currentSession.User.Role);
		bill.Status = "Committed";
		return true;
	}

	private async Task ScanPurchaseWithHandheldScannerAsync()
	{
		ErrorMessage = string.Empty;
		try
		{
			HardwareScannerService scanner = new HardwareScannerService();
			string? path = await scanner.CaptureFromHardwareScannerAsync();
			if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
			{
				StatusMessage = scanner.HasWiaScanner()
					? "Scanner capture cancelled."
					: "No WIA scanner found. Connect a USB document scanner or use Scan with Webcam.";
				return;
			}

			StatusMessage = "Reading the scanned invoice...";
			bool extracted = await StageExtractedFilesAsync(new[] { path });
			if (!extracted)
			{
				await TryAttachInvoicePreview(path);
				StatusMessage = "Invoice page scanned. No medicine lines were extracted — attach is ready for a manual review.";
			}
		}
		catch (Exception ex)
		{
			ErrorMessage = ex.Message;
		}
	}

	private async Task ScanInvoiceAsync()
	{
		await StoreInvoiceImageAsync(prescriptionDialogs.CaptureFromWebcam());
	}

	private async Task AttachInvoiceFileAsync()
	{
		await StoreInvoiceImageAsync(filePicker.PickPurchaseDocument());
	}

	public async Task<string?> AttachExistingDocumentAsync(string? source)
	{
		if (string.IsNullOrWhiteSpace(source) || !File.Exists(source))
		{
			return null;
		}

		try
		{
			string destination = PurchaseAttachmentStore.Copy(source, InvoiceDate.Year, SelectedSupplier?.Id ?? Guid.Empty);
			OriginalInvoiceFileName = Path.GetFileName(source);
			InvoiceImagePath = destination;
			await LoadInvoicePreviewAsync(destination);
			if (PurchaseAttachmentStore.IsPdf(destination))
			{
				InvoiceOcrText = string.Empty;
				StatusMessage = "PDF document attached. Open File launches it in the Windows viewer. It is saved with this purchase.";
			}
			else
			{
				StatusMessage = "Invoice image attached.";
				await ExtractInvoiceTextAsync();
			}

			return destination;
		}
		catch (Exception ex)
		{
			ErrorMessage = ex.Message;
			return null;
		}
	}

	private async Task StoreInvoiceImageAsync(string? source)
	{
		await AttachExistingDocumentAsync(source);
	}

	private async Task LoadInvoicePreviewAsync(string path)
	{
		IsPdfAttachment = PurchaseAttachmentStore.IsPdf(path);
		if (!IsPdfAttachment)
		{
			try
			{
				InvoiceThumbnail = PurchaseAttachmentStore.LoadImage(path, 360);
			}
			catch (Exception ex)
			{
				InvoiceThumbnail = null;
				ErrorMessage = ex.Message;
			}

			return;
		}

		InvoiceThumbnail = await PurchaseAttachmentStore.RenderPdfFirstPageAsync(path);
	}

	private void ViewInvoiceImage()
	{
		if (string.IsNullOrWhiteSpace(InvoiceImagePath) || !File.Exists(InvoiceImagePath))
		{
			return;
		}

		Process.Start(new ProcessStartInfo(InvoiceImagePath) { UseShellExecute = true });
	}

	private async Task ExtractInvoiceTextAsync()
	{
		if (string.IsNullOrWhiteSpace(InvoiceImagePath) || !File.Exists(InvoiceImagePath) || PurchaseAttachmentStore.IsPdf(InvoiceImagePath))
		{
			return;
		}

		try
		{
			byte[] bytes = await File.ReadAllBytesAsync(InvoiceImagePath);
			string text = await ocr.RecognizeAsync(bytes);
			InvoiceOcrText = text?.Trim() ?? string.Empty;
			if (string.IsNullOrWhiteSpace(InvoiceNo))
			{
				string? guessed = GuessInvoiceNumber(InvoiceOcrText);
				if (!string.IsNullOrWhiteSpace(guessed))
				{
					InvoiceNo = guessed;
				}
			}

			StatusMessage = string.IsNullOrWhiteSpace(InvoiceOcrText)
				? "Invoice image attached. No text was recognized."
				: "Invoice image attached. Recognized text is shown under the preview.";
		}
		catch (Exception ex)
		{
			InvoiceOcrText = string.Empty;
			StatusMessage = "Invoice image attached. Text recognition is unavailable: " + ex.Message;
		}
	}

	private static string FirstCode(Gs1Scan scan)
	{
		if (!string.IsNullOrWhiteSpace(scan.Gtin))
		{
			return scan.Gtin.Trim();
		}

		return scan.Raw.Trim();
	}

	private static string? GuessInvoiceNumber(string text)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return null;
		}

		foreach (string line in text.Split('\n', '\r'))
		{
			string trimmed = line.Trim();
			if (trimmed.Length < 3 || trimmed.Length > 30)
			{
				continue;
			}

			if (trimmed.Contains("invoice", StringComparison.OrdinalIgnoreCase) || trimmed.Contains("inv", StringComparison.OrdinalIgnoreCase))
			{
				string digits = new string(trimmed.Where(ch => char.IsLetterOrDigit(ch) || ch is '-' or '/').ToArray());
				int split = digits.LastIndexOfAny(new[] { ' ', ':' });
				string candidate = split >= 0 ? digits[(split + 1)..] : digits;
				if (candidate.Length >= 3 && candidate.Any(char.IsDigit))
				{
					return candidate;
				}
			}
		}

		return null;
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
		EnsureLinePaging();
		if (line.InwardDate == default)
		{
			line.InwardDate = _invoiceDate.Date;
		}
		line.PropertyChanged += OnLineChanged;
		line.AcceptMedicineEdits = true;
		Lines.Add(line);
		NotifyTotals();
	}

	private void EnsureLinePaging()
	{
		if (_linePagingHooked)
		{
			return;
		}

		_linePagingHooked = true;
		Lines.CollectionChanged += OnLinesCollectionChanged;
		RefreshPaging(resetToFirst: true);
	}

	private void OnLinesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
	{
		if (e.NewItems != null)
		{
			foreach (PurchaseLineDraft line in e.NewItems.OfType<PurchaseLineDraft>())
			{
				if (line.InwardDate == default)
				{
					line.InwardDate = _invoiceDate.Date;
				}
			}
		}

		if (_pagingSuspend > 0)
		{
			return;
		}

		if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems?.Count == 1)
		{
			_jumpToLine = e.NewItems[0] as PurchaseLineDraft;
		}

		RefreshPaging();
	}

	private void GoToPreviousPage()
	{
		if (!CanGoPrevious)
		{
			return;
		}

		_currentPage--;
		RefreshPaging();
	}

	private void GoToNextPage()
	{
		if (!CanGoNext)
		{
			return;
		}

		_currentPage++;
		RefreshPaging();
	}

	private void ApplyDateFilter()
	{
		if (FilterFromDate.HasValue && FilterToDate.HasValue && FilterFromDate.Value.Date > FilterToDate.Value.Date)
		{
			ErrorMessage = "From Date must be on or before To Date.";
			return;
		}

		_dateFilterActive = FilterFromDate.HasValue || FilterToDate.HasValue;
		ErrorMessage = string.Empty;
		RefreshPaging(resetToFirst: true);
		StatusMessage = _dateFilterActive
			? $"Showing inward lines from {FormatFilterDate(FilterFromDate)} to {FormatFilterDate(FilterToDate)}."
			: "Showing every inward line.";
	}

	private void ClearDateFilter()
	{
		_dateFilterActive = false;
		FilterFromDate = null;
		FilterToDate = null;
		ErrorMessage = string.Empty;
		StatusMessage = "Showing every inward line.";
		RefreshPaging(resetToFirst: true);
	}

	private static string FormatFilterDate(DateTime? value)
	{
		return value.HasValue ? value.Value.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture) : "any date";
	}

	private bool MatchesDateFilter(PurchaseLineDraft line)
	{
		if (!_dateFilterActive)
		{
			return true;
		}

		DateTime inward = line.InwardDate == default ? _invoiceDate.Date : line.InwardDate.Date;
		if (IsInsideFilter(inward))
		{
			return true;
		}

		return TryParseFilterDate(line.Expiry, out DateTime expiry) && IsInsideFilter(expiry);
	}

	private bool IsInsideFilter(DateTime value)
	{
		if (FilterFromDate.HasValue && value.Date < FilterFromDate.Value.Date)
		{
			return false;
		}

		if (FilterToDate.HasValue && value.Date > FilterToDate.Value.Date)
		{
			return false;
		}

		return true;
	}

	private static bool TryParseFilterDate(string text, out DateTime value)
	{
		value = default;
		if (string.IsNullOrWhiteSpace(text))
		{
			return false;
		}

		if (DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.None, out value))
		{
			return true;
		}

		return DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out value);
	}

	private void RefreshPaging(bool resetToFirst = false)
	{
		List<PurchaseLineDraft> filtered = Lines.Where(MatchesDateFilter).ToList();
		if (_jumpToLine != null)
		{
			int index = filtered.IndexOf(_jumpToLine);
			if (index >= 0)
			{
				_currentPage = (index / PurchaseLinesPerPage) + 1;
			}

			_jumpToLine = null;
		}

		if (resetToFirst)
		{
			_currentPage = 1;
		}

		int pages = Math.Max(1, (int)Math.Ceiling(filtered.Count / (double)PurchaseLinesPerPage));
		if (_currentPage > pages)
		{
			_currentPage = pages;
		}

		if (_currentPage < 1)
		{
			_currentPage = 1;
		}

		PurchaseLineDraft? keep = SelectedLine;
		// Show every filtered inward line in the review grid (CSV/Excel imports are typically 6–50 rows).
		// Keep page math for the summary strip; do not clip the DataGrid to a single page viewport.
		List<PurchaseLineDraft> page = filtered;
		PagedLines.Clear();
		foreach (PurchaseLineDraft line in page)
		{
			PagedLines.Add(line);
		}

		if (keep != null && PagedLines.Contains(keep))
		{
			SelectedLine = keep;
		}

		OnPropertyChanged(nameof(CurrentPage));
		OnPropertyChanged(nameof(TotalItems));
		OnPropertyChanged(nameof(TotalPages));
		OnPropertyChanged(nameof(PageSummary));
		OnPropertyChanged(nameof(CanGoPrevious));
		OnPropertyChanged(nameof(CanGoNext));
		OnPropertyChanged(nameof(ShowDateFilterEmpty));
		_previousPageCommand?.NotifyCanExecuteChanged();
		_nextPageCommand?.NotifyCanExecuteChanged();
		Debug.WriteLine($"Purchase paging: PagedLines.Count = {PagedLines.Count}, Lines.Count = {Lines.Count}, current page = {_currentPage}, pages = {pages}, PurchaseLinesPerPage = {PurchaseLinesPerPage}, resetToFirst = {resetToFirst}.");
	}

	private void ClearNewItem()
	{
		_suppressMedicineSearchSync = true;
		try
		{
			SelectedMedicineItem = null;
			MedicineSearchText = string.Empty;
		}
		finally
		{
			_suppressMedicineSearchSync = false;
		}
		MedicinePickerItems.Clear();
		IsMedicineDropDownOpen = false;
		ShowManualDrugFields = false;
		NewItemDrug = null;
		NewItemBatch = string.Empty;
		NewItemExpiry = string.Empty;
		NewItemQuantity = string.Empty;
		NewItemFree = string.Empty;
		NewItemMrp = string.Empty;
		NewItemRate = string.Empty;
		NewItemGst = string.Empty;
		NewItemDiscountPercent = string.Empty;
		RateSuggestionHint = string.Empty;
		NewItemRack = string.Empty;
		NewItemFormulation = string.Empty;
		NewItemHsn = string.Empty;
		NewItemPackLabel = string.Empty;
		NewItemError = string.Empty;
		_defaultsEnrichCts?.Cancel();
	}

	private void NotifyTotals()
	{
		OnPropertyChanged(nameof(Subtotal));
		OnPropertyChanged(nameof(TaxTotal));
		OnPropertyChanged(nameof(GrandTotal));
		OnPropertyChanged(nameof(HasTotalMismatch));
		OnPropertyChanged(nameof(TotalMismatchNote));
	}

	private async Task TryAttachInvoicePreview(string path)
	{
		if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
		{
			return;
		}

		if (!string.IsNullOrWhiteSpace(InvoiceImagePath)
			&& string.Equals(Path.GetFullPath(InvoiceImagePath), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase))
		{
			await LoadInvoicePreviewAsync(path);
			return;
		}

		await AttachExistingDocumentAsync(path);
	}

	private void ShowInvoiceHistory()
	{
		using IServiceScope scope = scopeFactory.CreateScope();
		PurchaseInvoiceHistoryWindow window = scope.ServiceProvider.GetRequiredService<PurchaseInvoiceHistoryWindow>();
		window.Owner = System.Windows.Application.Current?.MainWindow;
		window.ShowDialog();
	}

	private void ApplyStagedItems()
	{
		if (StagedItems.Count == 0)
		{
			return;
		}

		int added = 0;
		_pagingSuspend++;
		try
		{
		foreach (StagedPurchaseLine staged in StagedItems.ToList())
		{
			string expiry = staged.Expiry;
			if (AddStockViewModel.TryParseExpiry(FormatExpiryInput(expiry), out DateOnly lastDay))
			{
				expiry = lastDay.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
			}

			PurchaseLineDraft line = new PurchaseLineDraft(staged.Drug)
			{
				MedicineName = string.IsNullOrWhiteSpace(staged.ItemName) ? staged.Drug?.Name ?? string.Empty : staged.ItemName.Trim(),
				BatchNo = staged.Batch?.Trim() ?? string.Empty,
				Expiry = expiry,
				Quantity = staged.Quantity,
				FreeQuantity = staged.Free,
				Mrp = staged.Mrp,
				Rate = staged.Rate,
				GstRate = staged.Gst,
				ValidationWarning = staged.ValidationWarning,
				DiscountAmount = decimal.Round(staged.Quantity * staged.Rate * staged.DiscountPercent / 100m, 2, MidpointRounding.AwayFromZero)
			};
			AddDraftLine(line);
			added++;
		}
		}
		finally
		{
			_pagingSuspend = Math.Max(0, _pagingSuspend - 1);
			RefreshPaging(resetToFirst: true);
		}

		Debug.WriteLine($"Purchase import: Lines.Count after ApplyStagedItems = {Lines.Count}; added in this operation = {added}; staged before clear = {StagedItems.Count}.");
		StagedItems.Clear();
		OnPropertyChanged(nameof(HasStagedItems));
		applyStagedItemsCommand?.NotifyCanExecuteChanged();
		discardStagedItemsCommand?.NotifyCanExecuteChanged();
		StatusMessage = added + " staged line(s) are on the purchase bill. Edit or delete any row before saving.";
		ErrorMessage = string.Empty;
	}

	private void DiscardStagedItems()
	{
		StagedItems.Clear();
		OnPropertyChanged(nameof(HasStagedItems));
		applyStagedItemsCommand?.NotifyCanExecuteChanged();
		discardStagedItemsCommand?.NotifyCanExecuteChanged();
		StatusMessage = "Staged import discarded. The purchase bill was not changed.";
	}

	public async Task<bool> ExtractInvoicesAsync()
	{
		IReadOnlyList<string> files = filePicker.PickInvoiceDocuments();
		if (files.Count == 0)
		{
			return false;
		}

		return await StageExtractedFilesAsync(files);
	}

	private async Task<bool> StageExtractedFilesAsync(IReadOnlyList<string> files)
	{
		IsImporting = true;
		ImportStatus = "Reading invoices...";
		BillTotalCheck = string.Empty;
		try
		{
			using IServiceScope scope = scopeFactory.CreateScope();
			InvoiceDocumentExtractor extractor = scope.ServiceProvider.GetRequiredService<InvoiceDocumentExtractor>();
			StagedItems.Clear();
			string? previewPath = null;
			ExtractedPurchaseInvoice? header = null;
			List<string> problems = new List<string>();
			foreach (string path in files)
			{
				try
				{
					ExtractedPurchaseInvoice extracted = await extractor.ExtractAsync(path);
					previewPath ??= path;
					header ??= extracted;
					StageExtractedItems(extracted);
				}
				catch (Exception ex)
				{
					problems.Add(Path.GetFileName(path) + ": " + FriendlyInvoiceReadError(ex));
				}
			}

			if (previewPath != null)
			{
				await TryAttachInvoicePreview(previewPath);
			}

			if (header != null)
			{
				_pendingSupplierName = header.Supplier;
				await EnsureExtractedSupplierAsync(header.Supplier);

				if (string.IsNullOrWhiteSpace(InvoiceNo))
				{
					InvoiceNo = header.InvoiceNo;
				}

				if (DateOnly.TryParse(header.InvoiceDate, CultureInfo.InvariantCulture, out DateOnly parsedDate) && InvoiceDate.Date == DateTime.Today)
				{
					InvoiceDate = parsedDate.ToDateTime(TimeOnly.MinValue);
				}
			}

			int extractedCount = StagedItems.Count;
			decimal taxable = header?.Subtotal ?? 0m;
			decimal grand = header?.GrandTotal ?? 0m;
			LoadedDocumentCount = files.Count;
			if (extractedCount > 0)
			{
				_importedExpectedGrandTotal = grand > 0m ? grand : null;
				ApplyStagedItems();
			}

			NotifyTotals();

			OnPropertyChanged(nameof(HasStagedItems));
			applyStagedItemsCommand?.NotifyCanExecuteChanged();
			discardStagedItemsCommand?.NotifyCanExecuteChanged();
			int unmatched = Lines.Count(line => line.Drug == null);
			ErrorMessage = problems.Count == 0
				? (unmatched == 0 ? string.Empty : unmatched + " line(s) are not in the medicine master. Match or add those medicines before saving.")
				: string.Join(" ", problems);
			StatusMessage = extractedCount == 0
				? "No invoice rows were extracted."
				: extractedCount + " line(s) are on the purchase grid. Taxable Rs. " + taxable.ToString("N2", CultureInfo.InvariantCulture) + ", grand total Rs. " + grand.ToString("N2", CultureInfo.InvariantCulture) + ". Review the rows, then press Transfer to Stock (F10 or Ctrl+S).";
			ImportStatus = extractedCount == 0 ? ErrorMessage : "Read " + files.Count + " file(s) into the purchase table.";
			return extractedCount > 0;
		}
		finally
		{
			IsImporting = false;
		}
	}

	private Drug? MatchDrug(string itemName)
	{
		string name = itemName.Trim();
		Drug? exact = _drugIndex.Concat(Drugs).FirstOrDefault((Drug drug) => string.Equals(drug.Name.Trim(), name, StringComparison.OrdinalIgnoreCase) || string.Equals(drug.BrandName?.Trim(), name, StringComparison.OrdinalIgnoreCase));
		if (exact != null)
		{
			return exact;
		}

		string[] tokens = name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
			.Where(token => token.Length >= 6 && !token.Equals("tablets", StringComparison.OrdinalIgnoreCase) && !token.Equals("tablet", StringComparison.OrdinalIgnoreCase) && !token.Equals("capsules", StringComparison.OrdinalIgnoreCase))
			.ToArray();
		return _drugIndex.Concat(Drugs).FirstOrDefault((Drug drug) => tokens.Any(token => drug.Name.Contains(token, StringComparison.OrdinalIgnoreCase) || (drug.BrandName?.Contains(token, StringComparison.OrdinalIgnoreCase) ?? false)));
	}

	private Drug? FindDrug(Guid id) =>
		_drugIndex.FirstOrDefault(drug => drug.Id == id)
		?? Drugs.FirstOrDefault(drug => drug.Id == id);

	private async Task EnsureExtractedSupplierAsync(string? supplierName, bool autoCreateWithoutConfirm = false)
	{
		if (SelectedSupplier != null)
		{
			return;
		}

		string name = string.IsNullOrWhiteSpace(supplierName) ? "General Supplier" : supplierName.Trim();
		Supplier? existing = Suppliers.FirstOrDefault((Supplier supplier) => string.Equals(supplier.Name.Trim(), name, StringComparison.OrdinalIgnoreCase));
		if (existing != null)
		{
			SelectedSupplier = existing;
			return;
		}

		if (currentSession.User == null)
		{
			StatusMessage = "Extracted supplier: " + name + ". Add that supplier before saving.";
			return;
		}

		bool isDefault = IsDefaultSupplierName(name);
		if (!autoCreateWithoutConfirm && !isDefault
			&& !confirmationService.Confirm("'" + name + "' is not in the supplier list. Add this supplier and use it on the purchase?", "Add supplier"))
		{
			StatusMessage = "Extracted supplier: " + name + ". Select or add that supplier before saving.";
			return;
		}

		using IServiceScope scope = scopeFactory.CreateScope();
		Supplier created = await scope.ServiceProvider.GetRequiredService<AddStockService>().AddSupplierAsync(name, currentSession.User.Id, currentSession.User.Role);
		created.IsActive = true;
		if (Suppliers.All(supplier => supplier.Id != created.Id))
		{
			Suppliers.Add(created);
		}

		SelectedSupplier = Suppliers.First(supplier => supplier.Id == created.Id);
	}

	private static bool IsDefaultSupplierName(string name) =>
		string.Equals(name, "General Supplier", StringComparison.OrdinalIgnoreCase)
		|| string.Equals(name, "Cash / Direct Purchase", StringComparison.OrdinalIgnoreCase)
		|| string.Equals(name, "Cash", StringComparison.OrdinalIgnoreCase);

	private void StageExtractedItems(ExtractedPurchaseInvoice extracted)
	{
		InvoiceTextParserCache.Discounts.TryGetValue(extracted, out List<decimal>? discounts);
		for (int index = 0; index < extracted.Items.Count; index++)
		{
			ExtractedPurchaseLine extractedLine = extracted.Items[index];
			Drug? matched = MatchDrug(extractedLine.ItemName);
			string expiryText = extractedLine.Expiry ?? string.Empty;
			string formattedExpiry = FormatExpiryInput(expiryText);
			if (AddStockViewModel.TryParseExpiry(formattedExpiry, out DateOnly expiryDate) || DateOnly.TryParse(expiryText, CultureInfo.InvariantCulture, DateTimeStyles.None, out expiryDate))
			{
				formattedExpiry = expiryDate.ToString("MM/yyyy", CultureInfo.InvariantCulture);
			}

			StagedItems.Add(new StagedPurchaseLine
			{
				Drug = matched,
				ItemName = extractedLine.ItemName,
				Batch = extractedLine.Batch,
				Expiry = formattedExpiry,
				Quantity = extractedLine.Quantity,
				Free = extractedLine.Free,
				Mrp = extractedLine.Mrp,
				Rate = extractedLine.Rate,
				Gst = StandardGstSlab(extractedLine.Gst),
				DiscountPercent = discounts != null && index < discounts.Count ? discounts[index] : 0m,
				ValidationWarning = extractedLine.ValidationWarning
			});
		}
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
		LoadedDocumentCount = 1;
		ImportStatus = "Reading the bill...";
		BillTotalCheck = string.Empty;
		try
		{
			await ImportCoreAsync(path);
			if (string.IsNullOrWhiteSpace(ImportStatus) || ImportStatus == "Reading the bill...")
			{
				ImportStatus = (ErrorMessage.Length > 0 && Lines.Count == 0 && ReturnLines.Count == 0 && StagedItems.Count == 0) ? ErrorMessage : "Read by: Gemini";
			}
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
			ExtractedPurchaseInvoice extracted;
			try
			{
				extracted = await scope.ServiceProvider.GetRequiredService<GeminiPurchaseImportService>().ExtractAsync(path);
				Debug.WriteLine($"Purchase import: Gemini path returned {extracted.Items.Count} extracted item(s) for '{path}'.");
			}
			catch (Exception remoteError) when (remoteError is not OperationCanceledException)
			{
				extracted = await scope.ServiceProvider.GetRequiredService<InvoiceDocumentExtractor>().ExtractLocalAsync(path);
				Debug.WriteLine($"Purchase import: local OCR path returned {extracted.Items.Count} extracted item(s) for '{path}'. Gemini fallback reason: {remoteError.Message}");
				ImportStatus = "Gemini was unavailable. The invoice was read on this PC.";
				StatusMessage = GeminiGenerateContent.UserMessageFor(remoteError) + ". The rows were read locally. Review them before transferring to stock.";
			}
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
			if (SelectedSupplier == null)
			{
				SelectedSupplier = Suppliers.FirstOrDefault((Supplier supplier) => string.Equals(supplier.Name.Trim(), extracted.Supplier.Trim(), StringComparison.OrdinalIgnoreCase));
			}
			if (string.IsNullOrWhiteSpace(InvoiceNo))
			{
				InvoiceNo = extracted.InvoiceNo;
			}
			if (DateOnly.TryParse(extracted.InvoiceDate, CultureInfo.InvariantCulture, out var result) && InvoiceDate.Date == DateTime.Today)
			{
				InvoiceDate = result.ToDateTime(TimeOnly.MinValue);
			}
			await TryAttachInvoicePreview(path);
			StagedItems.Clear();
			StageExtractedItems(extracted);
			Debug.WriteLine($"Purchase import: StagedItems.Count after StageExtractedItems = {StagedItems.Count}; extracted count = {extracted.Items.Count}.");
			int unmatched = StagedItems.Count(line => line.Drug == null);
			OnPropertyChanged(nameof(HasStagedItems));
			applyStagedItemsCommand?.NotifyCanExecuteChanged();
			discardStagedItemsCommand?.NotifyCanExecuteChanged();
			_importedExpectedGrandTotal = extracted.GrandTotal;
			BillTotalMatches = false;
			bool readLocally = ImportStatus.StartsWith("Gemini was unavailable", StringComparison.Ordinal);
			BillTotalCheck = readLocally
				? $"Read {StagedItems.Count} lines on this PC. Bill total on the invoice: {extracted.GrandTotal:F2}. Review them, then transfer to stock."
				: $"Gemini read {StagedItems.Count} lines. Bill total on the invoice: {extracted.GrandTotal:F2}. Apply them, then check the purchase total.";
			ErrorMessage = unmatched == 0 ? string.Empty : unmatched + " staged line(s) are not in the medicine master. Edit or delete them after applying.";
			if (!readLocally)
			{
				StatusMessage = SelectedSupplier == null
					? "Review the staged lines beside the invoice, then apply them. Extracted supplier: " + extracted.Supplier + "."
					: "Review the staged lines beside the invoice, then apply them to the purchase bill.";
			}
		}
		catch (Exception ex)
		{
			ErrorMessage = FriendlyInvoiceReadError(ex);
			ImportStatus = ErrorMessage;
		}
	}

	private static string FriendlyInvoiceReadError(Exception ex)
	{
		string message = ex.Message ?? string.Empty;
		if (message.Contains('{') || message.Contains("NOT_FOUND", StringComparison.OrdinalIgnoreCase) || message.Contains("models/gemini", StringComparison.OrdinalIgnoreCase) || message.Length > 220)
		{
			return "This invoice could not be read. Check the file and try again.";
		}

		return string.IsNullOrWhiteSpace(message) ? "This invoice could not be read. Check the file and try again." : message;
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
			ErrorMessage = string.Empty;
			StatusMessage = "Reading spreadsheet…";
			_spreadsheet = await spreadsheetReader.ReadAsync(text);
			_spreadsheetFingerprint = ComputeSpreadsheetFingerprint(text, _spreadsheet);
			_spreadsheetFileName = Path.GetFileName(text);
			OriginalInvoiceFileName = _spreadsheetFileName;
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

			SpreadsheetPreview = BuildPreview(_spreadsheet);
			bool allRequiredMapped = SpreadsheetMappings
				.Where((SpreadsheetColumnMapping mapping) => mapping.IsRequired)
				.All((SpreadsheetColumnMapping mapping) => !string.IsNullOrWhiteSpace(mapping.SourceHeader));

			string applyError = string.Empty;
			if (allRequiredMapped && TryApplySpreadsheetMapping(replaceWithoutConfirm: true, out applyError))
			{
				confirmationService.Notify("Spreadsheet loaded", StatusMessage);
				return;
			}

			HasSpreadsheet = true;
			OnPropertyChanged(nameof(SpreadsheetHeaders));
			OnPropertyChanged(nameof(SpreadsheetMappings));
			StatusMessage = allRequiredMapped
				? $"Loaded {_spreadsheet.Rows.Count} rows. Columns were detected — click Apply to purchase table."
				: $"Loaded {_spreadsheet.Rows.Count} rows. Choose a column for Medicine, Batch, Expiry, Qty, and Rate, then Apply.";
			if (!string.IsNullOrWhiteSpace(applyError))
			{
				ErrorMessage = applyError;
				confirmationService.Notify("Could not auto-import spreadsheet", applyError + "\n\nMap the columns below and click Apply.");
			}
		}
		catch (Exception ex)
		{
			HasSpreadsheet = false;
			_spreadsheet = null;
			SpreadsheetPreview = null;
			ErrorMessage = "Could not load CSV/Excel: " + ex.Message;
			StatusMessage = string.Empty;
			confirmationService.Notify("Load CSV / Excel failed", ErrorMessage);
		}
	}

	private void ApplySpreadsheetMapping()
	{
		if (!TryApplySpreadsheetMapping(replaceWithoutConfirm: false, out string error) && !string.IsNullOrWhiteSpace(error))
		{
			ErrorMessage = error;
			confirmationService.Notify("Could not apply spreadsheet", error);
		}
	}

	private bool TryApplySpreadsheetMapping(bool replaceWithoutConfirm, out string error)
	{
		error = string.Empty;
		if (_spreadsheet == null)
		{
			error = "Choose a CSV or XLSX file first.";
			return false;
		}

		if (Lines.Count > 0 && !replaceWithoutConfirm
			&& !confirmationService.Confirm("Replace the purchase lines currently in the review grid?", "Replace purchase lines"))
		{
			return false;
		}

		Dictionary<string, string> mappings = SpreadsheetMappings.ToDictionary(
			(SpreadsheetColumnMapping mapping) => mapping.Target,
			(SpreadsheetColumnMapping mapping) => mapping.SourceHeader,
			StringComparer.Ordinal);
		SpreadsheetColumnMapping? missing = SpreadsheetMappings.FirstOrDefault(
			(SpreadsheetColumnMapping mapping) => mapping.IsRequired && string.IsNullOrWhiteSpace(mapping.SourceHeader));
		if (missing != null)
		{
			error = "Map required field '" + missing.Label + "' before applying the spreadsheet.";
			return false;
		}

		List<PurchaseLineDraft> list = new List<PurchaseLineDraft>();
		List<string> skipped = new List<string>();
		for (int num = 0; num < _spreadsheet.Rows.Count; num++)
		{
			IReadOnlyDictionary<string, string> row = _spreadsheet.Rows[num];
			string itemName = Read(row, mappings, "Medicine").Trim();
			if (string.IsNullOrWhiteSpace(itemName))
			{
				skipped.Add("Row " + (num + 2) + ": blank medicine name.");
				continue;
			}

			if (!TryDecimal(Read(row, mappings, "Quantity"), out decimal quantity) || quantity <= 0m)
			{
				skipped.Add("Row " + (num + 2) + " (" + itemName + "): invalid quantity.");
				continue;
			}

			if (!TryDecimal(Read(row, mappings, "Rate"), out decimal rate) || rate < 0m)
			{
				skipped.Add("Row " + (num + 2) + " (" + itemName + "): invalid rate.");
				continue;
			}

			decimal mrp = 0m;
			string mrpText = Read(row, mappings, "MRP");
			if (!string.IsNullOrWhiteSpace(mrpText) && !TryDecimal(mrpText, out mrp))
			{
				skipped.Add("Row " + (num + 2) + " (" + itemName + "): invalid MRP.");
				continue;
			}

			if (mrp <= 0m)
			{
				mrp = rate;
			}

			if (!TryOptionalDecimal(Read(row, mappings, "Free quantity"), allowPercent: false, out decimal freeQty)
				|| !TryOptionalDecimal(Read(row, mappings, "GST"), allowPercent: true, out decimal gst)
				|| !TryOptionalDecimal(Read(row, mappings, "Discount"), allowPercent: true, out decimal discount)
				|| freeQty < 0m || gst < 0m || discount < 0m)
			{
				skipped.Add("Row " + (num + 2) + " (" + itemName + "): invalid free / GST / discount.");
				continue;
			}

			// Discount column may be % or ₹ — treat values ≤ 100 with no amount column as percent.
			string amountText = Read(row, mappings, "Amount");
			decimal discountAmount = discount;
			if (discount > 0m && discount <= 100m
				&& (string.IsNullOrWhiteSpace(amountText) || !TryDecimal(amountText, out _)))
			{
				discountAmount = decimal.Round(quantity * rate * discount / 100m, 2, MidpointRounding.AwayFromZero);
			}

			string expiryText = Read(row, mappings, "Expiry");
			string expiry = NormalizeSpreadsheetExpiry(expiryText);

			string batch = Read(row, mappings, "Batch").Trim();
			if (string.IsNullOrWhiteSpace(batch))
			{
				batch = "NA";
			}

			PurchaseLineDraft purchaseLineDraft = new PurchaseLineDraft(MatchDrug(itemName))
			{
				MedicineName = itemName,
				BatchNo = batch,
				Expiry = expiry,
				Quantity = quantity,
				FreeQuantity = freeQty,
				Mrp = mrp,
				Rate = rate,
				GstRate = gst,
				DiscountAmount = discountAmount,
				AcceptMedicineEdits = true,
				InwardDate = InvoiceDate.Date
			};
			purchaseLineDraft.PropertyChanged += OnLineChanged;
			list.Add(purchaseLineDraft);
		}

		if (list.Count == 0)
		{
			error = skipped.Count == 0
				? "No purchase rows could be read from the spreadsheet."
				: "No valid rows imported. " + string.Join(" ", skipped.Take(3));
			return false;
		}

		_pagingSuspend++;
		try
		{
			Lines.Clear();
			_importedExpectedGrandTotal = null;
			foreach (PurchaseLineDraft item in list)
			{
				Lines.Add(item);
			}
		}
		finally
		{
			_pagingSuspend = Math.Max(0, _pagingSuspend - 1);
			// Clear inward date filter so imported rows are not hidden by an old From/To range.
			_dateFilterActive = false;
			_filterFromDate = null;
			_filterToDate = null;
			OnPropertyChanged(nameof(FilterFromDate));
			OnPropertyChanged(nameof(FilterToDate));
			RefreshPaging(resetToFirst: true);
		}

		EnsurePurchaseHeaderDefaults(fromSpreadsheet: true);
		NotifyTotals();
		ErrorMessage = skipped.Count == 0 ? string.Empty : "Imported " + list.Count + " row(s); skipped " + skipped.Count + ". " + string.Join(" ", skipped.Take(2));
		StatusMessage = "Loaded " + list.Count + " row(s) from spreadsheet. Confirm medicines and expiry, then Transfer to Stock.";
		HasSpreadsheet = false;
		SpreadsheetPreview = null;
		_spreadsheet = null;
		return true;
	}

	/// <summary>
	/// Ensures Supplier + Invoice No. are filled so Transfer to Stock is not blocked after CSV/Excel import.
	/// </summary>
	private void EnsurePurchaseHeaderDefaults(bool fromSpreadsheet)
	{
		if (SelectedSupplier == null && Suppliers.Count > 0)
		{
			SelectedSupplier = Suppliers.FirstOrDefault((Supplier supplier) =>
					supplier.Name.Contains("Cash", StringComparison.OrdinalIgnoreCase)
					|| supplier.Name.Contains("Direct", StringComparison.OrdinalIgnoreCase)
					|| supplier.Name.Contains("General", StringComparison.OrdinalIgnoreCase))
				?? Suppliers[0];
		}

		if (string.IsNullOrWhiteSpace(InvoiceNo))
		{
			// Prefer a stable id from file content so re-importing the same spreadsheet
			// hits the duplicate-invoice guard instead of minting a new CSV-HHmm each time.
			if (fromSpreadsheet && !string.IsNullOrWhiteSpace(_spreadsheetFingerprint))
			{
				InvoiceNo = "CSV-" + _spreadsheetFingerprint;
			}
			else if (fromSpreadsheet)
			{
				InvoiceNo = $"CSV-{DateTime.Now:yyyyMMdd-HHmm}";
			}
			else
			{
				InvoiceNo = $"INV-{DateTime.Now:yyyyMMdd-HHmmss}";
			}
		}

		if (string.IsNullOrWhiteSpace(OriginalInvoiceFileName) && !string.IsNullOrWhiteSpace(_spreadsheetFileName))
		{
			OriginalInvoiceFileName = _spreadsheetFileName;
		}
	}

	private static string ComputeSpreadsheetFingerprint(string filePath, PurchaseSpreadsheetData data)
	{
		try
		{
			using SHA256 sha = SHA256.Create();
			byte[] fileHash = sha.ComputeHash(File.ReadAllBytes(filePath));
			string hex = Convert.ToHexString(fileHash.AsSpan(0, 6)).ToLowerInvariant();
			string name = Path.GetFileNameWithoutExtension(filePath);
			string safeName = new string(name.Where(ch => char.IsLetterOrDigit(ch)).Take(12).ToArray());
			if (string.IsNullOrWhiteSpace(safeName))
			{
				safeName = "sheet";
			}

			return safeName + "-" + hex + "-r" + data.Rows.Count.ToString(CultureInfo.InvariantCulture);
		}
		catch
		{
			return "sheet-" + DateTime.UtcNow.Ticks.ToString("x", CultureInfo.InvariantCulture);
		}
	}

	internal static string NormalizeSpreadsheetExpiry(string expiryText)
	{
		if (PurchaseLineDraft.TryParseExpiry(expiryText, out DateOnly expiryDate))
		{
			return expiryDate.ToString("MM/yyyy", CultureInfo.InvariantCulture);
		}

		string extractedExpiry = InvoiceTextParser.ExtractExpiry(expiryText);
		if (!string.IsNullOrWhiteSpace(extractedExpiry))
		{
			return extractedExpiry;
		}

		return expiryText;
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

	public event Func<Task>? PurchasePosted;

	private async Task EnsurePurchaseMedicinesAsync()
	{
		foreach (PurchaseLineDraft named in Lines.Where((PurchaseLineDraft line) => line.Drug == null && !string.IsNullOrWhiteSpace(line.MedicineName)))
		{
			named.Drug = MatchDrug(named.MedicineName);
		}

		List<PurchaseLineDraft> missing = Lines.Where((PurchaseLineDraft line) => line.Drug == null && !string.IsNullOrWhiteSpace(line.MedicineName)).ToList();
		if (missing.Count == 0 || currentSession.User == null)
		{
			return;
		}

		using IServiceScope scope = scopeFactory.CreateScope();
		CustomMedicineService medicines = scope.ServiceProvider.GetRequiredService<CustomMedicineService>();
		PharmaBillDbContext context = scope.ServiceProvider.GetRequiredService<PharmaBillDbContext>();
		foreach (PurchaseLineDraft line in missing)
		{
			CustomMedicineResult created = await medicines.SaveAsync(new CustomMedicineRequest(line.MedicineName.Trim(), null, null, null, "3004", line.GstRate, line.Mrp > 0m ? line.Mrp : null, line.Rate), currentSession.User.Id);
			Drug? drug = await context.Drugs.FirstOrDefaultAsync((Drug item) => item.Id == created.DrugId);
			if (drug == null)
			{
				continue;
			}

			line.Drug = drug;
			line.MedicineName = drug.Name;
			if (Drugs.All((Drug item) => item.Id != drug.Id))
			{
				Drugs.Add(drug);
			}
		}
	}

	private async Task TransferToStockAsync()
	{
		if (InwardAllPhoneBills && PhoneBills.Any((PhoneIncomingBill bill) => !bill.IsCommitted))
		{
			await CommitAllPhoneBillsAsync();
			return;
		}

		ErrorMessage = string.Empty;
		StatusMessage = string.Empty;
		EnsurePurchaseHeaderDefaults(fromSpreadsheet: false);

		string supplierName = !string.IsNullOrWhiteSpace(_pendingSupplierName)
			? _pendingSupplierName!
			: "Cash / Direct Purchase";
		if (SelectedSupplier == null)
		{
			await EnsureExtractedSupplierAsync(supplierName, autoCreateWithoutConfirm: true);
		}

		if (SelectedSupplier == null)
		{
			await EnsureExtractedSupplierAsync("Cash / Direct Purchase", autoCreateWithoutConfirm: true);
		}

		if (string.IsNullOrWhiteSpace(InvoiceNo))
		{
			InvoiceNo = $"CSV-{DateTime.Now:yyyyMMdd-HHmm}";
		}

		SupplierError = SelectedSupplier == null ? "Supplier is required" : string.Empty;
		InvoiceNoError = string.IsNullOrWhiteSpace(InvoiceNo) ? "Supplier invoice no. is required" : string.Empty;
		InvoiceDateError = string.Empty;
		if (SelectedSupplier == null || string.IsNullOrWhiteSpace(InvoiceNo))
		{
			ErrorMessage = "Please select a Supplier and enter an Invoice Number before saving.";
			confirmationService.Notify("Cannot Save Purchase Yet", ErrorMessage);
			return;
		}

		// Newly created suppliers must remain active for PurchaseService FK / IsActive checks.
		if (!SelectedSupplier.IsActive)
		{
			SelectedSupplier.IsActive = true;
		}

		if (Lines.Count == 0)
		{
			ErrorMessage = "Add at least one medicine item with batch, quantity, and purchase rate before transferring to stock.";
			confirmationService.Notify("Cannot Save Purchase Yet", ErrorMessage);
			return;
		}

		if (currentSession.User == null)
		{
			ErrorMessage = "Sign in again to transfer purchases into stock.";
			return;
		}

		if (!DateOnly.TryParse(InvoiceDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), out var invoiceDate))
		{
			ErrorMessage = "Enter a valid invoice date.";
			return;
		}

		NormalizeLineExpiries();
		await EnsurePurchaseMedicinesAsync();
		PurchaseLineDraft? invalid = Lines.FirstOrDefault((PurchaseLineDraft line) => !line.TryCreateInput(out PurchaseLineInput _));
		if (Lines.Count == 0 || invalid != null)
		{
			string detail = invalid == null
				? "Add at least one medicine line."
				: $"Check '{invalid.MedicineName}': medicine, batch, expiry (e.g. 06/2027), quantity, and rate are required.";
			ErrorMessage = "Complete each purchase line with a medicine, batch, expiry, quantity and purchase rate. " + detail;
			confirmationService.Notify("Cannot Save Purchase Yet", ErrorMessage);
			return;
		}

		NotifyTotals();
		try
		{
			using IServiceScope scope = scopeFactory.CreateScope();
			PurchaseService service = scope.ServiceProvider.GetRequiredService<PurchaseService>();
			string invoiceKey = InvoiceNo.Trim();
			if (await service.IsDuplicateInvoiceAsync(SelectedSupplier.Id, invoiceKey))
			{
				ErrorMessage = "Invoice '" + invoiceKey + "' was already transferred to stock for this supplier. Stock was not added again.";
				confirmationService.Notify("Duplicate purchase invoice", ErrorMessage);
				return;
			}

			if (!string.IsNullOrWhiteSpace(_spreadsheetFingerprint)
				&& await service.IsDuplicateSourceFingerprintAsync(_spreadsheetFingerprint))
			{
				ErrorMessage = "This spreadsheet file was already transferred to stock. Re-importing it will not add quantity again.";
				confirmationService.Notify("Duplicate spreadsheet import", ErrorMessage);
				return;
			}

			PurchaseLineInput[] items = Lines.Select((PurchaseLineDraft line) =>
			{
				line.TryCreateInput(out PurchaseLineInput input);
				return input;
			}).ToArray();
			string? notes = PurchaseService.AppendSourceFingerprint(Remarks, _spreadsheetFingerprint);
			string? attachedPath = InvoiceImagePath;
			if (!string.IsNullOrWhiteSpace(attachedPath) && File.Exists(attachedPath))
			{
				attachedPath = PurchaseAttachmentStore.Relocate(attachedPath, invoiceDate.Year, SelectedSupplier.Id);
				InvoiceImagePath = attachedPath;
			}

			if (string.IsNullOrWhiteSpace(OriginalInvoiceFileName) && !string.IsNullOrWhiteSpace(_spreadsheetFileName))
			{
				OriginalInvoiceFileName = _spreadsheetFileName;
			}

			int transferred = Lines.Count;
			await service.SavePurchaseAsync(new SavePurchaseInput(SelectedSupplier.Id, invoiceKey, invoiceDate, Subtotal, 0m, TaxTotal, GrandTotal, items, notes, SelectedStorageLocation?.Id, attachedPath, OriginalInvoiceFileName), currentSession.User.Id, currentSession.User.Role);
			_dateFilterActive = false;
			_filterFromDate = null;
			_filterToDate = null;
			OnPropertyChanged(nameof(FilterFromDate));
			OnPropertyChanged(nameof(FilterToDate));
			Lines.Clear();
			_importedExpectedGrandTotal = null;
			_pendingSupplierName = null;
			LoadedDocumentCount = 0;
			// Keep invoice no. + fingerprint so a second Transfer of the same bill is blocked immediately.
			StatusMessage = "Success! " + transferred + " items added to inventory under invoice " + invoiceKey + ". Re-importing this file will not add stock again.";
			IsPdfAttachment = false;
			InvoiceImagePath = null;
			InvoiceThumbnail = null;
			InvoiceOcrText = string.Empty;
			if (_loadedPhoneBill != null)
			{
				_loadedPhoneBill.Status = "Committed";
			}

			confirmationService.Notify("Success", StatusMessage);
			if (PurchasePosted != null)
			{
				await PurchasePosted.Invoke();
			}

			NotifyTotals();
		}
		catch (DbUpdateException ex)
		{
			ErrorMessage = FormatSaveFailure(ex);
			confirmationService.Notify("Transfer to Stock failed", ErrorMessage);
		}
		catch (Exception ex)
		{
			ErrorMessage = FormatSaveFailure(ex);
			confirmationService.Notify("Transfer to Stock failed", ErrorMessage);
		}
	}

	private void NormalizeLineExpiries()
	{
		foreach (PurchaseLineDraft line in Lines)
		{
			if (string.IsNullOrWhiteSpace(line.Expiry))
			{
				continue;
			}

			if (PurchaseLineDraft.TryParseExpiry(line.Expiry, out DateOnly expiry))
			{
				line.Expiry = expiry.ToString("MM/yyyy", CultureInfo.InvariantCulture);
			}
		}
	}

	private static string FormatSaveFailure(Exception ex)
	{
		Exception walk = ex;
		while (walk.InnerException != null)
		{
			walk = walk.InnerException;
		}

		string message = walk.Message;
		if (string.IsNullOrWhiteSpace(message))
		{
			message = ex.Message;
		}

		return "Could not transfer purchase into stock: " + message;
	}

	private async Task SavePurchaseReturnAsync()
	{
		bool missingBatch = SelectedReturnBatch == null && ReturnLines.Count == 0;
		bool missingQuantity = ReturnLines.Count == 0 && ReturnQuantity <= 0m;
		if (missingBatch || missingQuantity)
		{
			ErrorMessage = "Please select an existing batch and specify the quantity to return.";
			confirmationService.Notify("Cannot Post Return Yet", ErrorMessage);
			return;
		}
		if (SelectedSupplier == null || currentSession.User == null || string.IsNullOrWhiteSpace(ReturnNo) || string.IsNullOrWhiteSpace(ReturnReason))
		{
			ErrorMessage = "Select a supplier, enter a return number, and write the return reason before posting.";
			confirmationService.Notify("Cannot Post Return Yet", ErrorMessage);
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
			ErrorMessage = "Please select an existing batch and specify the quantity to return.";
			confirmationService.Notify("Cannot Post Return Yet", ErrorMessage);
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
				await RecordLockUi.RunAsync(scope.ServiceProvider, () => scope.ServiceProvider.GetRequiredService<PurchaseService>().SavePurchaseReturnAsync(SelectedSupplier.Id, ReturnNo, DateOnly.FromDateTime(DateTime.Today), array, ReturnReason, currentSession.User.Id, currentSession.User.Role));
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
		NotifyTotals();
	}

	private static string FindLikelyHeader(string target, IReadOnlyList<string> headers)
	{
		string[] candidates = target switch
		{
			"Medicine" => ["medicine", "drugname", "drug", "itemname", "item", "productname", "product", "particulars", "description"],
			"Batch" => ["batchno", "batchnumber", "batch", "lotno", "lot"],
			"Expiry" => ["expirydate", "expdate", "expiry", "expiration", "exp", "mfgexp"],
			"Quantity" => ["quantity", "qty", "packqty", "billedqty", "units"],
			"Free quantity" => ["freequantity", "freeqty", "schemeqty", "free"],
			"MRP" => ["mrp", "maximumretailprice"],
			"Rate" => ["purchaserate", "purrate", "rate", "ptr", "pts", "unitprice", "price", "cost"],
			"GST" => ["gstrate", "gst%", "gst", "tax%", "taxrate", "tax"],
			"Discount" => ["discount%", "discount", "disc", "tradediscount"],
			"Amount" => ["amount", "linevalue", "linetotal", "taxable", "value", "total"],
			_ => [],
		};

		// Prefer exact / longer matches so "free" does not steal "Quantity".
		string? best = null;
		int bestScore = 0;
		foreach (string header in headers)
		{
			string normalizedHeader = header
				.Replace(" ", string.Empty, StringComparison.Ordinal)
				.Replace("_", string.Empty, StringComparison.Ordinal)
				.Replace("-", string.Empty, StringComparison.Ordinal)
				.Replace("%", string.Empty, StringComparison.Ordinal)
				.Replace(".", string.Empty, StringComparison.Ordinal);
			foreach (string candidate in candidates)
			{
				if (string.Equals(normalizedHeader, candidate, StringComparison.OrdinalIgnoreCase))
				{
					return header;
				}

				if (normalizedHeader.Contains(candidate, StringComparison.OrdinalIgnoreCase) && candidate.Length > bestScore)
				{
					best = header;
					bestScore = candidate.Length;
				}
			}
		}

		return best ?? string.Empty;
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

	internal static bool TryOptionalDecimal(string value, bool allowPercent, out decimal result)
	{
		result = 0m;
		if (string.IsNullOrWhiteSpace(value))
		{
			return true;
		}

		string cleaned = value.Trim();
		if (cleaned is "-" or "–" or "—")
		{
			return true;
		}

		if (allowPercent && cleaned.EndsWith('%'))
		{
			cleaned = cleaned[..^1].Trim();
		}

		return TryDecimal(cleaned, out result);
	}

	private static decimal OptionalDecimal(string value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return 0m;
		}

		return TryDecimal(value, out decimal result) ? result : -1m;
	}

	private static bool TryDecimal(string value, out decimal result)
	{
		result = 0m;
		if (string.IsNullOrWhiteSpace(value))
		{
			return false;
		}

		string cleaned = value.Trim()
			.Replace("₹", string.Empty, StringComparison.Ordinal)
			.Replace("Rs.", string.Empty, StringComparison.OrdinalIgnoreCase)
			.Replace(",", string.Empty, StringComparison.Ordinal)
			.Trim();
		return decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out result)
			|| decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.CurrentCulture, out result);
	}
}
