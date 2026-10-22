using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using PharmaBill.App.Services;
using PharmaBill.Core.Entities;
using PharmaBill.Data.Services;

namespace PharmaBill.App.ViewModels;

/// <summary>Open, converted and cancelled quotations. Converting opens the matching billing page with the lines loaded.</summary>
public sealed class QuotationsPageViewModel(IServiceScopeFactory scopeFactory, CurrentSession session, INavigationService navigation, QuotationConversionHandoff handoff, QuotationDocumentService documents, BillingDeskState desk) : ObservableObject, ILoadablePage
{
	private Quotation? _selectedQuotation;

	private string _statusMessage = string.Empty;

	private string _errorMessage = string.Empty;

	public ObservableCollection<Quotation> Quotations { get; } = new ObservableCollection<Quotation>();

	public ObservableCollection<QuotationItem> SelectedItems { get; } = new ObservableCollection<QuotationItem>();

	public Quotation? SelectedQuotation
	{
		get => _selectedQuotation;
		set
		{
			if (SetProperty(ref _selectedQuotation, value))
			{
				OnPropertyChanged(nameof(CanConvert));
				_ = LoadSelectedItemsAsync();
			}
		}
	}

	public bool CanConvert => SelectedQuotation?.Status == Quotation.OpenStatus;

	public string StatusMessage
	{
		get => _statusMessage;
		set => SetProperty(ref _statusMessage, value);
	}

	public string ErrorMessage
	{
		get => _errorMessage;
		set => SetProperty(ref _errorMessage, value);
	}

	private AsyncRelayCommand? convertCommand;

	private AsyncRelayCommand? cancelCommand;

	private AsyncRelayCommand? printCommand;

	public IAsyncRelayCommand ConvertCommand => convertCommand ??= new AsyncRelayCommand(ConvertAsync);

	public IAsyncRelayCommand CancelCommand => cancelCommand ??= new AsyncRelayCommand(CancelAsync);

	public IAsyncRelayCommand PrintCommand => printCommand ??= new AsyncRelayCommand(PrintAsync);

	public async Task LoadAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		using IServiceScope scope = scopeFactory.CreateScope();
		var list = await scope.ServiceProvider.GetRequiredService<QuotationService>().ListAsync(cancellationToken);
		Quotations.Clear();
		foreach (Quotation quotation in list)
		{
			Quotations.Add(quotation);
		}
	}

	private async Task LoadSelectedItemsAsync()
	{
		SelectedItems.Clear();
		if (SelectedQuotation == null)
		{
			return;
		}

		using IServiceScope scope = scopeFactory.CreateScope();
		foreach (QuotationItem item in await scope.ServiceProvider.GetRequiredService<QuotationService>().GetItemsAsync(SelectedQuotation.Id))
		{
			SelectedItems.Add(item);
		}
	}

	private async Task ConvertAsync()
	{
		ErrorMessage = string.Empty;
		if (SelectedQuotation == null)
		{
			ErrorMessage = "Select a quotation to convert.";
			return;
		}

		try
		{
			using IServiceScope scope = scopeFactory.CreateScope();
			QuotationService service = scope.ServiceProvider.GetRequiredService<QuotationService>();
			Quotation quotation = await service.GetOpenAsync(SelectedQuotation.Id);
			if (quotation.Mode == QuotationService.RetailMode && !desk.CanBillRetail)
			{
				ErrorMessage = "Switch to Retail mode to convert a retail quotation.";
				return;
			}
			if (quotation.Mode == QuotationService.WholesaleMode && !desk.CanBillWholesale)
			{
				ErrorMessage = "Switch to Wholesale mode to convert a wholesale quotation.";
				return;
			}
			var lines = (await service.GetItemsAsync(quotation.Id))
				.Select(item => new QuotationLineInput(item.DrugId, item.BatchId, item.DrugName, item.Quantity, item.FreeQuantity, item.UnitPrice, item.DiscountAmount))
				.ToList();
			handoff.Pending = new QuotationConversionRequest(quotation.Id, quotation.QuoteNo, quotation.Mode, quotation.CustomerId, quotation.PartyName, quotation.PartyPhone, lines);
			navigation.Navigate(quotation.Mode == QuotationService.RetailMode ? "Billing" : "WholesaleBilling");
			StatusMessage = $"Opening {quotation.Mode} billing with quotation {quotation.QuoteNo}. Post the bill to complete the conversion.";
		}
		catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException)
		{
			ErrorMessage = PharmaBill.Core.UserMessage.Of(ex);
		}
	}

	private async Task CancelAsync()
	{
		ErrorMessage = string.Empty;
		if (SelectedQuotation == null)
		{
			ErrorMessage = "Select a quotation to cancel.";
			return;
		}

		try
		{
			using IServiceScope scope = scopeFactory.CreateScope();
			AppUser user = session.User ?? throw new UnauthorizedAccessException("Sign in before cancelling quotations.");
			await scope.ServiceProvider.GetRequiredService<QuotationService>().CancelAsync(SelectedQuotation.Id, user.Id, user.Role);
			StatusMessage = $"Quotation {SelectedQuotation.QuoteNo} cancelled.";
			await LoadAsync();
		}
		catch (Exception ex) when (ex is InvalidOperationException || ex is UnauthorizedAccessException || ex is ArgumentException)
		{
			ErrorMessage = PharmaBill.Core.UserMessage.Of(ex);
		}
	}

	private async Task PrintAsync()
	{
		ErrorMessage = string.Empty;
		if (SelectedQuotation == null)
		{
			ErrorMessage = "Select a quotation to print.";
			return;
		}

		try
		{
			string path = await documents.ExportAsync(SelectedQuotation.Id);
			QuotationDocumentService.Print(path);
			StatusMessage = $"Quotation {SelectedQuotation.QuoteNo} sent to the printer. PDF saved at {path}";
		}
		catch (Exception ex)
		{
			ErrorMessage = "Could not print the quotation: " + PharmaBill.Core.UserMessage.Of(ex);
		}
	}
}
