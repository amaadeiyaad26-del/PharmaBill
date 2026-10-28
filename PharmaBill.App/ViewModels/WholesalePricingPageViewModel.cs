using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PharmaBill.App.Services;
using PharmaBill.Core;
using PharmaBill.Core.Entities;
using PharmaBill.Data.Persistence;
using PharmaBill.Data.Services;

namespace PharmaBill.App.ViewModels;

/// <summary>A batch in the picker: number, expiry and what is in stock.</summary>
public sealed record BatchChoice(Batch Batch, decimal OnHand)
{
	public string Label => $"{Batch.BatchNo}  ·  Exp {(Batch.ExpiryDate is DateOnly expiry ? expiry.ToString("MM/yyyy", CultureInfo.InvariantCulture) : "—")}  ·  {OnHand:0.##} in stock";
}

/// <summary>A customer in the recent-rates picker: found by name or phone.</summary>
public sealed record CustomerChoice(Customer Customer)
{
	public string Label => string.IsNullOrWhiteSpace(Customer.Phone) ? Customer.Name : $"{Customer.Name}  ·  {Customer.Phone}";
}

/// <summary>A line of saved customer pricing, as shown.</summary>
public sealed record SavedPricingRow(string Kind, string Category, string Value, string Period);

/// <summary>A recent sale to the chosen customer, as shown: date in IST.</summary>
public sealed record RecentRateRow(WholesaleRateHistoryRow Rate)
{
	public string SoldOn => Rate.SoldAtUtc.ToBusinessLocal().ToString("dd-MMM-yyyy  hh:mm tt", CultureInfo.InvariantCulture);
	public string InvoiceNo => Rate.InvoiceNo;
	public string RateText => Rate.UnitRate.ToString("N2", CultureInfo.InvariantCulture);
	public string QuantityText => Rate.Quantity.ToString("0.##", CultureInfo.InvariantCulture);
}

/// <summary>
/// Pack levels, batch rates and wholesale pricing. Pick a medicine, then one of its batches; then each section shows what
/// is saved now, takes labelled values, refuses what cannot be right with the reason, and says what it saved.
/// </summary>
public sealed class WholesalePricingPageViewModel : ObservableObject, ILoadablePage
{
	public const string ChooseFirst = "Choose a medicine and batch first.";

	private readonly IServiceScopeFactory _scopeFactory;
	private readonly CurrentSession _session;
	private readonly IUserSessionService _userSession;
	private readonly List<Drug> _allDrugs = [];
	private readonly List<Customer> _allCustomers = [];

	private Drug? _selectedDrug;
	private BatchChoice? _selectedBatch;
	private CustomerChoice? _selectedCustomer;
	private string _medicineSearch = string.Empty;
	private string _customerSearch = string.Empty;
	private bool _isLoading;
	private bool _isLoadingBatches;
	private bool _isLoadingRates;
	private string _errorMessage = string.Empty;

	private string _purchaseRateText = string.Empty, _ptrText = string.Empty, _ptsText = string.Empty, _mrpText = string.Empty;
	private string _ratesMessage = string.Empty, _ratesError = string.Empty;

	private PackLevel _newPackLevel = PackLevel.Strip;
	private string _newPackLabel = string.Empty, _newPackUnits = string.Empty;
	private string _packMessage = string.Empty, _packError = string.Empty;

	private string _priceCategory = string.Empty, _categoryPrice = string.Empty;
	private DateTime? _priceFrom = BusinessClock.Now.Date, _priceTo;
	private string _priceMessage = string.Empty, _priceError = string.Empty;

	private string _discountCategory = string.Empty, _discountPercent = string.Empty;
	private DateTime? _discountFrom = BusinessClock.Now.Date, _discountTo;
	private string _discountMessage = string.Empty, _discountError = string.Empty;

	private string _schemeCategory = string.Empty, _schemeBuy = string.Empty, _schemeFree = string.Empty;
	private DateTime? _schemeFrom = BusinessClock.Now.Date, _schemeTo;
	private string _schemeMessage = string.Empty, _schemeError = string.Empty;

	private AsyncRelayCommand? _saveBatchRatesCommand, _addPackLevelCommand, _saveCategoryPriceCommand, _saveTradeDiscountCommand, _saveSchemeCommand;

	public WholesalePricingPageViewModel(IServiceScopeFactory scopeFactory, CurrentSession session, IUserSessionService userSession)
	{
		_scopeFactory = scopeFactory;
		_session = session;
		_userSession = userSession;
	}

	public bool CanViewPurchaseMargins => _userSession.CanViewPurchaseMargins;

	public ObservableCollection<Drug> MedicineMatches { get; } = new();
	public ObservableCollection<BatchChoice> Batches { get; } = new();
	public ObservableCollection<DrugPackLevel> PackLevels { get; } = new();
	public ObservableCollection<SavedPricingRow> SavedPricing { get; } = new();
	public ObservableCollection<CustomerChoice> CustomerMatches { get; } = new();
	public ObservableCollection<RecentRateRow> RecentRates { get; } = new();
	public ObservableCollection<string> PriceCategories { get; } = new();
	public PackLevel[] PackLevelOptions { get; } = Enum.GetValues<PackLevel>();

	// ---------- Step 1: medicine and batch ----------

	/// <summary>Typed into the medicine picker: the list narrows to names containing it.</summary>
	public string MedicineSearch
	{
		get => _medicineSearch;
		set
		{
			if (SetProperty(ref _medicineSearch, value ?? string.Empty) && !string.Equals(_medicineSearch, SelectedDrug?.Name, StringComparison.Ordinal))
			{
				FilterMedicines();
			}
		}
	}

	public Drug? SelectedDrug
	{
		get => _selectedDrug;
		set
		{
			if (SetProperty(ref _selectedDrug, value))
			{
				SelectedBatch = null;
				Batches.Clear();
				PackLevels.Clear();
				SavedPricing.Clear();
				RecentRates.Clear();
				ClearSectionMessages();
				OnSelectionChanged();
				if (value != null)
				{
					MedicineLoading = LoadMedicineAsync(value.Id);
				}
			}
		}
	}

	public BatchChoice? SelectedBatch
	{
		get => _selectedBatch;
		set
		{
			if (SetProperty(ref _selectedBatch, value))
			{
				ShowSavedRates();
				OnSelectionChanged();
				RatesLoading = LoadRecentRatesAsync();
			}
		}
	}

	/// <summary>The load started by the last medicine choice, for tests to wait on.</summary>
	internal Task MedicineLoading { get; private set; } = Task.CompletedTask;

	/// <summary>The recent-rates load started by the last customer or batch choice, for tests to wait on.</summary>
	internal Task RatesLoading { get; private set; } = Task.CompletedTask;

	public bool HasMedicine => SelectedDrug != null;
	public bool HasSelection => SelectedDrug != null && SelectedBatch != null;
	public bool NeedsSelection => !HasSelection;
	public bool HasBatches => Batches.Count > 0;
	public bool HasNoBatches => HasMedicine && !IsLoadingBatches && Batches.Count == 0;
	public bool HasNoMedicines => !IsLoading && _allDrugs.Count == 0;

	public bool IsLoading
	{
		get => _isLoading;
		private set
		{
			if (SetProperty(ref _isLoading, value))
			{
				OnPropertyChanged(nameof(HasNoMedicines));
			}
		}
	}

	public bool IsLoadingBatches
	{
		get => _isLoadingBatches;
		private set
		{
			if (SetProperty(ref _isLoadingBatches, value))
			{
				OnPropertyChanged(nameof(HasNoBatches));
			}
		}
	}

	public bool IsLoadingRates
	{
		get => _isLoadingRates;
		private set => SetProperty(ref _isLoadingRates, value);
	}

	/// <summary>Page-level problems only (the lists could not be loaded).</summary>
	public string ErrorMessage
	{
		get => _errorMessage;
		private set => SetProperty(ref _errorMessage, value);
	}

	public string SelectionHint => SelectedDrug == null ? ChooseFirst
		: SelectedBatch == null ? (HasNoBatches ? $"{SelectedDrug.Name} has no batches yet. Add its stock first; its batches then appear here." : "Now choose the batch.")
		: $"{SelectedDrug.Name}, batch {SelectedBatch.Batch.BatchNo}.";

	// ---------- Batch rates ----------

	public string SavedRatesText
	{
		get
		{
			if (SelectedBatch?.Batch is not Batch batch)
			{
				return string.Empty;
			}
			string purchase = CanViewPurchaseMargins ? $"Purchase {Money(batch.PurchasePrice)}  ·  " : string.Empty;
			return $"Saved now: {purchase}PTR {Money(batch.Ptr)}  ·  PTS {Money(batch.Pts)}  ·  MRP {Money(batch.Mrp)}";
		}
	}

	public string PurchaseRateText { get => _purchaseRateText; set => SetProperty(ref _purchaseRateText, value ?? string.Empty); }
	public string PtrText { get => _ptrText; set => SetProperty(ref _ptrText, value ?? string.Empty); }
	public string PtsText { get => _ptsText; set => SetProperty(ref _ptsText, value ?? string.Empty); }
	public string MrpText { get => _mrpText; set => SetProperty(ref _mrpText, value ?? string.Empty); }
	public string RatesMessage { get => _ratesMessage; private set => SetProperty(ref _ratesMessage, value); }
	public string RatesError { get => _ratesError; private set => SetProperty(ref _ratesError, value); }

	public IAsyncRelayCommand SaveBatchRatesCommand => _saveBatchRatesCommand ??= new AsyncRelayCommand(SaveBatchRatesAsync, () => HasSelection);

	// ---------- Pack conversions ----------

	public bool HasPackLevels => PackLevels.Count > 0;
	public PackLevel NewPackLevel { get => _newPackLevel; set => SetProperty(ref _newPackLevel, value); }
	public string NewPackLabel { get => _newPackLabel; set => SetProperty(ref _newPackLabel, value ?? string.Empty); }
	public string NewPackUnits { get => _newPackUnits; set => SetProperty(ref _newPackUnits, value ?? string.Empty); }
	public string PackMessage { get => _packMessage; private set => SetProperty(ref _packMessage, value); }
	public string PackError { get => _packError; private set => SetProperty(ref _packError, value); }

	public IAsyncRelayCommand AddPackLevelCommand => _addPackLevelCommand ??= new AsyncRelayCommand(AddPackLevelAsync, () => HasSelection);

	// ---------- Customer pricing ----------

	public bool HasSavedPricing => SavedPricing.Count > 0;

	public string PriceCategory { get => _priceCategory; set => SetProperty(ref _priceCategory, value ?? string.Empty); }
	public string CategoryPrice { get => _categoryPrice; set => SetProperty(ref _categoryPrice, value ?? string.Empty); }
	public DateTime? PriceFrom { get => _priceFrom; set => SetProperty(ref _priceFrom, value); }
	public DateTime? PriceTo { get => _priceTo; set => SetProperty(ref _priceTo, value); }
	public string PriceMessage { get => _priceMessage; private set => SetProperty(ref _priceMessage, value); }
	public string PriceError { get => _priceError; private set => SetProperty(ref _priceError, value); }

	public string DiscountCategory { get => _discountCategory; set => SetProperty(ref _discountCategory, value ?? string.Empty); }
	public string DiscountPercent { get => _discountPercent; set => SetProperty(ref _discountPercent, value ?? string.Empty); }
	public DateTime? DiscountFrom { get => _discountFrom; set => SetProperty(ref _discountFrom, value); }
	public DateTime? DiscountTo { get => _discountTo; set => SetProperty(ref _discountTo, value); }
	public string DiscountMessage { get => _discountMessage; private set => SetProperty(ref _discountMessage, value); }
	public string DiscountError { get => _discountError; private set => SetProperty(ref _discountError, value); }

	public string SchemeCategory { get => _schemeCategory; set => SetProperty(ref _schemeCategory, value ?? string.Empty); }
	public string SchemeBuy { get => _schemeBuy; set => SetProperty(ref _schemeBuy, value ?? string.Empty); }
	public string SchemeFree { get => _schemeFree; set => SetProperty(ref _schemeFree, value ?? string.Empty); }
	public DateTime? SchemeFrom { get => _schemeFrom; set => SetProperty(ref _schemeFrom, value); }
	public DateTime? SchemeTo { get => _schemeTo; set => SetProperty(ref _schemeTo, value); }
	public string SchemeMessage { get => _schemeMessage; private set => SetProperty(ref _schemeMessage, value); }
	public string SchemeError { get => _schemeError; private set => SetProperty(ref _schemeError, value); }

	public IAsyncRelayCommand SaveCategoryPriceCommand => _saveCategoryPriceCommand ??= new AsyncRelayCommand(SaveCategoryPriceAsync, () => HasSelection);
	public IAsyncRelayCommand SaveTradeDiscountCommand => _saveTradeDiscountCommand ??= new AsyncRelayCommand(SaveTradeDiscountAsync, () => HasSelection);
	public IAsyncRelayCommand SaveSchemeCommand => _saveSchemeCommand ??= new AsyncRelayCommand(SaveSchemeAsync, () => HasSelection);

	// ---------- Recent rates ----------

	public string CustomerSearch
	{
		get => _customerSearch;
		set
		{
			if (SetProperty(ref _customerSearch, value ?? string.Empty) && !string.Equals(_customerSearch, SelectedCustomer?.Label, StringComparison.Ordinal))
			{
				FilterCustomers();
			}
		}
	}

	public CustomerChoice? SelectedCustomer
	{
		get => _selectedCustomer;
		set
		{
			if (SetProperty(ref _selectedCustomer, value))
			{
				RatesLoading = LoadRecentRatesAsync();
				OnPropertyChanged(nameof(RecentRatesEmptyText));
			}
		}
	}

	public bool HasRecentRates => RecentRates.Count > 0;

	public string RecentRatesEmptyText => SelectedCustomer == null
		? "Choose a customer to see the last five rates they were billed for this medicine."
		: IsLoadingRates ? "Loading…"
		: $"{SelectedCustomer.Customer.Name} has not been billed for {SelectedDrug?.Name ?? "this medicine"} yet.";

	// ---------- Loading ----------

	public async Task LoadAsync(CancellationToken cancellationToken = default)
	{
		IsLoading = true;
		try
		{
			using IServiceScope scope = _scopeFactory.CreateScope();
			PharmaBillDbContext context = scope.ServiceProvider.GetRequiredService<PharmaBillDbContext>();
			_allDrugs.Clear();
			_allDrugs.AddRange(await context.Drugs.AsNoTracking().Where(drug => drug.IsActive).OrderBy(drug => drug.Name).ToListAsync(cancellationToken));
			_allCustomers.Clear();
			_allCustomers.AddRange(await context.Customers.AsNoTracking().Where(customer => customer.IsActive).OrderBy(customer => customer.Name).ToListAsync(cancellationToken));
			PriceCategories.Clear();
			foreach (string category in _allCustomers.Select(customer => customer.PriceCategory?.Trim()).Where(category => !string.IsNullOrEmpty(category)).Distinct(StringComparer.OrdinalIgnoreCase).Order()!)
			{
				PriceCategories.Add(category!);
			}
			FilterMedicines();
			FilterCustomers();
			ErrorMessage = string.Empty;
			if (SelectedDrug != null)
			{
				await LoadMedicineAsync(SelectedDrug.Id);
			}
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			ErrorMessage = "The medicines could not be loaded: " + PharmaBill.Core.UserMessage.Of(ex);
		}
		finally
		{
			IsLoading = false;
		}
	}

	private void FilterMedicines()
	{
		string text = MedicineSearch.Trim();
		MedicineMatches.Clear();
		foreach (Drug drug in _allDrugs.Where(drug => text.Length == 0 || drug.Name.Contains(text, StringComparison.OrdinalIgnoreCase)).Take(60))
		{
			MedicineMatches.Add(drug);
		}
		if (SelectedDrug != null && !MedicineMatches.Contains(SelectedDrug))
		{
			MedicineMatches.Insert(0, SelectedDrug);
		}
	}

	private void FilterCustomers()
	{
		string text = CustomerSearch.Trim();
		string digits = new(text.Where(char.IsDigit).ToArray());
		CustomerMatches.Clear();
		foreach (Customer customer in _allCustomers.Where(customer => text.Length == 0
				|| customer.Name.Contains(text, StringComparison.OrdinalIgnoreCase)
				|| (digits.Length >= 3 && (customer.Phone ?? string.Empty).Contains(digits, StringComparison.Ordinal))).Take(60))
		{
			CustomerMatches.Add(new CustomerChoice(customer));
		}
		if (SelectedCustomer != null && CustomerMatches.All(choice => choice.Customer.Id != SelectedCustomer.Customer.Id))
		{
			CustomerMatches.Insert(0, SelectedCustomer);
		}
	}

	private async Task LoadMedicineAsync(Guid drugId)
	{
		IsLoadingBatches = true;
		try
		{
			using IServiceScope scope = _scopeFactory.CreateScope();
			var pricing = new WholesalePricingService(new UnitOfWork(scope.ServiceProvider.GetRequiredService<PharmaBillDbContext>()));
			IReadOnlyList<BatchOnHand> batches = await pricing.GetBatchesAsync(drugId);
			List<DrugPackLevel> levels = await scope.ServiceProvider.GetRequiredService<PharmaBillDbContext>().DrugPackLevels.AsNoTracking()
				.Where(level => level.DrugId == drugId).OrderBy(level => level.Level).ToListAsync();
			DrugPricing saved = await pricing.GetPricingAsync(drugId);
			if (SelectedDrug?.Id != drugId)
			{
				return;
			}
			Guid? keep = SelectedBatch?.Batch.Id;
			Batches.Clear();
			foreach (BatchOnHand batch in batches)
			{
				Batches.Add(new BatchChoice(batch.Batch, batch.OnHand));
			}
			PackLevels.Clear();
			foreach (DrugPackLevel level in levels)
			{
				PackLevels.Add(level);
			}
			ShowSavedPricing(saved);
			if (keep is Guid id)
			{
				_selectedBatch = Batches.FirstOrDefault(choice => choice.Batch.Id == id);
				OnPropertyChanged(nameof(SelectedBatch));
				ShowSavedRates();
			}
		}
		catch (Exception ex)
		{
			ErrorMessage = "The batches could not be loaded: " + PharmaBill.Core.UserMessage.Of(ex);
		}
		finally
		{
			IsLoadingBatches = false;
			OnPropertyChanged(nameof(HasBatches));
			OnPropertyChanged(nameof(HasPackLevels));
			OnSelectionChanged();
		}
	}

	private void ShowSavedRates()
	{
		Batch? batch = SelectedBatch?.Batch;
		PurchaseRateText = batch == null ? string.Empty : Amount(batch.PurchasePrice);
		PtrText = batch?.Ptr is decimal ptr ? Amount(ptr) : string.Empty;
		PtsText = batch?.Pts is decimal pts ? Amount(pts) : string.Empty;
		MrpText = batch?.Mrp is decimal mrp ? Amount(mrp) : string.Empty;
		OnPropertyChanged(nameof(SavedRatesText));
	}

	private void ShowSavedPricing(DrugPricing saved)
	{
		SavedPricing.Clear();
		foreach (CustomerCategoryPrice price in saved.Prices)
		{
			SavedPricing.Add(new SavedPricingRow("Category price", price.PriceCategory, Money(price.UnitPrice) + " per pack", Period(price.EffectiveFrom, price.EffectiveTo)));
		}
		foreach (TradeDiscount discount in saved.Discounts)
		{
			SavedPricing.Add(new SavedPricingRow(discount.DrugId == null ? "Trade discount (all medicines)" : "Trade discount", discount.PriceCategory, discount.DiscountPercent.ToString("0.##", CultureInfo.InvariantCulture) + "%", Period(discount.EffectiveFrom, discount.EffectiveTo)));
		}
		foreach (WholesaleScheme scheme in saved.Schemes)
		{
			SavedPricing.Add(new SavedPricingRow(scheme.DrugId == null ? "Scheme (all medicines)" : "Scheme", string.IsNullOrWhiteSpace(scheme.PriceCategory) ? "All customers" : scheme.PriceCategory,
				$"Buy {scheme.BuyQuantity:0.##}, get {scheme.FreeQuantity:0.##} free", Period(scheme.StartsOn, scheme.EndsOn)));
		}
		OnPropertyChanged(nameof(HasSavedPricing));
	}

	private async Task LoadRecentRatesAsync()
	{
		RecentRates.Clear();
		OnPropertyChanged(nameof(HasRecentRates));
		if (SelectedCustomer == null || SelectedDrug == null)
		{
			OnPropertyChanged(nameof(RecentRatesEmptyText));
			return;
		}
		IsLoadingRates = true;
		OnPropertyChanged(nameof(RecentRatesEmptyText));
		try
		{
			using IServiceScope scope = _scopeFactory.CreateScope();
			IReadOnlyList<WholesaleRateHistoryRow> rates = await new WholesalePricingService(new UnitOfWork(scope.ServiceProvider.GetRequiredService<PharmaBillDbContext>()))
				.GetLastRatesAsync(SelectedCustomer.Customer.Id, SelectedDrug.Id);
			foreach (WholesaleRateHistoryRow rate in rates)
			{
				RecentRates.Add(new RecentRateRow(rate));
			}
		}
		catch (Exception ex)
		{
			ErrorMessage = "Recent rates could not be loaded: " + PharmaBill.Core.UserMessage.Of(ex);
		}
		finally
		{
			IsLoadingRates = false;
			OnPropertyChanged(nameof(HasRecentRates));
			OnPropertyChanged(nameof(RecentRatesEmptyText));
		}
	}

	private void OnSelectionChanged()
	{
		foreach (string name in new[] { nameof(HasMedicine), nameof(HasSelection), nameof(NeedsSelection), nameof(HasNoBatches), nameof(SelectionHint), nameof(SavedRatesText), nameof(RecentRatesEmptyText) })
		{
			OnPropertyChanged(name);
		}
		foreach (AsyncRelayCommand? command in new[] { _saveBatchRatesCommand, _addPackLevelCommand, _saveCategoryPriceCommand, _saveTradeDiscountCommand, _saveSchemeCommand })
		{
			command?.NotifyCanExecuteChanged();
		}
	}

	private void ClearSectionMessages()
	{
		RatesMessage = RatesError = PackMessage = PackError = PriceMessage = PriceError = DiscountMessage = DiscountError = SchemeMessage = SchemeError = string.Empty;
	}

	// ---------- Saving ----------

	private async Task SaveBatchRatesAsync()
	{
		RatesMessage = RatesError = string.Empty;
		if (SelectedBatch is not BatchChoice choice || SelectedDrug is not Drug drug)
		{
			RatesError = ChooseFirst;
			return;
		}
		// Without the purchase-margin permission the purchase rate is not shown, and it is kept as saved.
		decimal purchase = choice.Batch.PurchasePrice;
		string? problem;
		if (CanViewPurchaseMargins)
		{
			if (!TryAmount(PurchaseRateText, "Purchase rate", required: true, out decimal? typedPurchase, out problem))
			{
				RatesError = problem!;
				return;
			}
			purchase = typedPurchase!.Value;
		}
		if (!TryAmount(PtrText, "PTR (price to retailer)", required: false, out decimal? ptr, out problem)
			|| !TryAmount(PtsText, "PTS (price to stockist)", required: false, out decimal? pts, out problem)
			|| !TryAmount(MrpText, "MRP", required: true, out decimal? mrp, out problem))
		{
			RatesError = problem!;
			return;
		}
		var rates = new WholesaleBatchRate(purchase, ptr, pts, mrp!.Value);
		if (WholesalePricingService.BatchRateProblem(rates) is string refused)
		{
			RatesError = refused;
			return;
		}
		try
		{
			using IServiceScope scope = _scopeFactory.CreateScope();
			await scope.ServiceProvider.GetRequiredService<WholesalePricingService>().SaveBatchRatesAsync(choice.Batch.Id, rates,
				(_session.User ?? throw new UnauthorizedAccessException("Sign in before changing rates.")).Id);
			await LoadMedicineAsync(drug.Id);
			RatesMessage = $"Saved for batch {choice.Batch.BatchNo}: PTR {Money(ptr)}, PTS {Money(pts)}, MRP {Money(mrp)}.";
		}
		catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException)
		{
			RatesError = PharmaBill.Core.UserMessage.Of(ex);
		}
	}

	private async Task AddPackLevelAsync()
	{
		PackMessage = PackError = string.Empty;
		if (SelectedDrug is not Drug drug)
		{
			PackError = ChooseFirst;
			return;
		}
		if (!int.TryParse(NewPackUnits.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int units) || units <= 0)
		{
			PackError = "Units per pack must be a whole number above zero, for example 10.";
			return;
		}
		if (NewPackLevel == PackLevel.Unit && units != 1)
		{
			PackError = "The Unit level is the base unit itself, so it is always 1.";
			return;
		}
		string label = string.IsNullOrWhiteSpace(NewPackLabel) ? NewPackLevel.ToString() : NewPackLabel.Trim();
		List<DrugPackLevel> levels = PackLevels.Where(level => level.Level != NewPackLevel)
			.Select(level => new DrugPackLevel { Level = level.Level, Label = level.Label, UnitsPerPack = level.UnitsPerPack })
			.Append(new DrugPackLevel { Level = NewPackLevel, Label = label, UnitsPerPack = units })
			.ToList();
		if (levels.All(level => level.Level != PackLevel.Unit))
		{
			levels.Add(new DrugPackLevel { Level = PackLevel.Unit, Label = "Unit", UnitsPerPack = 1m });
		}
		try
		{
			using IServiceScope scope = _scopeFactory.CreateScope();
			await scope.ServiceProvider.GetRequiredService<WholesalePricingService>().SavePackLevelsAsync(drug.Id, levels);
			await LoadMedicineAsync(drug.Id);
			PackMessage = $"Saved: 1 {label} = {units} base units.";
			NewPackLabel = NewPackUnits = string.Empty;
		}
		catch (InvalidOperationException ex)
		{
			PackError = ex.Message;
		}
	}

	private async Task SaveCategoryPriceAsync()
	{
		PriceMessage = PriceError = string.Empty;
		if (SelectedDrug is not Drug drug)
		{
			PriceError = ChooseFirst;
			return;
		}
		if (string.IsNullOrWhiteSpace(PriceCategory))
		{
			PriceError = "Choose or type the customer category, for example Hospital.";
			return;
		}
		if (!TryAmount(CategoryPrice, "Category price", required: true, out decimal? price, out string? problem))
		{
			PriceError = problem!;
			return;
		}
		if (PeriodProblem(PriceFrom, PriceTo) is string period)
		{
			PriceError = period;
			return;
		}
		try
		{
			using IServiceScope scope = _scopeFactory.CreateScope();
			await scope.ServiceProvider.GetRequiredService<WholesalePricingService>().SaveCategoryPriceAsync(new CustomerCategoryPrice
			{
				DrugId = drug.Id,
				PriceCategory = PriceCategory.Trim(),
				UnitPrice = price!.Value,
				EffectiveFrom = Date(PriceFrom),
				EffectiveTo = Date(PriceTo)
			});
			await LoadMedicineAsync(drug.Id);
			PriceMessage = $"Saved: {PriceCategory.Trim()} pays {Money(price)} per pack for {drug.Name}, {Period(Date(PriceFrom), Date(PriceTo))}.";
		}
		catch (InvalidOperationException ex)
		{
			PriceError = ex.Message;
		}
	}

	private async Task SaveTradeDiscountAsync()
	{
		DiscountMessage = DiscountError = string.Empty;
		if (SelectedDrug is not Drug drug)
		{
			DiscountError = ChooseFirst;
			return;
		}
		if (string.IsNullOrWhiteSpace(DiscountCategory))
		{
			DiscountError = "Choose or type the customer category the discount is for.";
			return;
		}
		if (!TryAmount(DiscountPercent, "Discount", required: true, out decimal? percent, out string? problem))
		{
			DiscountError = problem!;
			return;
		}
		if (percent > 100m)
		{
			DiscountError = "A discount cannot be more than 100%.";
			return;
		}
		if (PeriodProblem(DiscountFrom, DiscountTo) is string period)
		{
			DiscountError = period;
			return;
		}
		try
		{
			using IServiceScope scope = _scopeFactory.CreateScope();
			await scope.ServiceProvider.GetRequiredService<WholesalePricingService>().SaveTradeDiscountAsync(new TradeDiscount
			{
				DrugId = drug.Id,
				PriceCategory = DiscountCategory.Trim(),
				DiscountPercent = percent!.Value,
				EffectiveFrom = Date(DiscountFrom),
				EffectiveTo = Date(DiscountTo)
			});
			await LoadMedicineAsync(drug.Id);
			DiscountMessage = $"Saved: {percent:0.##}% trade discount for {DiscountCategory.Trim()} on {drug.Name}, {Period(Date(DiscountFrom), Date(DiscountTo))}.";
		}
		catch (InvalidOperationException ex)
		{
			DiscountError = ex.Message;
		}
	}

	private async Task SaveSchemeAsync()
	{
		SchemeMessage = SchemeError = string.Empty;
		if (SelectedDrug is not Drug drug)
		{
			SchemeError = ChooseFirst;
			return;
		}
		if (!int.TryParse(SchemeBuy.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int buy) || buy <= 0
			|| !int.TryParse(SchemeFree.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int free) || free <= 0)
		{
			SchemeError = "Enter whole numbers above zero for Buy and Free, for example buy 10, get 1 free.";
			return;
		}
		if (SchemeFrom == null || SchemeTo == null)
		{
			SchemeError = "A scheme needs a start and an end date.";
			return;
		}
		if (PeriodProblem(SchemeFrom, SchemeTo) is string period)
		{
			SchemeError = period;
			return;
		}
		try
		{
			using IServiceScope scope = _scopeFactory.CreateScope();
			await scope.ServiceProvider.GetRequiredService<WholesalePricingService>().SaveSchemeAsync(new WholesaleScheme
			{
				DrugId = drug.Id,
				PriceCategory = string.IsNullOrWhiteSpace(SchemeCategory) ? null : SchemeCategory.Trim(),
				BuyQuantity = buy,
				FreeQuantity = free,
				StartsOn = DateOnly.FromDateTime(SchemeFrom.Value),
				EndsOn = DateOnly.FromDateTime(SchemeTo.Value)
			});
			await LoadMedicineAsync(drug.Id);
			SchemeMessage = $"Saved: buy {buy}, get {free} free on {drug.Name} for {(string.IsNullOrWhiteSpace(SchemeCategory) ? "all customers" : SchemeCategory.Trim())}, {Period(Date(SchemeFrom), Date(SchemeTo))}.";
		}
		catch (InvalidOperationException ex)
		{
			SchemeError = ex.Message;
		}
	}

	// ---------- Helpers ----------

	/// <summary>A typed amount: blank is allowed only when not required; never negative; the problem names the field.</summary>
	internal static bool TryAmount(string text, string label, bool required, out decimal? amount, out string? problem)
	{
		amount = null;
		problem = null;
		string trimmed = (text ?? string.Empty).Trim().TrimStart('₹').Trim();
		if (trimmed.Length == 0)
		{
			if (required)
			{
				problem = $"Enter the {label}.";
				return false;
			}
			return true;
		}
		if (!decimal.TryParse(trimmed, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal value))
		{
			problem = $"{label} must be a number, for example 42.50.";
			return false;
		}
		if (value < 0m)
		{
			problem = $"{label} cannot be negative.";
			return false;
		}
		amount = value;
		return true;
	}

	private static string? PeriodProblem(DateTime? from, DateTime? to) =>
		from != null && to != null && to < from ? "The end date is before the start date." : null;

	private static DateOnly? Date(DateTime? value) => value is DateTime date ? DateOnly.FromDateTime(date) : null;

	private static string Period(DateOnly? from, DateOnly? to) => (from, to) switch
	{
		(null, null) => "no end date",
		(DateOnly f, null) => "from " + f.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture),
		(null, DateOnly t) => "until " + t.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture),
		(DateOnly f, DateOnly t) => f.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture) + " to " + t.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture),
	};

	private static string Money(decimal? value) => value is decimal amount ? MoneyFormat.Rupees(amount) : "not set";

	private static string Amount(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);
}
