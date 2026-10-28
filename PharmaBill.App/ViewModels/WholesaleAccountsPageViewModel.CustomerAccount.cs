using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using PharmaBill.App.Services;
using PharmaBill.Core;
using PharmaBill.Core.Entities;
using PharmaBill.Data.Services;

namespace PharmaBill.App.ViewModels;

/// <summary>A customer in the dues list, with whether a WhatsApp reminder can be sent to them.</summary>
public sealed record DueRow(CustomerAgeing Ageing)
{
	public string CustomerName => Ageing.CustomerName;
	public decimal Current0To30 => Ageing.Current0To30;
	public decimal Days31To60 => Ageing.Days31To60;
	public decimal Days61To90 => Ageing.Days61To90;
	public decimal Over90 => Ageing.Over90;
	public decimal Total => Ageing.Total;
	public bool HasPhone => !string.IsNullOrWhiteSpace(Ageing.Phone);
	public string WhatsAppToolTip => HasPhone
		? $"Send {Ageing.CustomerName} a WhatsApp reminder of {MoneyFormat.Rupees(Ageing.Total)}"
		: $"No phone number for {Ageing.CustomerName}. Open the account to add one.";
}

/// <summary>Statement lines, bills, payments and credits as shown: dates in IST.</summary>
public sealed record AccountLedgerRow(AccountLedgerLine Line)
{
	public string DateText => Line.AtUtc is DateTime at ? at.ToBusinessLocal().ToString("dd-MMM-yyyy  hh:mm tt", CultureInfo.InvariantCulture) : "Opening balance";
	public DateOnly? Date => Line.AtUtc?.ToBusinessDate();
	public string Kind => Line.Kind;
	public string Reference => Line.Reference ?? string.Empty;
	public string DebitText => Line.Debit == 0m ? string.Empty : Line.Debit.ToString("N2", CultureInfo.InvariantCulture);
	public string CreditText => Line.Credit == 0m ? string.Empty : Line.Credit.ToString("N2", CultureInfo.InvariantCulture);
	public string BalanceText => Line.Balance.ToString("N2", CultureInfo.InvariantCulture);
	public string Notes => Line.Notes ?? string.Empty;
}

public sealed record AccountBillRow(AccountBill Bill)
{
	public string DateText => Bill.AtUtc.ToBusinessLocal().ToString("dd-MMM-yyyy  hh:mm tt", CultureInfo.InvariantCulture);
	public string Kind => Bill.Kind;
	public string Number => Bill.Number;
	public string TotalText => Bill.Total.ToString("N2", CultureInfo.InvariantCulture);
	public string PaidText => Bill.Paid.ToString("N2", CultureInfo.InvariantCulture);
	public string Status => Bill.Status;
}

public sealed record AccountPaymentRow(AccountPayment Payment)
{
	public string DateText => Payment.AtUtc.ToBusinessLocal().ToString("dd-MMM-yyyy  hh:mm tt", CultureInfo.InvariantCulture);
	public string ReceiptNo => Payment.ReceiptNo;
	public string AmountText => Payment.Amount.ToString("N2", CultureInfo.InvariantCulture);
	public string Mode => Payment.Mode;
	public string Reference => Payment.Reference ?? "—";
	public string Notes => Payment.Notes ?? string.Empty;
}

public sealed record AccountCreditRow(AccountCredit Credit)
{
	public string DateText => Credit.AtUtc.ToBusinessLocal().ToString("dd-MMM-yyyy  hh:mm tt", CultureInfo.InvariantCulture);
	public string Kind => Credit.Kind;
	public string Reference => Credit.Reference;
	public string AmountText => Credit.Amount.ToString("N2", CultureInfo.InvariantCulture);
	public string Notes => Credit.Notes ?? string.Empty;
}

public sealed record AccountNoteRow(AccountNote Note)
{
	public string Heading => $"{Note.Author} · {Note.AtUtc.ToBusinessLocal().ToString("dd-MMM-yyyy  hh:mm tt", CultureInfo.InvariantCulture)}";
	public string Text => Note.Text;
}

// The customer's account view: who they are and what they owe (the headline), the ageing of it, the actions (receive a
// payment, statement, WhatsApp, phone number), and the account itself in tabs. Messages about an action show beside it;
// the page's top line is for page-level problems only.
public partial class WholesaleAccountsPageViewModel
{
	private CustomerAccount? _account;
	private DueRow? _selectedDue;
	private bool _isAddingPhone;
	private string _newPhone = string.Empty;
	private string _phoneError = string.Empty;
	private string _receiptError = string.Empty;
	private string _newNoteText = string.Empty;
	private string _noteError = string.Empty;
	private string _billMessage = string.Empty;
	private DateTime? _accountFrom;
	private DateTime? _accountTo;
	private AsyncRelayCommand? _savePhoneCommand;
	private RelayCommand? _addPhoneCommand;
	private RelayCommand? _cancelPhoneCommand;
	private RelayCommand? _sendReminderCommand;
	private AsyncRelayCommand? _addNoteCommand;
	private AsyncRelayCommand<AccountBillRow?>? _openBillCommand;
	private AsyncRelayCommand<AccountBillRow?>? _reprintBillCommand;
	private RelayCommand? _clearAccountRangeCommand;

	/// <summary>Opens a document file: (path, print). Tests replace it so nothing opens during a run.</summary>
	internal Action<string, bool> OpenDocument { get; set; } = (path, print) =>
		Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, Verb = print ? "print" : string.Empty });

	/// <summary>Opens a URL (the WhatsApp reminder). Tests replace it.</summary>
	internal Action<string> OpenUrl { get; set; } = url => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

	/// <summary>The load started by the last customer selection, for tests to wait on.</summary>
	internal Task AccountLoading { get; private set; } = Task.CompletedTask;

	public ObservableCollection<DueRow> Dues { get; } = new();
	public ObservableCollection<AccountLedgerRow> AccountLedger { get; } = new();
	public ObservableCollection<AccountBillRow> AccountBills { get; } = new();
	public ObservableCollection<AccountPaymentRow> AccountPayments { get; } = new();
	public ObservableCollection<AccountCreditRow> AccountCredits { get; } = new();
	public ObservableCollection<AccountNoteRow> AccountNotes { get; } = new();

	public bool HasDues => Dues.Count > 0;

	/// <summary>Selecting a row in the dues list opens that customer's account.</summary>
	public DueRow? SelectedDue
	{
		get => _selectedDue;
		set
		{
			if (SetProperty(ref _selectedDue, value) && value != null && SelectedCustomer?.Id != value.Ageing.CustomerId)
			{
				SelectedCustomer = Customers.FirstOrDefault(customer => customer.Id == value.Ageing.CustomerId) ?? SelectedCustomer;
			}
		}
	}

	public bool HasAccount => _account != null && SelectedCustomer != null;
	public bool HasNoAccount => !HasAccount;
	public string AccountName => _account?.Summary.Name ?? string.Empty;
	public bool HasPhone => !string.IsNullOrWhiteSpace(_account?.Summary.Phone);
	public string PhoneText => HasPhone ? _account!.Summary.Phone! : "No phone number";
	public decimal Outstanding => _account?.Summary.Outstanding ?? 0m;
	public string OutstandingText => MoneyFormat.Rupees(Math.Abs(Outstanding));
	public string OutstandingCaption => Outstanding > 0m ? "OUTSTANDING" : Outstanding < 0m ? "IN CREDIT (ADVANCE)" : "SETTLED · NOTHING DUE";
	public bool IsOwing => Outstanding > 0m;
	public string Ageing0To30Text => MoneyFormat.Rupees(_account?.Summary.Ageing.Days0To30 ?? 0m);
	public string Ageing31To60Text => MoneyFormat.Rupees(_account?.Summary.Ageing.Days31To60 ?? 0m);
	public string Ageing61To90Text => MoneyFormat.Rupees(_account?.Summary.Ageing.Days61To90 ?? 0m);
	public string AgeingOver90Text => MoneyFormat.Rupees(_account?.Summary.Ageing.Over90 ?? 0m);
	public bool HasOver90 => (_account?.Summary.Ageing.Over90 ?? 0m) > 0m;
	public bool HasCreditLimit => (_account?.Summary.CreditLimit ?? 0m) > 0m;
	public string CreditLimitText => HasCreditLimit
		? $"Credit limit {MoneyFormat.Rupees(_account!.Summary.CreditLimit)} · {(Outstanding > _account.Summary.CreditLimit ? "over the limit by " + MoneyFormat.Rupees(Outstanding - _account.Summary.CreditLimit) : MoneyFormat.Rupees(Math.Max(0m, _account.Summary.CreditLimit - Outstanding)) + " available")}"
		: string.Empty;
	public bool IsOverLimit => HasCreditLimit && Outstanding > _account!.Summary.CreditLimit;
	public string CreditTermsText => _account == null ? string.Empty : $"{_account.Summary.CreditDays} days credit";

	public string WhatsAppToolTip => !HasAccount ? string.Empty
		: HasPhone ? $"Send {AccountName} a WhatsApp reminder of {OutstandingText}"
		: $"No phone number for {AccountName}. Add one to send a reminder.";

	public bool IsAddingPhone
	{
		get => _isAddingPhone;
		private set => SetProperty(ref _isAddingPhone, value);
	}

	public string NewPhone
	{
		get => _newPhone;
		set => SetProperty(ref _newPhone, value ?? string.Empty);
	}

	public string PhoneError
	{
		get => _phoneError;
		private set => SetProperty(ref _phoneError, value);
	}

	public string AddPhoneLabel => HasPhone ? "Change phone number" : "Add phone number";

	/// <summary>A problem with Receive Payment, shown under it.</summary>
	public string ReceiptError
	{
		get => _receiptError;
		private set => SetProperty(ref _receiptError, value);
	}

	public bool IsChequePayment => string.Equals(PaymentMethod, "Cheque", StringComparison.OrdinalIgnoreCase);

	public string NewNoteText
	{
		get => _newNoteText;
		set => SetProperty(ref _newNoteText, value ?? string.Empty);
	}

	public string NoteError
	{
		get => _noteError;
		private set => SetProperty(ref _noteError, value);
	}

	public string BillMessage
	{
		get => _billMessage;
		private set => SetProperty(ref _billMessage, value);
	}

	public DateTime? AccountFrom
	{
		get => _accountFrom;
		set
		{
			if (SetProperty(ref _accountFrom, value))
			{
				ShowLedger();
			}
		}
	}

	public DateTime? AccountTo
	{
		get => _accountTo;
		set
		{
			if (SetProperty(ref _accountTo, value))
			{
				ShowLedger();
			}
		}
	}

	public string LedgerEmptyText => _account == null || _account.Ledger.Count == 0
		? "No entries on this account yet. Credit bills, payments and returns appear here as they happen."
		: "No entries between these dates. Clear the dates to see the whole statement.";

	public bool HasLedgerRows => AccountLedger.Count > 0;
	public bool HasBills => AccountBills.Count > 0;
	public bool HasPayments => AccountPayments.Count > 0;
	public bool HasCredits => AccountCredits.Count > 0;
	public bool HasNotes => AccountNotes.Count > 0;

	public IRelayCommand AddPhoneCommand => _addPhoneCommand ??= new RelayCommand(() =>
	{
		NewPhone = _account?.Summary.Phone ?? string.Empty;
		PhoneError = string.Empty;
		IsAddingPhone = true;
	}, () => HasAccount);

	public IRelayCommand CancelPhoneCommand => _cancelPhoneCommand ??= new RelayCommand(() =>
	{
		IsAddingPhone = false;
		PhoneError = string.Empty;
	});

	public IAsyncRelayCommand SavePhoneCommand => _savePhoneCommand ??= new AsyncRelayCommand(SavePhoneAsync);

	/// <summary>WhatsApp reminder for the open account: greyed out (with the reason in its tooltip) when there is no phone.</summary>
	public IRelayCommand SendReminderCommand => _sendReminderCommand ??= new RelayCommand(SendReminder, () => HasAccount && HasPhone && IsOwing);

	public IAsyncRelayCommand AddNoteCommand => _addNoteCommand ??= new AsyncRelayCommand(AddNoteAsync);

	public IAsyncRelayCommand<AccountBillRow?> OpenBillCommand => _openBillCommand ??= new AsyncRelayCommand<AccountBillRow?>(row => ShowBillAsync(row, print: false));

	public IAsyncRelayCommand<AccountBillRow?> ReprintBillCommand => _reprintBillCommand ??= new AsyncRelayCommand<AccountBillRow?>(row => ShowBillAsync(row, print: true));

	public IRelayCommand ClearAccountRangeCommand => _clearAccountRangeCommand ??= new RelayCommand(() =>
	{
		_accountFrom = null;
		_accountTo = null;
		OnPropertyChanged(nameof(AccountFrom));
		OnPropertyChanged(nameof(AccountTo));
		ShowLedger();
	});

	private void OnCustomerSelectedForAccount(Customer? customer)
	{
		IsAddingPhone = false;
		PhoneError = ReceiptError = NoteError = BillMessage = string.Empty;
		// The last customer's message does not carry over to this one.
		StatusMessage = string.Empty;
		if (customer == null)
		{
			ShowAccount(null);
			return;
		}
		if (SelectedDue?.Ageing.CustomerId != customer.Id)
		{
			_selectedDue = Dues.FirstOrDefault(row => row.Ageing.CustomerId == customer.Id);
			OnPropertyChanged(nameof(SelectedDue));
		}
		AccountLoading = LoadAccountAsync(customer.Id);
	}

	private async Task LoadAccountAsync(Guid customerId)
	{
		try
		{
			using IServiceScope scope = _scopeFactory.CreateScope();
			CustomerAccount account = await new CustomerAccountService(scope.ServiceProvider.GetRequiredService<PharmaBill.Data.Persistence.PharmaBillDbContext>())
				.GetAccountAsync(customerId, BusinessClock.Today);
			if (SelectedCustomer?.Id == customerId)
			{
				ShowAccount(account);
			}
		}
		catch (Exception ex)
		{
			ErrorMessage = "The account could not be opened: " + PharmaBill.Core.UserMessage.Of(ex);
		}
	}

	private void ShowAccount(CustomerAccount? account)
	{
		_account = account;
		AccountBills.Clear();
		AccountPayments.Clear();
		AccountCredits.Clear();
		AccountNotes.Clear();
		if (account != null)
		{
			foreach (AccountBill bill in account.Bills)
			{
				AccountBills.Add(new AccountBillRow(bill));
			}
			foreach (AccountPayment payment in account.Payments)
			{
				AccountPayments.Add(new AccountPaymentRow(payment));
			}
			foreach (AccountCredit credit in account.Credits)
			{
				AccountCredits.Add(new AccountCreditRow(credit));
			}
			foreach (AccountNote note in account.Notes)
			{
				AccountNotes.Add(new AccountNoteRow(note));
			}
		}
		ShowLedger();
		foreach (string name in new[]
		{
			nameof(HasAccount), nameof(HasNoAccount), nameof(AccountName), nameof(HasPhone), nameof(PhoneText), nameof(Outstanding), nameof(OutstandingText),
			nameof(OutstandingCaption), nameof(IsOwing), nameof(Ageing0To30Text), nameof(Ageing31To60Text), nameof(Ageing61To90Text), nameof(AgeingOver90Text),
			nameof(HasOver90), nameof(HasCreditLimit), nameof(CreditLimitText), nameof(IsOverLimit), nameof(CreditTermsText), nameof(WhatsAppToolTip),
			nameof(AddPhoneLabel), nameof(HasBills), nameof(HasPayments), nameof(HasCredits), nameof(HasNotes),
		})
		{
			OnPropertyChanged(name);
		}
		_sendReminderCommand?.NotifyCanExecuteChanged();
		_addPhoneCommand?.NotifyCanExecuteChanged();
	}

	// Newest first; the running balance on each line is the account's balance after it.
	private void ShowLedger()
	{
		AccountLedger.Clear();
		if (_account != null)
		{
			DateOnly? from = AccountFrom is DateTime f ? DateOnly.FromDateTime(f) : null;
			DateOnly? to = AccountTo is DateTime t ? DateOnly.FromDateTime(t) : null;
			foreach (AccountLedgerLine line in _account.Ledger.Reverse())
			{
				DateOnly? date = line.AtUtc?.ToBusinessDate();
				bool inRange = date == null ? from == null : (from == null || date >= from) && (to == null || date <= to);
				if (inRange)
				{
					AccountLedger.Add(new AccountLedgerRow(line));
				}
			}
		}
		OnPropertyChanged(nameof(HasLedgerRows));
		OnPropertyChanged(nameof(LedgerEmptyText));
	}

	private async Task SavePhoneAsync()
	{
		if (SelectedCustomer is not Customer customer)
		{
			return;
		}
		try
		{
			using IServiceScope scope = _scopeFactory.CreateScope();
			string saved = await new CustomerAccountService(scope.ServiceProvider.GetRequiredService<PharmaBill.Data.Persistence.PharmaBillDbContext>())
				.SetPhoneAsync(customer.Id, NewPhone, _session.User?.Id);
			customer.Phone = saved;
			IsAddingPhone = false;
			PhoneError = string.Empty;
			StatusMessage = "Phone number saved for " + customer.Name + ".";
			await ReloadDuesAsync();
			await LoadAccountAsync(customer.Id);
		}
		catch (InvalidOperationException ex)
		{
			PhoneError = ex.Message;
		}
	}

	private void SendReminder()
	{
		if (!HasAccount || !HasPhone)
		{
			return;
		}
		string digits = new(_account!.Summary.Phone!.Where(char.IsDigit).ToArray());
		string message = Uri.EscapeDataString($"Dear {AccountName}, our records show an outstanding balance of {OutstandingText}. Please contact us to settle your account.");
		OpenUrl("https://wa.me/" + (digits.Length == 10 ? "91" + digits : digits) + "?text=" + message);
		StatusMessage = "WhatsApp opened with a reminder for " + AccountName + ".";
	}

	private async Task AddNoteAsync()
	{
		if (SelectedCustomer is not Customer customer || _session.User is not AppUser user)
		{
			NoteError = "Sign in to add a note.";
			return;
		}
		try
		{
			using IServiceScope scope = _scopeFactory.CreateScope();
			AccountNote note = await new CustomerAccountService(scope.ServiceProvider.GetRequiredService<PharmaBill.Data.Persistence.PharmaBillDbContext>())
				.AddNoteAsync(customer.Id, NewNoteText, user);
			AccountNotes.Insert(0, new AccountNoteRow(note));
			NewNoteText = string.Empty;
			NoteError = string.Empty;
			OnPropertyChanged(nameof(HasNotes));
		}
		catch (InvalidOperationException ex)
		{
			NoteError = ex.Message;
		}
	}

	private async Task ShowBillAsync(AccountBillRow? row, bool print)
	{
		if (row == null)
		{
			return;
		}
		try
		{
			using IServiceScope scope = _scopeFactory.CreateScope();
			if (row.Bill.IsRetail)
			{
				IInvoicePrintService invoices = scope.ServiceProvider.GetRequiredService<IInvoicePrintService>();
				if (print)
				{
					await invoices.PrintAsync(row.Bill.Id);
				}
				else
				{
					await invoices.PreviewAsync(row.Bill.Id);
				}
			}
			else
			{
				string path = await scope.ServiceProvider.GetRequiredService<WholesaleInvoiceDocumentService>().ExportAsync(row.Bill.Id, deliveryChallan: false);
				OpenDocument(path, print);
			}
			BillMessage = (print ? "Sent " : "Opened ") + row.Number + (print ? " to the printer." : ".");
		}
		catch (Exception ex)
		{
			BillMessage = row.Number + " could not be " + (print ? "reprinted" : "opened") + ": " + PharmaBill.Core.UserMessage.Of(ex);
		}
	}

	/// <summary>The dues list: from the customer ledger at the retail desk (retail credit bills), from invoices at the wholesale desk.</summary>
	private async Task ReloadDuesAsync(CancellationToken cancellationToken = default)
	{
		using IServiceScope scope = _scopeFactory.CreateScope();
		IReadOnlyList<CustomerAgeing> ageing = _isRetailMode
			? await new CustomerAccountService(scope.ServiceProvider.GetRequiredService<PharmaBill.Data.Persistence.PharmaBillDbContext>()).GetDuesAsync(BusinessClock.Today, cancellationToken)
			: await scope.ServiceProvider.GetRequiredService<WholesaleAccountsService>().GetAgeingAsync(BusinessClock.Today, cancellationToken);
		Ageing.Clear();
		Dues.Clear();
		foreach (CustomerAgeing item in ageing)
		{
			Ageing.Add(item);
			Dues.Add(new DueRow(item));
		}
		_selectedDue = SelectedCustomer == null ? null : Dues.FirstOrDefault(row => row.Ageing.CustomerId == SelectedCustomer.Id);
		OnPropertyChanged(nameof(SelectedDue));
		OnPropertyChanged(nameof(HasDues));
	}

	/// <summary>A statement of the account as shown (the date range applies), oldest first, dates in IST.</summary>
	private async Task ExportAccountStatementAsync()
	{
		if (!HasAccount)
		{
			ReceiptError = "Select a customer before exporting a statement.";
			return;
		}
		string? destination = _filePicker.PickExportDestination("pdf", "Statement-" + AccountName);
		if (destination == null)
		{
			return;
		}
		IReadOnlyList<string>[] rows = AccountLedger.Reverse()
			.Select(row => (IReadOnlyList<string>)new[] { row.DateText, row.Kind, row.Reference, row.DebitText, row.CreditText, row.BalanceText, row.Notes })
			.ToArray();
		string range = AccountFrom == null && AccountTo == null ? "all entries"
			: $"{AccountFrom?.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture) ?? "start"} to {AccountTo?.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture) ?? "today"}";
		await _exports.ExportAsync(destination, $"Statement: {AccountName} · {PhoneText} · {OutstandingCaption.ToLowerInvariant()} {OutstandingText} · {range}",
			["Date (IST)", "Type", "Reference", "Debit", "Credit", "Balance", "Notes"], rows);
		StatusMessage = "Statement saved: " + destination;
	}
}
