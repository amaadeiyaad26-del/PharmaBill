using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel.__Internals;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using PharmaBill.App.Services;
using PharmaBill.Data.Services;

namespace PharmaBill.App.ViewModels;

public sealed class RecentBillsViewModel(IServiceScopeFactory scopeFactory, RetailBillPdfService pdfService, CurrentSession currentSession, IUserSessionService userSession, SensitiveAccessService sensitiveAccess, IConfirmationService confirmationService, IPromptService promptService) : ObservableObject
{
	[ObservableProperty]
	private string _searchText = string.Empty;

	[ObservableProperty]
	private string _returnReason = string.Empty;

	[ObservableProperty]
	private bool _restockReturnedItems;

	[ObservableProperty]
	private RetailDocumentType _documentType;

	[ObservableProperty]
	private string _emailRecipient = string.Empty;

	[ObservableProperty]
	[NotifyPropertyChangedFor("CanUnlockSelected")]
	[NotifyPropertyChangedFor("CanVoidSelected")]
	private RecentRetailBill? _selectedBill;

	[ObservableProperty]
	private string _statusMessage = string.Empty;

	[ObservableProperty]
	private string _errorMessage = string.Empty;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? searchCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? unlockBillCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? voidBillCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? issueSalesReturnCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? issueSalesReturnCommandCommand;

	public ObservableCollection<RecentRetailBill> Bills { get; } = new ObservableCollection<RecentRetailBill>();

	public ObservableCollection<RetailSaleReturnableLine> ReturnableLines { get; } = new ObservableCollection<RetailSaleReturnableLine>();

	public IReadOnlyList<RetailDocumentType> DocumentTypes { get; } = Enum.GetValues<RetailDocumentType>();

	public bool CanUnlockSelected => SelectedBill?.IsLocked ?? false;

	public bool CanVoidSelected => (object)SelectedBill != null;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string SearchText
	{
		get
		{
			return _searchText;
		}
		[MemberNotNull("_searchText")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_searchText, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.SearchText);
				_searchText = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.SearchText);
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
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.ReturnReason);
				_returnReason = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.ReturnReason);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool RestockReturnedItems
	{
		get
		{
			return _restockReturnedItems;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_restockReturnedItems, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.RestockReturnedItems);
				_restockReturnedItems = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.RestockReturnedItems);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public RetailDocumentType DocumentType
	{
		get
		{
			return _documentType;
		}
		set
		{
			if (!EqualityComparer<RetailDocumentType>.Default.Equals(_documentType, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.DocumentType);
				_documentType = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.DocumentType);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string EmailRecipient
	{
		get
		{
			return _emailRecipient;
		}
		[MemberNotNull("_emailRecipient")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_emailRecipient, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.EmailRecipient);
				_emailRecipient = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.EmailRecipient);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public RecentRetailBill? SelectedBill
	{
		get
		{
			return _selectedBill;
		}
		set
		{
			if (!EqualityComparer<RecentRetailBill>.Default.Equals(_selectedBill, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.SelectedBill);
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.CanUnlockSelected);
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.CanVoidSelected);
				_selectedBill = value;
				OnSelectedBillChanged(value);
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.SelectedBill);
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.CanUnlockSelected);
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.CanVoidSelected);
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

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand SearchCommand => searchCommand ?? (searchCommand = new AsyncRelayCommand(SearchAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand UnlockBillCommand => unlockBillCommand ?? (unlockBillCommand = new AsyncRelayCommand(UnlockBillAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand VoidBillCommand => voidBillCommand ?? (voidBillCommand = new AsyncRelayCommand(VoidBillAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand IssueSalesReturnCommand => issueSalesReturnCommand ?? (issueSalesReturnCommand = new AsyncRelayCommand(IssueSalesReturnAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand IssueSalesReturnCommandCommand => issueSalesReturnCommandCommand ?? (issueSalesReturnCommandCommand = new AsyncRelayCommand(IssueSalesReturnCommandAsync));

	public async Task LoadReturnableLinesAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		ReturnableLines.Clear();
		if ((object)SelectedBill == null)
		{
			return;
		}
		try
		{
			using IServiceScope scope = scopeFactory.CreateScope();
			foreach (RetailSaleReturnableLine item in await scope.ServiceProvider.GetRequiredService<RetailBillingService>().GetSaleReturnLinesAsync(SelectedBill.SaleId, cancellationToken))
			{
				ReturnableLines.Add(item);
			}
			ErrorMessage = string.Empty;
		}
		catch (Exception ex)
		{
			ErrorMessage = ex.Message;
		}
	}

	public async Task LoadAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		try
		{
			using IServiceScope scope = scopeFactory.CreateScope();
			IReadOnlyList<RecentRetailBill> readOnlyList = await scope.ServiceProvider.GetRequiredService<RetailBillingService>().SearchRecentBillsAsync(SearchText, cancellationToken);
			Bills.Clear();
			foreach (RecentRetailBill item in readOnlyList)
			{
				Bills.Add(item);
			}
			StatusMessage = $"{Bills.Count} bill(s) found.";
			ErrorMessage = string.Empty;
		}
		catch (Exception ex)
		{
			ErrorMessage = ex.Message;
		}
	}

	[RelayCommand]
	private Task SearchAsync()
	{
		return LoadAsync();
	}

	public async Task ExportPdfAsync(string path, CancellationToken cancellationToken = default(CancellationToken))
	{
		if ((object)SelectedBill == null)
		{
			throw new InvalidOperationException("Select a bill first.");
		}
		await pdfService.ExportAsync(SelectedBill.SaleId, path, DocumentType, cancellationToken);
	}

	public async Task PreviewSelectedAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		if ((object)SelectedBill == null)
		{
			throw new InvalidOperationException("Select a bill first.");
		}
		await pdfService.PreviewAsync(SelectedBill.SaleId, DocumentType, cancellationToken);
	}

	public async Task PrintSelectedAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		if ((object)SelectedBill == null)
		{
			throw new InvalidOperationException("Select a bill first.");
		}
		await pdfService.PrintAsync(SelectedBill.SaleId, DocumentType, cancellationToken);
	}

	public async Task ShareSelectedToWhatsAppAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		if ((object)SelectedBill == null)
		{
			throw new InvalidOperationException("Select a bill first.");
		}
		await pdfService.ShareWhatsAppAsync(SelectedBill.SaleId, DocumentType, cancellationToken);
	}

	public async Task EmailSelectedAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		if ((object)SelectedBill == null)
		{
			throw new InvalidOperationException("Select a bill first.");
		}
		await pdfService.EmailAsync(SelectedBill.SaleId, DocumentType, EmailRecipient);
	}

	[RelayCommand]
	private async Task UnlockBillAsync()
	{
		if ((object)SelectedBill == null || !SelectedBill.IsLocked || !confirmationService.Confirm("Unlock finalized bill " + SelectedBill.InvoiceNo + " for editing?\nAn Admin override is required.", "Admin override required"))
		{
			return;
		}
		Window mainWindow = Application.Current.MainWindow;
		var (flag, admin) = await sensitiveAccess.RequestAdminOverrideAsync(mainWindow, "unlock bill " + SelectedBill.InvoiceNo);
		if (!flag || admin == null)
		{
			ErrorMessage = "Admin override was cancelled or denied.";
			return;
		}
		string text = promptService.AskText("Unlock reason", "Enter the reason for unlocking this printed bill (required for the audit trail).", "Reason");
		if (string.IsNullOrWhiteSpace(text))
		{
			ErrorMessage = "Unlock cancelled — a reason is required.";
			return;
		}
		try
		{
			using IServiceScope scope = scopeFactory.CreateScope();
			await scope.ServiceProvider.GetRequiredService<RetailBillingService>().UnlockSaleAsync(SelectedBill.SaleId, admin.Id, admin.Role, text);
			StatusMessage = $"Bill {SelectedBill.InvoiceNo} unlocked by Admin {admin.UserName}.";
			ErrorMessage = string.Empty;
			await LoadAsync();
		}
		catch (Exception ex)
		{
			ErrorMessage = ex.Message;
		}
	}

	[RelayCommand]
	private async Task VoidBillAsync()
	{
		if ((object)SelectedBill == null || !confirmationService.Confirm("Void (delete) finalized bill " + SelectedBill.InvoiceNo + "?\nAn Admin override is required. Stock is not restocked automatically.", "Admin override required"))
		{
			return;
		}
		Window mainWindow = Application.Current.MainWindow;
		var (flag, admin) = await sensitiveAccess.RequestAdminOverrideAsync(mainWindow, "void bill " + SelectedBill.InvoiceNo);
		if (!flag || admin == null)
		{
			ErrorMessage = "Admin override was cancelled or denied.";
			return;
		}
		string text = promptService.AskText("Void reason", "Enter the reason for voiding this bill (required for the audit trail).", "Reason");
		if (string.IsNullOrWhiteSpace(text))
		{
			ErrorMessage = "Void cancelled — a reason is required.";
			return;
		}
		try
		{
			using IServiceScope scope = scopeFactory.CreateScope();
			await scope.ServiceProvider.GetRequiredService<RetailBillingService>().VoidSaleAsync(SelectedBill.SaleId, admin.Id, admin.Role, text);
			StatusMessage = $"Bill {SelectedBill.InvoiceNo} voided by Admin {admin.UserName}.";
			ErrorMessage = string.Empty;
			SelectedBill = null;
			await LoadAsync();
		}
		catch (Exception ex)
		{
			ErrorMessage = ex.Message;
		}
	}

	[RelayCommand]
	private async Task IssueSalesReturnAsync()
	{
		if ((object)SelectedBill == null || currentSession.User == null || string.IsNullOrWhiteSpace(ReturnReason))
		{
			ErrorMessage = "Select a bill, enter return quantities, and provide a reason.";
			return;
		}
		if (SelectedBill.IsLocked && !userSession.CanEditPrintedBills)
		{
			ErrorMessage = "This bill is locked. Unlock it with Admin override before processing returns, or use Admin credentials.";
			return;
		}
		RetailSaleReturnLineInput[] array = (from line in ReturnableLines
			where line.ReturnQuantity > 0m
			select new RetailSaleReturnLineInput(line.SaleItemId, line.ReturnQuantity)).ToArray();
		if (array.Length == 0)
		{
			ErrorMessage = "Enter a return quantity for at least one bill item.";
		}
		else
		{
			if (!confirmationService.Confirm("Issue a credit note for the selected item quantities from " + SelectedBill.InvoiceNo + "?", "Confirm sales return") || (RestockReturnedItems && !confirmationService.Confirm("Confirm that every returned pack is sealed, uncompromised, physically inspected, and not expired. Only then will its quantity be added back to saleable stock.", "Confirm returned stock is saleable")))
			{
				return;
			}
			try
			{
				using IServiceScope scope = scopeFactory.CreateScope();
				RetailSaleReturnResult retailSaleReturnResult = await scope.ServiceProvider.GetRequiredService<RetailBillingService>().IssueSalesReturnAsync(SelectedBill.SaleId, array, ReturnReason, RestockReturnedItems, currentSession.User.Id, currentSession.User.Role);
				ReturnReason = string.Empty;
				RestockReturnedItems = false;
				StatusMessage = $"Credit note {retailSaleReturnResult.ReturnNo} issued for {MoneyFormat.Rupees(retailSaleReturnResult.CreditAmount)}; {retailSaleReturnResult.RestockedQuantity:N2} unit(s) returned to stock.";
				ErrorMessage = string.Empty;
				await LoadReturnableLinesAsync();
			}
			catch (Exception ex)
			{
				ErrorMessage = ex.Message;
			}
		}
	}

	[RelayCommand]
	private Task IssueSalesReturnCommandAsync()
	{
		return IssueSalesReturnAsync();
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnSelectedBillChanged(RecentRetailBill? value)
	{
		LoadReturnableLinesAsync();
	}
}
