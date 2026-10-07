using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
using PharmaBill.Data.Services;

namespace PharmaBill.App.ViewModels;

public class WholesaleReturnsPageViewModel : ObservableObject, ILoadablePage
{
	private readonly IServiceScopeFactory _scopeFactory;

	private readonly CurrentSession _session;

	private WholesaleReturnInvoiceChoice? _selectedInvoice;

	private string _documentNo = string.Empty;

	private string _reason = string.Empty;

	private string _debitAmount = string.Empty;

	private string _errorMessage = string.Empty;

	private string _statusMessage = string.Empty;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? createCreditNoteCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? createDebitNoteCommand;

	public ObservableCollection<WholesaleReturnInvoiceChoice> Invoices { get; } = new ObservableCollection<WholesaleReturnInvoiceChoice>();

	public ObservableCollection<WholesaleCreditLineDraft> InvoiceLines { get; } = new ObservableCollection<WholesaleCreditLineDraft>();

	public ObservableCollection<ExpiryReturnTrackerRow> ExpiryTracker { get; } = new ObservableCollection<ExpiryReturnTrackerRow>();

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public WholesaleReturnInvoiceChoice? SelectedInvoice
	{
		get
		{
			return _selectedInvoice;
		}
		set
		{
			if (!EqualityComparer<WholesaleReturnInvoiceChoice>.Default.Equals(_selectedInvoice, value))
			{
				OnPropertyChanging(nameof(SelectedInvoice));
				_selectedInvoice = value;
				OnSelectedInvoiceChanged(value);
				OnPropertyChanged(nameof(SelectedInvoice));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string DocumentNo
	{
		get
		{
			return _documentNo;
		}
		[MemberNotNull("_documentNo")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_documentNo, value))
			{
				OnPropertyChanging(nameof(DocumentNo));
				_documentNo = value;
				OnPropertyChanged(nameof(DocumentNo));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string Reason
	{
		get
		{
			return _reason;
		}
		[MemberNotNull("_reason")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_reason, value))
			{
				OnPropertyChanging(nameof(Reason));
				_reason = value;
				OnPropertyChanged(nameof(Reason));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string DebitAmount
	{
		get
		{
			return _debitAmount;
		}
		[MemberNotNull("_debitAmount")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_debitAmount, value))
			{
				OnPropertyChanging(nameof(DebitAmount));
				_debitAmount = value;
				OnPropertyChanged(nameof(DebitAmount));
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

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand CreateCreditNoteCommand => createCreditNoteCommand ?? (createCreditNoteCommand = new AsyncRelayCommand(CreateCreditNoteAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand CreateDebitNoteCommand => createDebitNoteCommand ?? (createDebitNoteCommand = new AsyncRelayCommand(CreateDebitNoteAsync));

	public WholesaleReturnsPageViewModel(IServiceScopeFactory scopeFactory, CurrentSession session)
	{
		_scopeFactory = scopeFactory;
		_session = session;
	}

	public async Task LoadAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		using IServiceScope scope = _scopeFactory.CreateScope();
		PharmaBillDbContext requiredService = scope.ServiceProvider.GetRequiredService<PharmaBillDbContext>();
		List<WholesaleReturnInvoiceChoice> list = await (from invoice in requiredService.WholesaleInvoices.AsNoTracking()
			join customer in requiredService.Customers.AsNoTracking() on invoice.CustomerId equals customer.Id
			where invoice.Status == "Posted"
			orderby invoice.InvoiceAtUtc descending
			select new WholesaleReturnInvoiceChoice(invoice.Id, invoice.InvoiceNo, customer.Name, invoice.InvoiceAtUtc)).ToListAsync(cancellationToken);
		Invoices.Clear();
		foreach (WholesaleReturnInvoiceChoice item in list)
		{
			Invoices.Add(item);
		}
		WholesaleReturnsService requiredService2 = scope.ServiceProvider.GetRequiredService<WholesaleReturnsService>();
		ExpiryTracker.Clear();
		foreach (ExpiryReturnTrackerRow item2 in await requiredService2.GetExpiryReturnTrackerAsync(DateOnly.FromDateTime(DateTime.Today.AddDays(90.0)), cancellationToken))
		{
			ExpiryTracker.Add(item2);
		}
	}

	private async Task CreateCreditNoteAsync()
	{
		if ((object)SelectedInvoice == null || string.IsNullOrWhiteSpace(Reason))
		{
			ErrorMessage = "Select a posted invoice and enter a return reason.";
			return;
		}
		WholesaleCreditLineInput[] lines = (from item in InvoiceLines
			where item.Quantity > 0m
			select new WholesaleCreditLineInput(item.InvoiceItemId, item.Quantity, item.Restock)).ToArray();
		try
		{
			using IServiceScope scope = _scopeFactory.CreateScope();
			ReturnNote returnNote = await scope.ServiceProvider.GetRequiredService<WholesaleReturnsService>().CreateCreditNoteAsync(SelectedInvoice.Id, DocumentNo, DateOnly.FromDateTime(DateTime.Today), lines, Reason, (_session.User ?? throw new UnauthorizedAccessException("Sign in before creating credit notes.")).Id, _session.User.Role);
			StatusMessage = $"Credit note {returnNote.ReturnNo} saved for {MoneyFormat.Rupees(returnNote.TotalAmount)}. Unrestocked items are quarantined.";
			ErrorMessage = string.Empty;
			await LoadInvoiceLinesAsync(SelectedInvoice.Id);
		}
		catch (Exception ex) when ((ex is InvalidOperationException || ex is UnauthorizedAccessException || ex is ArgumentException) ? true : false)
		{
			ErrorMessage = ex.Message;
		}
	}

	private async Task CreateDebitNoteAsync()
	{
		if ((object)SelectedInvoice == null || !decimal.TryParse(DebitAmount, out var amount) || string.IsNullOrWhiteSpace(Reason))
		{
			ErrorMessage = "Select an invoice/customer, enter a positive amount and a reason.";
			return;
		}
		try
		{
			using IServiceScope scope = _scopeFactory.CreateScope();
			WholesaleInvoice wholesaleInvoice = await scope.ServiceProvider.GetRequiredService<PharmaBillDbContext>().WholesaleInvoices.SingleAsync((WholesaleInvoice item) => item.Id == SelectedInvoice.Id);
			StatusMessage = "Debit note " + (await scope.ServiceProvider.GetRequiredService<WholesaleReturnsService>().CreateDebitNoteAsync(wholesaleInvoice.CustomerId, DocumentNo, DateOnly.FromDateTime(DateTime.Today), amount, Reason, (_session.User ?? throw new UnauthorizedAccessException("Sign in before creating debit notes.")).Id, _session.User.Role)).ReturnNo + " saved.";
			ErrorMessage = string.Empty;
		}
		catch (Exception ex) when ((ex is InvalidOperationException || ex is UnauthorizedAccessException || ex is ArgumentException) ? true : false)
		{
			ErrorMessage = ex.Message;
		}
	}

	private async Task LoadInvoiceLinesAsync(Guid invoiceId)
	{
		using IServiceScope scope = _scopeFactory.CreateScope();
		PharmaBillDbContext context = scope.ServiceProvider.GetRequiredService<PharmaBillDbContext>();
		var invoiceItems = await (from item in context.WholesaleInvoiceItems.AsNoTracking()
			join drug in context.Drugs.AsNoTracking() on item.DrugId equals drug.Id
			join batch in context.Batches.AsNoTracking() on item.BatchId equals batch.Id
			where item.WholesaleInvoiceId == invoiceId
			select new { item.Id, item.BatchId, item.DrugId, drug.Name, batch.BatchNo, item.Quantity, item.LineTotal }).ToListAsync();
		Dictionary<Guid, decimal> dictionary = await (from item in context.WholesaleReturnItems.AsNoTracking()
			where invoiceItems.Select(line => line.Id).Contains(item.WholesaleInvoiceItemId)
			group item by item.WholesaleInvoiceItemId into @group
			select new
			{
				Id = @group.Key,
				Quantity = @group.Sum((WholesaleReturnItem item) => item.Quantity)
			}).ToDictionaryAsync(item => item.Id, item => item.Quantity);
		InvoiceLines.Clear();
		foreach (var item in invoiceItems)
		{
			decimal num = item.Quantity - dictionary.GetValueOrDefault(item.Id);
			if (!(num <= 0m))
			{
				InvoiceLines.Add(new WholesaleCreditLineDraft
				{
					InvoiceItemId = item.Id,
					BatchId = item.BatchId,
					DrugId = item.DrugId,
					DrugName = item.Name,
					BatchNo = item.BatchNo,
					Remaining = num,
					UnitCredit = ((item.Quantity == 0m) ? 0m : (item.LineTotal / item.Quantity))
				});
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnSelectedInvoiceChanged(WholesaleReturnInvoiceChoice? value)
	{
		if ((object)value != null)
		{
			LoadInvoiceLinesAsync(value.Id);
		}
	}
}
