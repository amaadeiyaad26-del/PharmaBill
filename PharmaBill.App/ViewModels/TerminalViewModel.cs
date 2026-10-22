using PharmaBill.Core;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Printing;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using PharmaBill.App.Services;
using PharmaBill.Data.Services;
using PharmaBill.Sync.Terminals;

namespace PharmaBill.App.ViewModels;

/// <summary>One line on a counter bill. Price, stock and tax are checked again on the server.</summary>
public sealed class TerminalLineViewModel : ObservableObject
{
	private decimal _quantity = 1m;
	private decimal _unitPrice;
	private decimal _discount;
	private string _hsnCode;
	private string _gstRate;
	private bool _isNilRated;
	private bool _saveTaxToProduct = true;
	private string _mrpInput = string.Empty;

	public TerminalLineViewModel(RetailStockChoice choice, Action changed)
	{
		Choice = choice;
		_unitPrice = choice.SalePrice is > 0m && choice.SalePrice <= choice.Mrp ? choice.SalePrice.Value : choice.Mrp;
		_hsnCode = choice.HsnCode ?? string.Empty;
		_gstRate = choice.GstRate?.ToString("0.##", CultureInfo.InvariantCulture) ?? string.Empty;
		_isNilRated = choice.IsNilRated;
		PropertyChanged += (_, e) =>
		{
			if (e.PropertyName is nameof(Quantity) or nameof(UnitPrice) or nameof(Discount))
			{
				OnPropertyChanged(nameof(LineTotal));
				changed();
			}
		};
	}

	public RetailStockChoice Choice { get; }

	public string DrugName => Choice.DrugName;

	public string BatchLabel => $"{Choice.BatchNo}  exp {Choice.ExpiryDate:MM/yy}  · {Choice.AvailableQuantity:0.##} in stock" + (string.IsNullOrWhiteSpace(Choice.Schedule) ? string.Empty : "  · Sch " + Choice.Schedule);

	public bool IsControlled => Choice.Schedule is "H1" or "X" or "NDPS";

	/// <summary>No HSN or GST rate recorded: the cashier must enter them; never billed at 0% by default.</summary>
	public bool NeedsTax => string.IsNullOrWhiteSpace(Choice.HsnCode) || (!Choice.IsNilRated && Choice.GstRate is null);

	/// <summary>No MRP recorded for the batch: someone allowed to enter stock must set it before it can be billed.</summary>
	public bool NeedsMrp => Choice.Mrp <= 0m;

	public decimal Quantity { get => _quantity; set => SetProperty(ref _quantity, value); }

	public decimal UnitPrice { get => _unitPrice; set => SetProperty(ref _unitPrice, value); }

	public decimal Discount { get => _discount; set => SetProperty(ref _discount, value); }

	public string HsnCode { get => _hsnCode; set => SetProperty(ref _hsnCode, value); }

	public string GstRate { get => _gstRate; set => SetProperty(ref _gstRate, value); }

	public bool IsNilRated { get => _isNilRated; set => SetProperty(ref _isNilRated, value); }

	public bool SaveTaxToProduct { get => _saveTaxToProduct; set => SetProperty(ref _saveTaxToProduct, value); }

	public string MrpInput { get => _mrpInput; set => SetProperty(ref _mrpInput, value); }

	public decimal LineTotal => Math.Round(Quantity * UnitPrice - Discount, 2, MidpointRounding.AwayFromZero);

	public string? Problem()
	{
		if (Quantity <= 0m || UnitPrice < 0m || Discount < 0m)
		{
			return $"{DrugName}: quantity must be above zero and price and discount not negative.";
		}
		if (NeedsMrp && !TryDecimal(MrpInput, out _))
		{
			return $"{DrugName}: enter the batch MRP.";
		}
		if (NeedsTax && (string.IsNullOrWhiteSpace(HsnCode) || (!IsNilRated && !TryDecimal(GstRate, out _))))
		{
			return $"{DrugName}: enter its HSN code and GST rate, or tick Nil-rated. A missing rate is never treated as zero.";
		}
		return null;
	}

	public TerminalSaleLine ToSaleLine() => NeedsTax
		? new TerminalSaleLine(Choice.DrugId, Choice.BatchId, Quantity, Discount, UnitPrice, HsnCode.Trim(), IsNilRated ? null : Decimal(GstRate), IsNilRated)
		: new TerminalSaleLine(Choice.DrugId, Choice.BatchId, Quantity, Discount, UnitPrice);

	internal static bool TryDecimal(string? text, out decimal value) =>
		decimal.TryParse((text ?? string.Empty).Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out value) && value >= 0m;

	internal static decimal Decimal(string text) => TryDecimal(text, out decimal value) ? value : 0m;
}

public sealed class TerminalReturnLineViewModel(RetailSaleReturnableLine line) : ObservableObject
{
	private decimal _returnQuantity;

	public RetailSaleReturnableLine Line { get; } = line;

	public string Label => $"{Line.DrugName}  ·  batch {Line.BatchNo}  ·  {Line.RemainingQuantity:0.##} can be returned";

	public decimal ReturnQuantity { get => _returnQuantity; set => SetProperty(ref _returnQuantity, value); }
}

// The billing counter on a terminal PC. It holds no records: every read and every bill goes to the paired server,
// and when the server cannot be reached it stops billing and says so.
public sealed class TerminalViewModel : ObservableObject, IDisposable
{
	private readonly TerminalClient _client;
	private readonly TerminalClientSettingsStore _store;
	private readonly DocumentOutputSettingsStore _printSettings;
	private readonly DispatcherTimer _heartbeat;

	private bool _isConnected;
	private bool _billingAllowed = true;
	private bool _isUnpaired;
	private string _connectionMessage = "Connecting to the server PC…";
	private string _shopName;
	private bool _isSignedIn;
	private string _signedInAs = string.Empty;
	private string _userName = string.Empty;
	private string _pin = string.Empty;
	private string _searchText = string.Empty;
	private RetailStockChoice? _selectedResult;
	private string _patientPhone = string.Empty;
	private string _patientName = string.Empty;
	private string _patientAddress = string.Empty;
	private string _doctorName = string.Empty;
	private string _doctorRegistration = string.Empty;
	private string? _prescriptionPath;
	private string _overrideUser = string.Empty;
	private string _overridePin = string.Empty;
	private string _paymentMethod = "Cash";
	private string _cashTendered = string.Empty;
	private string _nextInvoiceNo = string.Empty;
	private Guid? _pendingRequestId;
	private bool _notConfirmed;
	private bool _isBusy;
	private string _statusMessage = string.Empty;
	private string _errorMessage = string.Empty;
	private string _recentQuery = string.Empty;
	private RecentRetailBill? _selectedRecent;
	private string _returnReason = string.Empty;
	private bool _restock = true;
	private string _adminUser = string.Empty;
	private string _adminPin = string.Empty;
	private string _voidReason = string.Empty;
	private string? _selectedPrinter;
	private InvoiceTemplateType _selectedTemplate;

	public TerminalViewModel(TerminalClientSettings settings, TerminalClientSettingsStore store, DocumentOutputSettingsStore printSettings)
	{
		_client = new TerminalClient(settings);
		_store = store;
		_printSettings = printSettings;
		PrintBill = bill => InvoicePrintService.PrintReceivedAsync(bill, _printSettings.Load());
		_shopName = settings.ShopName;
		DocumentOutputSettings print = printSettings.Load();
		_selectedTemplate = print.DefaultInvoiceTemplate == InvoiceTemplateType.StandardA4 ? InvoiceTemplateType.StandardA4 : InvoiceTemplateType.Thermal80mm;
		_selectedPrinter = _selectedTemplate == InvoiceTemplateType.StandardA4 ? print.RetailInvoicePrinter : print.RetailMemoPrinter;
		Lines.CollectionChanged += (_, _) => OnBillChanged();
		SignInCommand = new AsyncRelayCommand(SignInAsync);
		SignOutCommand = new AsyncRelayCommand(SignOutAsync);
		SearchCommand = new AsyncRelayCommand(SearchAsync);
		AddLineCommand = new RelayCommand(AddSelected);
		RemoveLineCommand = new RelayCommand<TerminalLineViewModel>(line => { if (line != null && IsBillEditable) Lines.Remove(line); });
		FindPatientCommand = new AsyncRelayCommand(FindPatientAsync);
		AttachPrescriptionCommand = new RelayCommand(AttachPrescription);
		SaveCommand = new AsyncRelayCommand(SaveAsync);
		NewBillCommand = new RelayCommand(NewBill);
		LoadRecentCommand = new AsyncRelayCommand(LoadRecentAsync);
		ReprintCommand = new AsyncRelayCommand(ReprintAsync);
		ReturnCommand = new AsyncRelayCommand(ReturnAsync);
		VoidCommand = new AsyncRelayCommand(VoidAsync);
		SavePrinterCommand = new RelayCommand(SavePrinter);
		ForgetPairingCommand = new RelayCommand(ForgetPairing);
		_heartbeat = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
		_heartbeat.Tick += async (_, _) => await CheckConnectionAsync();
		_heartbeat.Start();
		_ = CheckConnectionAsync();
	}

	// ---- connection ----

	public string ServerLabel => _client.ServerLabel;

	public string CounterName => _client.Settings.TerminalName;

	public string ShopName { get => _shopName; private set => SetProperty(ref _shopName, value); }

	public bool IsConnected
	{
		get => _isConnected;
		private set
		{
			if (SetProperty(ref _isConnected, value))
			{
				Notify(nameof(CanBill), nameof(ShowBlockedBanner), nameof(ConnectionLabel));
			}
		}
	}

	public bool IsUnpaired { get => _isUnpaired; private set { if (SetProperty(ref _isUnpaired, value)) Notify(nameof(ShowBlockedBanner)); } }

	public string ConnectionMessage { get => _connectionMessage; private set => SetProperty(ref _connectionMessage, value); }

	public string ConnectionLabel => IsConnected ? "Connected to " + ServerLabel : "Server not reachable";

	/// <summary>Shown across the top whenever billing is stopped, in words a cashier can act on.</summary>
	public bool ShowBlockedBanner => !IsConnected || !_billingAllowed || IsUnpaired;

	public bool CanBill => IsConnected && _billingAllowed && IsSignedIn && !IsBusy;

	// ---- sign-in ----

	public bool IsSignedIn { get => _isSignedIn; private set { if (SetProperty(ref _isSignedIn, value)) Notify(nameof(CanBill)); } }

	public string SignedInAs { get => _signedInAs; private set => SetProperty(ref _signedInAs, value); }

	public string UserName { get => _userName; set => SetProperty(ref _userName, value); }

	public string Pin { get => _pin; set => SetProperty(ref _pin, value); }

	// ---- bill ----

	public string SearchText { get => _searchText; set => SetProperty(ref _searchText, value); }

	public ObservableCollection<RetailStockChoice> SearchResults { get; } = new ObservableCollection<RetailStockChoice>();

	public RetailStockChoice? SelectedResult { get => _selectedResult; set => SetProperty(ref _selectedResult, value); }

	public ObservableCollection<TerminalLineViewModel> Lines { get; } = new ObservableCollection<TerminalLineViewModel>();

	public string PatientPhone { get => _patientPhone; set => SetProperty(ref _patientPhone, value); }

	public string PatientName { get => _patientName; set => SetProperty(ref _patientName, value); }

	public string PatientAddress { get => _patientAddress; set => SetProperty(ref _patientAddress, value); }

	public string DoctorName { get => _doctorName; set => SetProperty(ref _doctorName, value); }

	public string DoctorRegistration { get => _doctorRegistration; set => SetProperty(ref _doctorRegistration, value); }

	public string PrescriptionLabel => _prescriptionPath == null ? "No prescription attached" : "Attached: " + System.IO.Path.GetFileName(_prescriptionPath);

	public bool NeedsPrescription => Lines.Any(line => line.IsControlled);

	public string OverrideUser { get => _overrideUser; set => SetProperty(ref _overrideUser, value); }

	public string OverridePin { get => _overridePin; set => SetProperty(ref _overridePin, value); }

	public IReadOnlyList<string> PaymentMethods { get; } = ["Cash", "UPI", "Card"];

	public string PaymentMethod { get => _paymentMethod; set { if (SetProperty(ref _paymentMethod, value)) Notify(nameof(ChangeText)); } }

	public string CashTendered { get => _cashTendered; set { if (SetProperty(ref _cashTendered, value)) Notify(nameof(ChangeText)); } }

	public decimal Total => Lines.Sum(line => line.LineTotal);

	public string TotalText => MoneyFormat.Rupees(Total);

	public string ChangeText => PaymentMethod == "Cash" && TerminalLineViewModel.TryDecimal(CashTendered, out decimal tendered) && tendered > Total
		? "Change: " + MoneyFormat.Rupees(tendered - Total)
		: string.Empty;

	public string NextInvoiceNo { get => _nextInvoiceNo; private set => SetProperty(ref _nextInvoiceNo, value); }

	/// <summary>A save was sent but its answer never came. The bill is held as it is until the server confirms it.</summary>
	public bool NotConfirmed { get => _notConfirmed; private set { if (SetProperty(ref _notConfirmed, value)) Notify(nameof(IsBillEditable), nameof(SaveLabel)); } }

	public bool IsBillEditable => !NotConfirmed;

	public string SaveLabel => NotConfirmed ? "Confirm with server" : "Save & print";

	public bool IsBusy { get => _isBusy; private set { if (SetProperty(ref _isBusy, value)) Notify(nameof(CanBill)); } }

	public string StatusMessage { get => _statusMessage; private set => SetProperty(ref _statusMessage, value); }

	public string ErrorMessage { get => _errorMessage; private set => SetProperty(ref _errorMessage, value); }

	// ---- recent bills ----

	public string RecentQuery { get => _recentQuery; set => SetProperty(ref _recentQuery, value); }

	public ObservableCollection<RecentRetailBill> RecentBills { get; } = new ObservableCollection<RecentRetailBill>();

	public RecentRetailBill? SelectedRecent
	{
		get => _selectedRecent;
		set
		{
			if (SetProperty(ref _selectedRecent, value))
			{
				_ = LoadReturnableAsync();
			}
		}
	}

	public ObservableCollection<TerminalReturnLineViewModel> ReturnLines { get; } = new ObservableCollection<TerminalReturnLineViewModel>();

	public string ReturnReason { get => _returnReason; set => SetProperty(ref _returnReason, value); }

	public bool Restock { get => _restock; set => SetProperty(ref _restock, value); }

	public string AdminUser { get => _adminUser; set => SetProperty(ref _adminUser, value); }

	public string AdminPin { get => _adminPin; set => SetProperty(ref _adminPin, value); }

	public string VoidReason { get => _voidReason; set => SetProperty(ref _voidReason, value); }

	// ---- printer ----

	public IReadOnlyList<InvoiceTemplateType> Templates { get; } = [InvoiceTemplateType.Thermal80mm, InvoiceTemplateType.StandardA4];

	public InvoiceTemplateType SelectedTemplate { get => _selectedTemplate; set => SetProperty(ref _selectedTemplate, value); }

	public IReadOnlyList<string> Printers { get; } = InstalledPrinters();

	public string? SelectedPrinter { get => _selectedPrinter; set => SetProperty(ref _selectedPrinter, value); }

	/// <summary>Sends a saved bill to this counter's printer. Replaceable so a test can see what would be printed.</summary>
	public Func<RetailBillPrintData, Task> PrintBill { get; set; }

	public IAsyncRelayCommand SignInCommand { get; }
	public IAsyncRelayCommand SignOutCommand { get; }
	public IAsyncRelayCommand SearchCommand { get; }
	public IRelayCommand AddLineCommand { get; }
	public IRelayCommand<TerminalLineViewModel> RemoveLineCommand { get; }
	public IAsyncRelayCommand FindPatientCommand { get; }
	public IRelayCommand AttachPrescriptionCommand { get; }
	public IAsyncRelayCommand SaveCommand { get; }
	public IRelayCommand NewBillCommand { get; }
	public IAsyncRelayCommand LoadRecentCommand { get; }
	public IAsyncRelayCommand ReprintCommand { get; }
	public IAsyncRelayCommand ReturnCommand { get; }
	public IAsyncRelayCommand VoidCommand { get; }
	public IRelayCommand SavePrinterCommand { get; }
	public IRelayCommand ForgetPairingCommand { get; }

	public void Dispose()
	{
		_heartbeat.Stop();
		_client.Dispose();
	}

	private async Task CheckConnectionAsync()
	{
		try
		{
			TerminalHello hello = await _client.HelloAsync();
			ShopName = hello.ShopName;
			_billingAllowed = hello.BillingAllowed;
			IsUnpaired = false;
			IsConnected = true;
			ConnectionMessage = hello.BillingAllowed ? string.Empty : hello.BillingBlockedReason ?? "The server PC is not allowing billing right now.";
			Notify(nameof(CanBill), nameof(ShowBlockedBanner));
		}
		catch (Exception exception)
		{
			Classify(exception);
		}
	}

	private async Task SignInAsync()
	{
		ErrorMessage = string.Empty;
		await RunAsync(async () =>
		{
			TerminalSignInResponse response = await _client.SignInAsync(UserName.Trim(), Pin);
			Pin = string.Empty;
			SignedInAs = $"{response.DisplayName} ({response.Role})";
			IsSignedIn = true;
			NextInvoiceNo = await _client.NextInvoiceNoAsync();
			StatusMessage = string.Empty;
		});
	}

	private async Task SignOutAsync()
	{
		await _client.SignOutAsync();
		IsSignedIn = false;
		SignedInAs = string.Empty;
	}

	private async Task SearchAsync()
	{
		if (string.IsNullOrWhiteSpace(SearchText))
		{
			return;
		}
		await RunAsync(async () =>
		{
			RetailStockChoice[] found = await _client.SearchStockAsync(SearchText.Trim());
			SearchResults.Clear();
			foreach (RetailStockChoice choice in found)
			{
				SearchResults.Add(choice);
			}
			SelectedResult = SearchResults.FirstOrDefault();
			StatusMessage = found.Length == 0 ? "Nothing in stock matches \"" + SearchText.Trim() + "\"." : string.Empty;
		});
	}

	private void AddSelected()
	{
		if (SelectedResult == null || !IsBillEditable)
		{
			return;
		}
		Lines.Add(new TerminalLineViewModel(SelectedResult, OnBillChanged));
		SearchText = string.Empty;
	}

	private async Task FindPatientAsync()
	{
		await RunAsync(async () =>
		{
			RetailPatientMatch? match = (await _client.SearchPatientsAsync(PatientPhone.Trim())).FirstOrDefault();
			if (match != null)
			{
				PatientName = match.Name;
				PatientAddress = match.Address ?? string.Empty;
				StatusMessage = $"Found {match.Name}.";
			}
			else
			{
				StatusMessage = "New patient: enter the name.";
			}
		});
	}

	private void AttachPrescription()
	{
		var dialog = new OpenFileDialog { Filter = "Prescription (JPG, PNG, PDF)|*.jpg;*.jpeg;*.png;*.pdf", Title = "Attach prescription" };
		if (dialog.ShowDialog() == true)
		{
			_prescriptionPath = dialog.FileName;
			Notify(nameof(PrescriptionLabel));
		}
	}

	private async Task SaveAsync()
	{
		ErrorMessage = string.Empty;
		if (Lines.Count == 0)
		{
			ErrorMessage = "Add at least one medicine.";
			return;
		}
		if (!NotConfirmed && Lines.Select(line => line.Problem()).FirstOrDefault(problem => problem != null) is string problem)
		{
			ErrorMessage = problem;
			return;
		}
		decimal total = Total;
		decimal tendered = PaymentMethod == "Cash" && TerminalLineViewModel.TryDecimal(CashTendered, out decimal cash) && cash >= total ? cash : total;
		var sale = new TerminalSaleRequest(PatientName.Trim(), PatientPhone.Trim(), Blank(PatientAddress), Blank(DoctorName), Blank(DoctorRegistration),
			Lines.Select(line => line.ToSaleLine()).ToArray(), [new TerminalPayment(PaymentMethod, total, PaymentMethod == "Cash" ? tendered : 0m)], PaymentMethod == "UPI",
			PrescriptionOverride: string.IsNullOrWhiteSpace(OverrideUser) ? null : new TerminalAdminOverride(OverrideUser.Trim(), OverridePin));
		IsBusy = true;
		try
		{
			if (!NotConfirmed)
			{
				// Corrections the bill needs first. Each is decided on the server, under the signed-in user's role.
				foreach (TerminalLineViewModel line in Lines.Where(line => line.NeedsMrp))
				{
					await _client.SetBatchMrpAsync(line.Choice.BatchId, TerminalLineViewModel.Decimal(line.MrpInput));
				}
				foreach (TerminalLineViewModel line in Lines.Where(line => line.NeedsTax && line.SaveTaxToProduct))
				{
					await _client.SetProductTaxAsync(line.Choice.DrugId, new TerminalProductTaxRequest(line.HsnCode.Trim(), line.IsNilRated ? null : TerminalLineViewModel.Decimal(line.GstRate), line.IsNilRated));
				}
			}
			_pendingRequestId ??= Guid.NewGuid();
			TerminalSaleResponse saved = await _client.SaveSaleAsync(sale, _pendingRequestId.Value, _prescriptionPath);
			_pendingRequestId = null;
			NotConfirmed = false;
			NextInvoiceNo = saved.NextInvoiceNo;
			string change = saved.ChangeDue > 0m ? "  Change due: " + MoneyFormat.Rupees(saved.ChangeDue) + "." : string.Empty;
			ClearBill();
			StatusMessage = $"Bill {saved.InvoiceNo} saved on the server. Total {MoneyFormat.Rupees(saved.TotalAmount)}.{change}";
			await PrintAsync(saved.SaleId, saved.InvoiceNo);
		}
		catch (TerminalServerUnreachableException exception) when (_pendingRequestId != null)
		{
			NotConfirmed = true;
			IsConnected = false;
			ConnectionMessage = PharmaBill.Core.UserMessage.Of(exception);
			ErrorMessage = "NOT CONFIRMED: the server could not be reached while saving, so it is not known whether this bill was saved. "
				+ "Do not hand over the medicine yet. When the server is back, press \"Confirm with server\": the same bill can never be charged twice.";
		}
		catch (Exception exception)
		{
			Classify(exception);
		}
		finally
		{
			IsBusy = false;
		}
	}

	private void NewBill()
	{
		if (NotConfirmed)
		{
			if (MessageBox.Show("This bill was not confirmed by the server. If it was saved there, it will be in Recent bills.\n\nCheck Recent bills before billing these medicines again.\n\nDiscard it on this screen?",
				"Bill not confirmed", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
			{
				return;
			}
		}
		ClearBill();
		StatusMessage = string.Empty;
		ErrorMessage = string.Empty;
	}

	private void ClearBill()
	{
		_pendingRequestId = null;
		NotConfirmed = false;
		Lines.Clear();
		PatientPhone = PatientName = PatientAddress = DoctorName = DoctorRegistration = string.Empty;
		OverrideUser = OverridePin = CashTendered = string.Empty;
		_prescriptionPath = null;
		PaymentMethod = "Cash";
		Notify(nameof(PrescriptionLabel));
	}

	private async Task PrintAsync(Guid saleId, string invoiceNo)
	{
		try
		{
			RetailBillPrintData bill = await _client.PrintDataAsync(saleId);
			await PrintBill(bill);
		}
		catch (Exception exception)
		{
			ErrorMessage = $"Bill {invoiceNo} is saved, but printing failed: {PharmaBill.Core.UserMessage.Of(exception)} Reprint it from Recent bills.";
		}
	}

	private async Task LoadRecentAsync()
	{
		await RunAsync(async () =>
		{
			RecentRetailBill[] bills = await _client.RecentBillsAsync(Blank(RecentQuery));
			RecentBills.Clear();
			foreach (RecentRetailBill bill in bills)
			{
				RecentBills.Add(bill);
			}
		});
	}

	private async Task LoadReturnableAsync()
	{
		ReturnLines.Clear();
		if (SelectedRecent == null)
		{
			return;
		}
		await RunAsync(async () =>
		{
			foreach (RetailSaleReturnableLine line in await _client.ReturnableLinesAsync(SelectedRecent.SaleId))
			{
				ReturnLines.Add(new TerminalReturnLineViewModel(line));
			}
		});
	}

	private async Task ReprintAsync()
	{
		if (SelectedRecent != null)
		{
			await PrintAsync(SelectedRecent.SaleId, SelectedRecent.InvoiceNo);
		}
	}

	private async Task ReturnAsync()
	{
		ErrorMessage = string.Empty;
		if (SelectedRecent == null)
		{
			return;
		}
		TerminalReturnLine[] lines = ReturnLines.Where(line => line.ReturnQuantity > 0m).Select(line => new TerminalReturnLine(line.Line.SaleItemId, line.ReturnQuantity)).ToArray();
		if (lines.Length == 0 || string.IsNullOrWhiteSpace(ReturnReason))
		{
			ErrorMessage = "Enter the quantity to return on at least one line, and the reason.";
			return;
		}
		TerminalAdminOverride? unlock = string.IsNullOrWhiteSpace(AdminUser) ? null : new TerminalAdminOverride(AdminUser.Trim(), AdminPin);
		await RunAsync(async () =>
		{
			TerminalReturnResponse result = await _client.ReturnAsync(SelectedRecent.SaleId, new TerminalReturnRequest(lines, ReturnReason.Trim(), Restock, unlock), Guid.NewGuid());
			StatusMessage = $"Credit note {result.ReturnNo} issued for {MoneyFormat.Rupees(result.CreditAmount)}" + (result.RestockedQuantity > 0m ? $"; {result.RestockedQuantity:0.##} back in stock." : ".");
			ReturnReason = AdminUser = AdminPin = string.Empty;
			await LoadReturnableAsync();
		});
	}

	private async Task VoidAsync()
	{
		ErrorMessage = string.Empty;
		if (SelectedRecent == null)
		{
			return;
		}
		if (string.IsNullOrWhiteSpace(VoidReason) || string.IsNullOrWhiteSpace(AdminUser))
		{
			ErrorMessage = "Voiding needs a reason and an admin's user name and PIN.";
			return;
		}
		await RunAsync(async () =>
		{
			TerminalVoidResponse result = await _client.VoidAsync(SelectedRecent.SaleId, new TerminalVoidRequest(VoidReason.Trim(), new TerminalAdminOverride(AdminUser.Trim(), AdminPin)));
			StatusMessage = result.AlreadyVoided ? $"Bill {result.InvoiceNo} was already voided." : $"Bill {result.InvoiceNo} voided; its stock is back and any cash is refunded on the server.";
			VoidReason = AdminUser = AdminPin = string.Empty;
			await LoadRecentAsync();
		});
	}

	private void SavePrinter()
	{
		DocumentOutputSettings current = _printSettings.Load();
		current.DefaultInvoiceTemplate = SelectedTemplate;
		if (SelectedTemplate == InvoiceTemplateType.StandardA4)
		{
			current.RetailInvoicePrinter = SelectedPrinter ?? string.Empty;
		}
		else
		{
			current.RetailMemoPrinter = SelectedPrinter ?? string.Empty;
		}
		_printSettings.Save(current);
		StatusMessage = "Printer saved for this counter.";
	}

	private void ForgetPairing()
	{
		if (MessageBox.Show($"Stop using this PC as billing counter \"{CounterName}\"?\n\nPharma Remit will close. Also remove it from the server's Devices panel.",
			"Forget the server", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
		{
			_store.Clear();
			Application.Current?.Shutdown();
		}
	}

	private async Task RunAsync(Func<Task> action)
	{
		ErrorMessage = string.Empty;
		IsBusy = true;
		try
		{
			await action();
		}
		catch (Exception exception)
		{
			Classify(exception);
		}
		finally
		{
			IsBusy = false;
		}
	}

	// Every failure becomes something the cashier can act on, and billing stops whenever the server is not there.
	private void Classify(Exception exception)
	{
		switch (exception)
		{
			case TerminalServerUnreachableException:
				IsConnected = false;
				ConnectionMessage = PharmaBill.Core.UserMessage.Of(exception);
				break;
			case TerminalServerIdentityException:
				IsConnected = false;
				ConnectionMessage = PharmaBill.Core.UserMessage.Of(exception);
				ErrorMessage = PharmaBill.Core.UserMessage.Of(exception);
				break;
			case TerminalUnpairedException:
				IsConnected = false;
				IsUnpaired = true;
				ConnectionMessage = "This PC was removed from the server's Devices panel, so it can no longer bill. Ask the owner to pair it again.";
				break;
			case TerminalSignedOutException:
				IsSignedIn = false;
				ErrorMessage = PharmaBill.Core.UserMessage.Of(exception);
				break;
			case TerminalLockedException:
				ErrorMessage = PharmaBill.Core.UserMessage.Of(exception) + " Enter an admin's user name and PIN, then try again.";
				break;
			case TerminalRefusedException:
				ErrorMessage = PharmaBill.Core.UserMessage.Of(exception);
				break;
			default:
				ErrorMessage = PharmaBill.Core.UserMessage.Of(exception);
				break;
		}
	}

	private void OnBillChanged()
	{
		Notify(nameof(Total), nameof(TotalText), nameof(ChangeText), nameof(NeedsPrescription));
	}

	private void Notify(params string[] names)
	{
		foreach (string name in names)
		{
			OnPropertyChanged(name);
		}
	}

	private static string? Blank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

	private static IReadOnlyList<string> InstalledPrinters()
	{
		try
		{
			using var server = new LocalPrintServer();
			return server.GetPrintQueues().Select(queue => queue.FullName).OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();
		}
		catch
		{
			return Array.Empty<string>();
		}
	}
}
