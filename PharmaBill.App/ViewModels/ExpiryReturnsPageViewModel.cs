using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PharmaBill.App.Services;
using PharmaBill.Core;
using PharmaBill.Core.Entities;
using PharmaBill.Core.Inventory;
using PharmaBill.Data.Persistence;
using PharmaBill.Data.Services;

namespace PharmaBill.App.ViewModels;

/// <summary>A supplier to pick for batches with no traceable purchase.</summary>
public sealed record ExpirySupplierChoice(Guid Id, string Name);

/// <summary>One batch on the Claimable now tab: tick it and say how much.</summary>
public sealed partial class ClaimableItem : ObservableObject
{
	private bool _isSelected;
	private string _claimQuantity;

	public ClaimableItem(ClaimableBatchRow row)
	{
		Row = row;
		_claimQuantity = row.Available.ToString("0.##", CultureInfo.InvariantCulture);
	}

	public ClaimableBatchRow Row { get; }

	public string ExpiryText => Row.Expiry.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture);

	public string DaysText => Row.DaysToExpiry < 0 ? $"expired {-Row.DaysToExpiry} d ago" : Row.DaysToExpiry == 0 ? "expires today" : $"{Row.DaysToExpiry} d left";

	public string AvailableText => Row.Available.ToString("0.##", CultureInfo.InvariantCulture);

	public string RateText => Row.Rate.ToString("N2", ExpiryReturnsPageViewModel.Indian);

	public string GstText => Row.GstRate.ToString("0.##", CultureInfo.InvariantCulture) + "%";

	public string ValueText => Row.ValueAtRate.ToString("N2", ExpiryReturnsPageViewModel.Indian);

	public bool IsSelected
	{
		get => _isSelected;
		set
		{
			if (SetProperty(ref _isSelected, value))
			{
				Changed?.Invoke();
			}
		}
	}

	/// <summary>Part quantity: all that is available unless the pharmacist types less.</summary>
	public string ClaimQuantity
	{
		get => _claimQuantity;
		set
		{
			if (SetProperty(ref _claimQuantity, value))
			{
				Changed?.Invoke();
			}
		}
	}

	public decimal? Quantity => decimal.TryParse(ClaimQuantity, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal quantity) ? quantity : null;

	internal Action? Changed { get; set; }
}

/// <summary>A supplier's claimable batches, with one button to build a draft claim for them.</summary>
public sealed partial class ClaimableGroup : ObservableObject
{
	private ExpirySupplierChoice? _chosenSupplier;
	private string _selectedText = string.Empty;

	public ClaimableGroup(Guid? supplierId, string heading, string policyText, IEnumerable<ClaimableItem> items, IAsyncRelayCommand<ClaimableGroup> build)
	{
		SupplierId = supplierId;
		Heading = heading;
		PolicyText = policyText;
		Items = new ObservableCollection<ClaimableItem>(items);
		BuildCommand = build;
		foreach (ClaimableItem item in Items)
		{
			item.Changed = Refresh;
		}
		Refresh();
	}

	public Guid? SupplierId { get; }

	public string Heading { get; }

	public string PolicyText { get; }

	/// <summary>Batches with no purchase to trace them to a supplier: the pharmacist picks the supplier.</summary>
	public bool IsUntraceable => SupplierId == null;

	public ObservableCollection<ClaimableItem> Items { get; }

	public IAsyncRelayCommand<ClaimableGroup> BuildCommand { get; }

	public string TotalText => "Claimable value at purchase rate: ₹" + Items.Sum(item => item.Row.ValueAtRate).ToString("N2", ExpiryReturnsPageViewModel.Indian);

	public string SelectedText
	{
		get => _selectedText;
		private set => SetProperty(ref _selectedText, value);
	}

	public ExpirySupplierChoice? ChosenSupplier
	{
		get => _chosenSupplier;
		set => SetProperty(ref _chosenSupplier, value);
	}

	public string BuildLabel => "Build draft claim";

	private void Refresh()
	{
		ClaimableItem[] ticked = Items.Where(item => item.IsSelected).ToArray();
		decimal value = ticked.Sum(item => item.Quantity is decimal quantity ? RetailTaxCalculator.RoundMoney(item.Row.Rate * quantity) : 0m);
		SelectedText = ticked.Length == 0 ? "Tick the batches to claim." : $"{ticked.Length} ticked · ₹{value.ToString("N2", ExpiryReturnsPageViewModel.Indian)} before GST";
	}
}

/// <summary>A claim in the Open or Closed list.</summary>
public sealed record ExpiryClaimListRow(ExpiryClaimSummary Summary)
{
	public string ClaimNo => Summary.ClaimNo;
	public string Supplier => Summary.SupplierName;
	public string ClaimDate => Summary.ClaimDate.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture);
	public string Status => ExpiryReturnService.StatusText(Summary.Status);
	public string SentDate => Summary.SentDate?.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture) ?? "Not sent";
	public string Age => Summary.DaysOutstanding is int days ? days + (days == 1 ? " day" : " days") : "—";
	public string Claimed => Summary.TotalValue.ToString("N2", ExpiryReturnsPageViewModel.Indian);
	public string Credited => Summary.CreditedValue.ToString("N2", ExpiryReturnsPageViewModel.Indian);
	public string Balance => Summary.Balance.ToString("N2", ExpiryReturnsPageViewModel.Indian);
}

/// <summary>One line of the selected claim, with the credit-note and rejection entry fields beside it.</summary>
public sealed partial class ExpiryClaimLineRow : ObservableObject
{
	private string _creditNow = string.Empty;
	private string _shortfallReason = string.Empty;

	public ExpiryClaimLineRow(ExpiryClaimLine line)
	{
		Line = line;
	}

	public ExpiryClaimLine Line { get; }

	public string Item => Line.ItemName;
	public string Pack => Line.Pack;
	public string Hsn => Line.HsnCode;
	public string Batch => Line.BatchNo;
	public string Expiry => Line.BatchExpiry?.ToString("MM/yyyy", CultureInfo.InvariantCulture) ?? "";
	public string Quantity => Line.Quantity.ToString("0.##", CultureInfo.InvariantCulture);
	public string Rate => Line.PurchaseRate.ToString("N2", ExpiryReturnsPageViewModel.Indian);
	public string Gst => Line.GstRate.ToString("0.##", CultureInfo.InvariantCulture) + "%";
	public string Tax => (Line.CgstAmount + Line.SgstAmount + Line.IgstAmount).ToString("N2", ExpiryReturnsPageViewModel.Indian);
	public string Value => Line.LineValue.ToString("N2", ExpiryReturnsPageViewModel.Indian);
	public string Credited => Line.CreditedValue.ToString("N2", ExpiryReturnsPageViewModel.Indian);
	public string Rejected => Line.RejectedQuantity == 0m ? "—" : $"{Line.RejectedQuantity:0.##} ({Line.RejectedValue.ToString("N2", ExpiryReturnsPageViewModel.Indian)})";
	public string Shortfall => Line.ShortfallValue == 0m ? "—" : Line.ShortfallValue.ToString("N2", ExpiryReturnsPageViewModel.Indian) + (string.IsNullOrWhiteSpace(Line.ShortfallReason) ? "" : " · " + Line.ShortfallReason);
	public decimal OpenValue => ExpiryClaimMath.Open(Line);
	public string Open => OpenValue.ToString("N2", ExpiryReturnsPageViewModel.Indian);
	public decimal HeldRejected => ExpiryClaimMath.HeldRejected(Line);

	public string State => Line.State switch
	{
		ExpiryClaimLineState.Held => "Held for return",
		ExpiryClaimLineState.Sent => HeldRejected > 0m ? $"Sent · {HeldRejected:0.##} rejected, held" : "Sent",
		ExpiryClaimLineState.RejectedWrittenOff => "Rejected · written off" + (Line.WrittenOffByName is { Length: > 0 } name ? $" by {name} on {Line.WrittenOffAtUtc?.ToBusinessDate():dd-MMM-yyyy}" : ""),
		ExpiryClaimLineState.RejectedHeld => $"Rejected · {HeldRejected:0.##} held in the shop",
		ExpiryClaimLineState.Released => "Back to saleable stock",
		_ => Line.State.ToString()
	};

	/// <summary>Credit-note entry: how much of the credit note covers this line.</summary>
	public string CreditNow
	{
		get => _creditNow;
		set => SetProperty(ref _creditNow, value);
	}

	/// <summary>Credit-note entry: the reason the rest of this line will not be paid (blank: the rest is still owed).</summary>
	public string ShortfallReason
	{
		get => _shortfallReason;
		set => SetProperty(ref _shortfallReason, value);
	}
}

/// <summary>A credit note received, as shown under the claim (with its own GST beside the claim's).</summary>
public sealed record ExpiryCreditRow(ExpiryClaimCredit Credit)
{
	public string Number => Credit.CreditNoteNo;
	public string Date => Credit.CreditNoteDate.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture);
	public string Amount => Credit.Amount.ToString("N2", ExpiryReturnsPageViewModel.Indian);
	public string Gst => $"Taxable {Credit.TaxableValue.ToString("N2", ExpiryReturnsPageViewModel.Indian)} · CGST {Credit.CgstAmount:0.00} · SGST {Credit.SgstAmount:0.00} · IGST {Credit.IgstAmount:0.00}";
	public string Settlement => Credit.Settlement == ExpiryCreditSettlement.Cash ? "Cash" : "Adjusted against purchases";
	public string RecordedBy => Credit.RecordedByName;
}

/// <summary>
/// Expiry Returns: what can be claimed now, grouped by distributor; the open claims oldest first; the closed ones; and,
/// for the selected claim, sending, credit notes, rejections, printing and export.
/// </summary>
public sealed partial class ExpiryReturnsPageViewModel : ObservableObject, ILoadablePage
{
	internal static readonly CultureInfo Indian = CultureInfo.GetCultureInfo("en-IN");

	private readonly IServiceScopeFactory _scopeFactory;
	private readonly CurrentSession _session;
	private readonly IFilePickerService _picker;
	private readonly TabularExportService _exports;
	private readonly IConfirmationService _confirm;

	private int _selectedTab;
	private string _defaultDaysBefore = string.Empty;
	private string _defaultDaysAfter = string.Empty;
	private string _heldSummary = string.Empty;
	private string _statusMessage = string.Empty;
	private string _errorMessage = string.Empty;
	private bool _isBusy;
	private ExpiryClaimListRow? _selectedOpenClaim;
	private ExpiryClaimListRow? _selectedClosedClaim;
	private ExpiryClaim? _claim;
	private ExpiryClaimLineRow? _selectedLine;
	private string _sendVia = "By hand";
	private string _sendTo = string.Empty;
	private DateTime? _sendDate = BusinessClock.Today.ToDateTime(TimeOnly.MinValue);
	private string _voidReason = string.Empty;
	private string _creditNoteNo = string.Empty;
	private DateTime? _creditNoteDate = BusinessClock.Today.ToDateTime(TimeOnly.MinValue);
	private string _creditAmount = string.Empty;
	private string _creditTaxable = string.Empty;
	private string _creditCgst = string.Empty;
	private string _creditSgst = string.Empty;
	private string _creditIgst = string.Empty;
	private bool _creditIsCash;
	private string _rejectQuantity = string.Empty;
	private string _rejectReason = string.Empty;
	private string _writeOffReason = string.Empty;

	public ExpiryReturnsPageViewModel(IServiceScopeFactory scopeFactory, CurrentSession session, IFilePickerService picker, TabularExportService exports, IConfirmationService confirm)
	{
		_scopeFactory = scopeFactory;
		_session = session;
		_picker = picker;
		_exports = exports;
		_confirm = confirm;
		BuildClaimCommand = new AsyncRelayCommand<ClaimableGroup>(BuildClaimAsync);
		RefreshCommand = new AsyncRelayCommand(() => LoadAsync());
		SaveDefaultWindowCommand = new AsyncRelayCommand(SaveDefaultWindowAsync);
		MarkSentCommand = new AsyncRelayCommand(MarkSentAsync);
		VoidCommand = new AsyncRelayCommand(VoidAsync);
		SpreadCreditCommand = new RelayCommand(SpreadCredit);
		RecordCreditCommand = new AsyncRelayCommand(RecordCreditAsync);
		RejectWriteOffCommand = new AsyncRelayCommand(() => RejectAsync(RejectedGoodsAction.WriteOff));
		RejectHoldCommand = new AsyncRelayCommand(() => RejectAsync(RejectedGoodsAction.ReturnToHeldStock));
		WriteOffHeldCommand = new AsyncRelayCommand(WriteOffHeldAsync);
		ReleaseHeldCommand = new AsyncRelayCommand(ReleaseHeldAsync);
		WriteOffBalanceCommand = new AsyncRelayCommand(WriteOffBalanceAsync);
		PrintCommand = new AsyncRelayCommand(PrintAsync);
		SavePdfCommand = new AsyncRelayCommand(SavePdfAsync);
		ExportExcelCommand = new AsyncRelayCommand(ExportExcelAsync);
	}

	public string ItcNote => ExpiryReturnService.ItcNote;

	public ObservableCollection<ClaimableGroup> ClaimableGroups { get; } = [];
	public ObservableCollection<ExpirySupplierChoice> Suppliers { get; } = [];
	public ObservableCollection<ExpiryClaimListRow> OpenClaims { get; } = [];
	public ObservableCollection<ExpiryClaimListRow> ClosedClaims { get; } = [];
	public ObservableCollection<ExpiryClaimLineRow> Lines { get; } = [];
	public ObservableCollection<ExpiryCreditRow> Credits { get; } = [];
	public IReadOnlyList<string> SendMethods { get; } = ["By hand", "Courier", "Distributor's salesman", "Transport"];

	public IAsyncRelayCommand<ClaimableGroup> BuildClaimCommand { get; }
	public IAsyncRelayCommand RefreshCommand { get; }
	public IAsyncRelayCommand SaveDefaultWindowCommand { get; }
	public IAsyncRelayCommand MarkSentCommand { get; }
	public IAsyncRelayCommand VoidCommand { get; }
	public IRelayCommand SpreadCreditCommand { get; }
	public IAsyncRelayCommand RecordCreditCommand { get; }
	public IAsyncRelayCommand RejectWriteOffCommand { get; }
	public IAsyncRelayCommand RejectHoldCommand { get; }
	public IAsyncRelayCommand WriteOffHeldCommand { get; }
	public IAsyncRelayCommand ReleaseHeldCommand { get; }
	public IAsyncRelayCommand WriteOffBalanceCommand { get; }
	public IAsyncRelayCommand PrintCommand { get; }
	public IAsyncRelayCommand SavePdfCommand { get; }
	public IAsyncRelayCommand ExportExcelCommand { get; }

	public int SelectedTab { get => _selectedTab; set => SetProperty(ref _selectedTab, value); }
	public string DefaultDaysBefore { get => _defaultDaysBefore; set => SetProperty(ref _defaultDaysBefore, value); }
	public string DefaultDaysAfter { get => _defaultDaysAfter; set => SetProperty(ref _defaultDaysAfter, value); }
	public string HeldSummary { get => _heldSummary; private set => SetProperty(ref _heldSummary, value); }
	public string StatusMessage { get => _statusMessage; private set => SetProperty(ref _statusMessage, value); }
	public string ErrorMessage { get => _errorMessage; private set => SetProperty(ref _errorMessage, value); }
	public bool IsBusy { get => _isBusy; private set => SetProperty(ref _isBusy, value); }
	public bool HasClaimable => ClaimableGroups.Count > 0;
	public string OpenTabTitle => OpenClaims.Count == 0 ? "Open claims" : $"Open claims ({OpenClaims.Count})";

	public ExpiryClaimListRow? SelectedOpenClaim
	{
		get => _selectedOpenClaim;
		set
		{
			if (SetProperty(ref _selectedOpenClaim, value) && value != null)
			{
				_ = ShowClaimAsync(value.Summary.Id);
			}
		}
	}

	public ExpiryClaimListRow? SelectedClosedClaim
	{
		get => _selectedClosedClaim;
		set
		{
			if (SetProperty(ref _selectedClosedClaim, value) && value != null)
			{
				_ = ShowClaimAsync(value.Summary.Id);
			}
		}
	}

	/// <summary>The claim shown under the lists.</summary>
	public ExpiryClaim? Claim
	{
		get => _claim;
		private set
		{
			SetProperty(ref _claim, value);
			OnPropertyChanged(nameof(HasClaim));
			OnPropertyChanged(nameof(IsDraft));
			OnPropertyChanged(nameof(IsAwaitingCredit));
			OnPropertyChanged(nameof(ClaimHeading));
			OnPropertyChanged(nameof(ClaimSubheading));
			OnPropertyChanged(nameof(ClaimTotals));
			OnPropertyChanged(nameof(ClaimBalance));
			OnPropertyChanged(nameof(ClaimSentText));
		}
	}

	/// <summary>The detail panel shows only once a claim has been loaded.</summary>
	public Task? ClaimLoading { get; private set; }

	public bool HasClaim => Claim != null;
	public bool IsDraft => Claim?.Status == ExpiryClaimStatus.Draft;
	public bool IsAwaitingCredit => Claim?.Status is ExpiryClaimStatus.Sent or ExpiryClaimStatus.PartCredited;
	public string ClaimHeading => Claim == null ? string.Empty : $"{Claim.ClaimNo} · {Claim.SupplierName} · {ExpiryReturnService.StatusText(Claim.Status)}";
	public string ClaimSubheading => Claim == null ? string.Empty
		: $"Claim date {Claim.ClaimDate:dd-MMM-yyyy} · {new ExpiryReturnWindow(Claim.PolicyDaysBeforeExpiry, Claim.PolicyDaysAfterExpiry).Describe()}" + (string.IsNullOrWhiteSpace(Claim.PolicyNote) ? string.Empty : " · Policy: " + Claim.PolicyNote);
	public string ClaimTotals => Claim == null ? string.Empty
		: $"Taxable ₹{Claim.TaxableValue.ToString("N2", Indian)} · " + (Claim.IsInterState ? $"IGST ₹{Claim.IgstAmount.ToString("N2", Indian)}" : $"CGST ₹{Claim.CgstAmount.ToString("N2", Indian)} · SGST ₹{Claim.SgstAmount.ToString("N2", Indian)}") + $" · Claimed ₹{Claim.TotalValue.ToString("N2", Indian)}";
	public string ClaimBalance => Claim == null || Claim.Status is ExpiryClaimStatus.Draft or ExpiryClaimStatus.Voided ? string.Empty
		: $"Credited ₹{Lines.Sum(line => line.Line.CreditedValue).ToString("N2", Indian)} · Still owed ₹{Lines.Sum(line => line.OpenValue).ToString("N2", Indian)}";
	public string ClaimSentText => Claim?.SentDate is DateOnly sent ? $"Sent {sent:dd-MMM-yyyy} · {Claim.SentVia}" + (string.IsNullOrWhiteSpace(Claim.SentTo) ? "" : " · received by " + Claim.SentTo) + (string.IsNullOrWhiteSpace(Claim.SentByName) ? "" : " · recorded by " + Claim.SentByName)
		: Claim?.Status == ExpiryClaimStatus.Voided ? $"Voided by {Claim.VoidedByName}: {Claim.VoidReason}" : string.Empty;

	public ExpiryClaimLineRow? SelectedLine
	{
		get => _selectedLine;
		set
		{
			if (SetProperty(ref _selectedLine, value))
			{
				RejectQuantity = value == null ? string.Empty : (value.Line.Quantity - value.Line.RejectedQuantity).ToString("0.##", CultureInfo.InvariantCulture);
				OnPropertyChanged(nameof(SelectedLineHasHeldRejected));
			}
		}
	}

	public bool SelectedLineHasHeldRejected => SelectedLine?.HeldRejected > 0m;

	public string SendVia { get => _sendVia; set => SetProperty(ref _sendVia, value); }
	public string SendTo { get => _sendTo; set => SetProperty(ref _sendTo, value); }
	public DateTime? SendDate { get => _sendDate; set => SetProperty(ref _sendDate, value); }
	public string VoidReason { get => _voidReason; set => SetProperty(ref _voidReason, value); }
	public string CreditNoteNo { get => _creditNoteNo; set => SetProperty(ref _creditNoteNo, value); }
	public DateTime? CreditNoteDate { get => _creditNoteDate; set => SetProperty(ref _creditNoteDate, value); }
	public string CreditAmount { get => _creditAmount; set => SetProperty(ref _creditAmount, value); }
	public string CreditTaxable { get => _creditTaxable; set => SetProperty(ref _creditTaxable, value); }
	public string CreditCgst { get => _creditCgst; set => SetProperty(ref _creditCgst, value); }
	public string CreditSgst { get => _creditSgst; set => SetProperty(ref _creditSgst, value); }
	public string CreditIgst { get => _creditIgst; set => SetProperty(ref _creditIgst, value); }
	public bool CreditIsCash { get => _creditIsCash; set => SetProperty(ref _creditIsCash, value); }
	public string RejectQuantity { get => _rejectQuantity; set => SetProperty(ref _rejectQuantity, value); }
	public string RejectReason { get => _rejectReason; set => SetProperty(ref _rejectReason, value); }
	public string WriteOffReason { get => _writeOffReason; set => SetProperty(ref _writeOffReason, value); }

	private Guid UserId => _session.User?.Id ?? Guid.Empty;
	private UserRole Role => _session.User?.Role ?? UserRole.Owner;

	// ------------------------------------------------------------------ loading

	public async Task LoadAsync(CancellationToken cancellationToken = default)
	{
		try
		{
			ErrorMessage = string.Empty;
			using IServiceScope scope = _scopeFactory.CreateScope();
			ExpiryReturnService service = scope.ServiceProvider.GetRequiredService<ExpiryReturnService>();
			PharmaBillDbContext context = scope.ServiceProvider.GetRequiredService<PharmaBillDbContext>();
			PharmacyProfile profile = await service.ProfileAsync();
			DefaultDaysBefore = profile.ExpiryReturnDaysBefore.ToString(CultureInfo.InvariantCulture);
			DefaultDaysAfter = profile.ExpiryReturnDaysAfter.ToString(CultureInfo.InvariantCulture);
			List<Supplier> suppliers = await context.Suppliers.AsNoTracking().Where(item => item.IsActive).OrderBy(item => item.Name).ToListAsync();
			Suppliers.Clear();
			foreach (Supplier supplier in suppliers)
			{
				Suppliers.Add(new ExpirySupplierChoice(supplier.Id, supplier.Name));
			}
			Dictionary<Guid, Supplier> byId = suppliers.ToDictionary(item => item.Id);

			ClaimableGroups.Clear();
			foreach (IGrouping<Guid?, ClaimableBatchRow> group in (await service.GetClaimableAsync()).GroupBy(row => row.SupplierId))
			{
				Supplier? supplier = group.Key is Guid id ? byId.GetValueOrDefault(id) : null;
				ExpiryReturnWindow window = ExpiryReturnService.WindowFor(supplier, profile);
				string policy = group.Key == null
					? "No traceable purchase: these batches came in without a purchase bill, so their supplier is not known. Choose the supplier they go back to."
					: window.Describe() + (supplier?.ExpiryReturnDaysBefore == null ? " (the default)" : "") + (string.IsNullOrWhiteSpace(supplier?.ExpiryReturnPolicyNote) ? "" : " · " + supplier!.ExpiryReturnPolicyNote);
				string heading = group.Key == null ? "Supplier not known" : group.First().SupplierName;
				ClaimableGroups.Add(new ClaimableGroup(group.Key, heading, policy, group.Select(row => new ClaimableItem(row)), BuildClaimCommand));
			}
			OnPropertyChanged(nameof(HasClaimable));

			IReadOnlyList<HeldStockRow> held = await service.GetHeldStockAsync();
			HeldSummary = held.Count == 0 ? "Nothing is held for return."
				: $"Held for return (not sellable, still in stock valuation): {held.Sum(row => row.Quantity):0.##} unit(s), ₹{held.Sum(row => row.Value).ToString("N2", Indian)}";

			await ReloadClaimListsAsync(service);
		}
		catch (Exception ex)
		{
			ErrorMessage = PharmaBill.Core.UserMessage.Of(ex);
		}
	}

	private async Task ReloadClaimListsAsync(ExpiryReturnService service)
	{
		Guid? keep = Claim?.Id;
		OpenClaims.Clear();
		foreach (ExpiryClaimSummary row in await service.GetClaimsAsync(open: true))
		{
			OpenClaims.Add(new ExpiryClaimListRow(row));
		}
		ClosedClaims.Clear();
		foreach (ExpiryClaimSummary row in await service.GetClaimsAsync(open: false))
		{
			ClosedClaims.Add(new ExpiryClaimListRow(row));
		}
		OnPropertyChanged(nameof(OpenTabTitle));
		if (keep is Guid id)
		{
			await ShowClaimAsync(id);
		}
	}

	private Task ShowClaimAsync(Guid claimId) => ClaimLoading = ShowClaimCoreAsync(claimId);

	private async Task ShowClaimCoreAsync(Guid claimId)
	{
		try
		{
			using IServiceScope scope = _scopeFactory.CreateScope();
			var (claim, lines, credits) = await scope.ServiceProvider.GetRequiredService<ExpiryReturnService>().GetClaimAsync(claimId);
			Lines.Clear();
			foreach (ExpiryClaimLine line in lines)
			{
				Lines.Add(new ExpiryClaimLineRow(line));
			}
			Credits.Clear();
			foreach (ExpiryClaimCredit credit in credits)
			{
				Credits.Add(new ExpiryCreditRow(credit));
			}
			SelectedLine = null;
			Claim = claim;
		}
		catch (Exception ex)
		{
			ErrorMessage = PharmaBill.Core.UserMessage.Of(ex);
		}
	}

	// ------------------------------------------------------------------ actions

	private async Task RunAsync(string done, Func<ExpiryReturnService, Task> action)
	{
		if (IsBusy)
		{
			return;
		}
		IsBusy = true;
		ErrorMessage = string.Empty;
		StatusMessage = string.Empty;
		try
		{
			using (IServiceScope scope = _scopeFactory.CreateScope())
			{
				await action(scope.ServiceProvider.GetRequiredService<ExpiryReturnService>());
			}
			await LoadAsync();
			StatusMessage = done;
		}
		catch (Exception ex)
		{
			ErrorMessage = PharmaBill.Core.UserMessage.Of(ex);
		}
		finally
		{
			IsBusy = false;
		}
	}

	private async Task SaveDefaultWindowAsync()
	{
		if (!int.TryParse(DefaultDaysBefore, out int before) || !int.TryParse(DefaultDaysAfter, out int after))
		{
			ErrorMessage = "Enter the default window as whole days before and after expiry.";
			return;
		}
		await RunAsync($"Default window saved: {before} days before to {after} days after expiry. Suppliers without their own window follow it.",
			service => service.SaveDefaultWindowAsync(before, after, UserId));
	}

	private async Task BuildClaimAsync(ClaimableGroup? group)
	{
		if (group == null)
		{
			return;
		}
		Guid? supplierId = group.SupplierId ?? group.ChosenSupplier?.Id;
		if (supplierId == null)
		{
			ErrorMessage = "These batches have no traceable purchase. Choose the supplier they go back to first.";
			return;
		}
		ClaimableItem[] ticked = group.Items.Where(item => item.IsSelected).ToArray();
		if (ticked.Length == 0)
		{
			ErrorMessage = "Tick the batches to claim.";
			return;
		}
		ClaimableItem? bad = ticked.FirstOrDefault(item => item.Quantity is not decimal quantity || quantity <= 0m || quantity > item.Row.Available);
		if (bad != null)
		{
			ErrorMessage = $"{bad.Row.ItemName} batch {bad.Row.BatchNo}: enter a quantity from 1 to {bad.AvailableText}.";
			return;
		}
		ExpiryClaim? created = null;
		await RunAsync("Draft claim built. Its quantities are held: not sellable until it is sent or voided.", async service =>
		{
			created = await service.CreateDraftAsync(supplierId.Value, ticked.Select(item => new ExpiryClaimLineRequest(item.Row.BatchId, item.Quantity!.Value)).ToArray(), null, UserId, Role);
		});
		if (created != null)
		{
			StatusMessage = $"Draft claim {created.ClaimNo} built for {created.SupplierName}: ₹{created.TotalValue.ToString("N2", Indian)}. Its quantities are held (not sellable). Print it and mark it sent when the goods go.";
			SelectedTab = 1;
			SelectedOpenClaim = OpenClaims.FirstOrDefault(row => row.Summary.Id == created.Id);
		}
	}

	private async Task MarkSentAsync()
	{
		if (Claim is not ExpiryClaim claim || SendDate is not DateTime date)
		{
			ErrorMessage = "Choose the claim and the date the goods went.";
			return;
		}
		await RunAsync($"{claim.ClaimNo} marked sent. The goods have left stock-in-hand; ₹{claim.TotalValue.ToString("N2", Indian)} is owed by {claim.SupplierName}.",
			service => service.MarkSentAsync(claim.Id, DateOnly.FromDateTime(date), SendVia, SendTo, UserId, Role));
	}

	private async Task VoidAsync()
	{
		if (Claim is not ExpiryClaim claim)
		{
			return;
		}
		if (string.IsNullOrWhiteSpace(VoidReason))
		{
			ErrorMessage = "Say why the draft is being voided.";
			return;
		}
		if (!_confirm.Confirm($"Void draft {claim.ClaimNo}? Its held stock goes back to saleable stock. The claim is kept, marked voided.", "Void expiry claim"))
		{
			return;
		}
		await RunAsync($"{claim.ClaimNo} voided; its stock is back on sale.", service => service.VoidDraftAsync(claim.Id, VoidReason, UserId, Role));
		VoidReason = string.Empty;
	}

	/// <summary>Spreads the credit note's total over the lines in proportion to what is still owed on each.</summary>
	private void SpreadCredit()
	{
		if (!TryMoney(CreditAmount, out decimal amount))
		{
			ErrorMessage = "Enter the credit note's total first.";
			return;
		}
		ExpiryClaimLineRow[] open = Lines.Where(line => line.OpenValue > 0m).ToArray();
		decimal owed = open.Sum(line => line.OpenValue);
		if (owed == 0m)
		{
			return;
		}
		decimal left = Math.Min(amount, owed);
		for (int i = 0; i < open.Length; i++)
		{
			decimal share = i == open.Length - 1 ? left : Math.Min(left, RetailTaxCalculator.RoundMoney(amount * open[i].OpenValue / owed));
			open[i].CreditNow = share.ToString("0.00", CultureInfo.InvariantCulture);
			left -= share;
		}
	}

	private async Task RecordCreditAsync()
	{
		if (Claim is not ExpiryClaim claim || CreditNoteDate is not DateTime date)
		{
			return;
		}
		if (string.IsNullOrWhiteSpace(CreditNoteNo) || !TryMoney(CreditAmount, out decimal amount))
		{
			ErrorMessage = "Enter the credit note number and its total.";
			return;
		}
		TryMoney(CreditTaxable, out decimal taxable);
		TryMoney(CreditCgst, out decimal cgst);
		TryMoney(CreditSgst, out decimal sgst);
		TryMoney(CreditIgst, out decimal igst);
		var lines = new List<ExpiryCreditLineInput>();
		foreach (ExpiryClaimLineRow row in Lines)
		{
			decimal part = 0m;
			if (!string.IsNullOrWhiteSpace(row.CreditNow) && !TryMoney(row.CreditNow, out part))
			{
				ErrorMessage = $"{row.Item} batch {row.Batch}: the credit amount is not a number.";
				return;
			}
			if (part != 0m || !string.IsNullOrWhiteSpace(row.ShortfallReason))
			{
				lines.Add(new ExpiryCreditLineInput(row.Line.Id, part, string.IsNullOrWhiteSpace(row.ShortfallReason) ? null : row.ShortfallReason));
			}
		}
		string settlement = CreditIsCash ? "paid in cash (the supplier ledger is not changed)" : "adjusted against purchases (reduces what you owe the supplier)";
		await RunAsync($"Credit note {CreditNoteNo.Trim()} for ₹{amount.ToString("N2", Indian)} recorded, {settlement}.", service =>
			service.RecordCreditNoteAsync(claim.Id, new ExpiryCreditInput(CreditNoteNo, DateOnly.FromDateTime(date), amount, taxable, cgst, sgst, igst,
				CreditIsCash ? ExpiryCreditSettlement.Cash : ExpiryCreditSettlement.AdjustedAgainstPurchases, lines), UserId, Role));
		if (string.IsNullOrEmpty(ErrorMessage))
		{
			CreditNoteNo = CreditAmount = CreditTaxable = CreditCgst = CreditSgst = CreditIgst = string.Empty;
		}
	}

	// The pharmacist chooses what happens to rejected goods: these are two separate buttons, never a default.
	private async Task RejectAsync(RejectedGoodsAction action)
	{
		if (SelectedLine is not ExpiryClaimLineRow line)
		{
			ErrorMessage = "Select the claim line the distributor rejected.";
			return;
		}
		if (!decimal.TryParse(RejectQuantity, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal quantity) || string.IsNullOrWhiteSpace(RejectReason))
		{
			ErrorMessage = "Enter the quantity rejected and the distributor's reason.";
			return;
		}
		string outcome = action == RejectedGoodsAction.WriteOff ? "written off" : "taken back into the shop as held stock (not sellable)";
		if (!_confirm.Confirm($"{line.Item} batch {line.Batch}: {quantity:0.##} rejected by the distributor ({RejectReason.Trim()}).\n\nThese goods will be {outcome}. Continue?", "Rejected by distributor"))
		{
			return;
		}
		await RunAsync($"{line.Item} batch {line.Batch}: {quantity:0.##} rejected and {outcome}.", service => service.RejectLineAsync(line.Line.Id, quantity, RejectReason, action, UserId, Role));
		RejectReason = string.Empty;
	}

	private async Task WriteOffHeldAsync()
	{
		if (SelectedLine is not ExpiryClaimLineRow line || line.HeldRejected <= 0m)
		{
			return;
		}
		if (_confirm.Confirm($"Write off the {line.HeldRejected:0.##} rejected unit(s) of {line.Item} batch {line.Batch} held in the shop?", "Write off held goods"))
		{
			await RunAsync($"{line.Item} batch {line.Batch}: held goods written off.", service => service.WriteOffHeldRejectedAsync(line.Line.Id, UserId, Role));
		}
	}

	private async Task ReleaseHeldAsync()
	{
		if (SelectedLine is not ExpiryClaimLineRow line || line.HeldRejected <= 0m)
		{
			return;
		}
		await RunAsync($"{line.Item} batch {line.Batch}: back on sale.", service => service.ReleaseHeldRejectedAsync(line.Line.Id, UserId, Role));
	}

	private async Task WriteOffBalanceAsync()
	{
		if (Claim is not ExpiryClaim claim)
		{
			return;
		}
		if (string.IsNullOrWhiteSpace(WriteOffReason))
		{
			ErrorMessage = "Say why the rest will not be recovered.";
			return;
		}
		if (_confirm.Confirm($"Write off what is still owed on {claim.ClaimNo}? The claim closes.", "Write off balance"))
		{
			await RunAsync($"{claim.ClaimNo}: balance written off; the claim is closed.", service => service.WriteOffBalanceAsync(claim.Id, WriteOffReason, UserId, Role));
			WriteOffReason = string.Empty;
		}
	}

	// ------------------------------------------------------------------ printing and export

	private async Task<string?> PdfAsync(string? destination)
	{
		if (Claim is not ExpiryClaim claim)
		{
			return null;
		}
		return await new ExpiryClaimDocumentService(_scopeFactory).ExportPdfAsync(claim.Id, destination);
	}

	private async Task PrintAsync()
	{
		try
		{
			string? path = await PdfAsync(null);
			if (path != null)
			{
				PurchaseReturnDocumentService.Print(path);
				StatusMessage = "Claim statement sent to the printer.";
			}
		}
		catch (Exception ex)
		{
			ErrorMessage = PharmaBill.Core.UserMessage.Of(ex);
		}
	}

	private async Task SavePdfAsync()
	{
		if (Claim is not ExpiryClaim claim)
		{
			return;
		}
		try
		{
			string? destination = _picker.PickExportDestination("pdf", "Expiry claim " + FileSafe(claim.ClaimNo));
			if (destination != null)
			{
				await PdfAsync(destination);
				StatusMessage = "Claim statement saved: " + destination;
			}
		}
		catch (Exception ex)
		{
			ErrorMessage = PharmaBill.Core.UserMessage.Of(ex);
		}
	}

	private async Task ExportExcelAsync()
	{
		if (Claim is not ExpiryClaim claim)
		{
			return;
		}
		try
		{
			string? destination = _picker.PickExportDestination("xlsx", "Expiry claim " + FileSafe(claim.ClaimNo));
			if (destination == null)
			{
				return;
			}
			string[] columns = ["#", "Product", "Pack", "HSN", "Batch", "Expiry", "Qty", "Rate", "Taxable", "GST %", "CGST", "SGST", "IGST", "Value", "Credited", "Status"];
			var rows = Lines.Select((line, index) => (IReadOnlyList<string>)[
				(index + 1).ToString(CultureInfo.InvariantCulture), line.Item, line.Pack, line.Hsn, line.Batch, line.Expiry, line.Quantity,
				Money(line.Line.PurchaseRate), Money(line.Line.TaxableValue), line.Line.GstRate.ToString("0.##", CultureInfo.InvariantCulture),
				Money(line.Line.CgstAmount), Money(line.Line.SgstAmount), Money(line.Line.IgstAmount), Money(line.Line.LineValue), Money(line.Line.CreditedValue), line.State]).ToList();
			rows.Add(["", "Total", "", "", "", "", "", "", Money(claim.TaxableValue), "", Money(claim.CgstAmount), Money(claim.SgstAmount), Money(claim.IgstAmount), Money(claim.TotalValue), Money(Lines.Sum(line => line.Line.CreditedValue)), ""]);
			await _exports.ExportAsync(destination, $"Expiry claim {claim.ClaimNo} - {claim.SupplierName}", columns, rows);
			StatusMessage = "Claim exported to Excel: " + destination;
		}
		catch (Exception ex)
		{
			ErrorMessage = PharmaBill.Core.UserMessage.Of(ex);
		}

		static string Money(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);
	}

	private static string FileSafe(string text) => string.Concat(text.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '-' : c));

	private static bool TryMoney(string? text, out decimal value)
	{
		value = 0m;
		return !string.IsNullOrWhiteSpace(text) && decimal.TryParse(text.Replace("₹", "").Replace(",", "").Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out value) && value >= 0m;
	}
}
