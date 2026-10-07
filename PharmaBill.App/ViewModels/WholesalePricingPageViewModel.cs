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

public class WholesalePricingPageViewModel : ObservableObject, ILoadablePage
{
	private readonly IServiceScopeFactory _scopeFactory;

	private readonly CurrentSession _session;

	private readonly IUserSessionService _userSession;

	private Drug? _selectedDrug;

	private Batch? _selectedBatch;

	private string _priceCategory = string.Empty;

	private string _categoryPrice = string.Empty;

	private string _tradeDiscountPercent = string.Empty;

	private string _buyQuantity = string.Empty;

	private string _freeQuantity = string.Empty;

	private DateTime? _effectiveFrom = DateTime.Today;

	private DateTime? _effectiveTo;

	private decimal _purchaseRate;

	private decimal? _ptr;

	private decimal? _pts;

	private decimal _mrp;

	private string _message = string.Empty;

	private string _errorMessage = string.Empty;

	private string _newPackLevelName = "Unit";

	private string _newPackLabel = string.Empty;

	private string _newPackUnits = "1";

	private Guid _selectedCustomerId;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? addPackLevelCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? saveBatchRatesCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? saveCategoryPriceCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? saveTradeDiscountCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? saveSchemeCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? loadRateHistoryCommand;

	public bool CanViewPurchaseMargins => _userSession.CanViewPurchaseMargins;

	public ObservableCollection<Drug> Drugs { get; } = new ObservableCollection<Drug>();

	public ObservableCollection<Batch> Batches { get; } = new ObservableCollection<Batch>();

	public ObservableCollection<DrugPackLevel> PackLevels { get; } = new ObservableCollection<DrugPackLevel>();

	public ObservableCollection<WholesaleRateHistoryRow> RecentRates { get; } = new ObservableCollection<WholesaleRateHistoryRow>();

	public PackLevel[] PackLevelOptions { get; } = Enum.GetValues<PackLevel>();

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public Drug? SelectedDrug
	{
		get
		{
			return _selectedDrug;
		}
		set
		{
			if (!EqualityComparer<Drug>.Default.Equals(_selectedDrug, value))
			{
				OnPropertyChanging(nameof(SelectedDrug));
				_selectedDrug = value;
				OnSelectedDrugChanged(value);
				OnPropertyChanged(nameof(SelectedDrug));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public Batch? SelectedBatch
	{
		get
		{
			return _selectedBatch;
		}
		set
		{
			if (!EqualityComparer<Batch>.Default.Equals(_selectedBatch, value))
			{
				OnPropertyChanging(nameof(SelectedBatch));
				_selectedBatch = value;
				OnSelectedBatchChanged(value);
				OnPropertyChanged(nameof(SelectedBatch));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string PriceCategory
	{
		get
		{
			return _priceCategory;
		}
		[MemberNotNull("_priceCategory")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_priceCategory, value))
			{
				OnPropertyChanging(nameof(PriceCategory));
				_priceCategory = value;
				OnPropertyChanged(nameof(PriceCategory));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string CategoryPrice
	{
		get
		{
			return _categoryPrice;
		}
		[MemberNotNull("_categoryPrice")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_categoryPrice, value))
			{
				OnPropertyChanging(nameof(CategoryPrice));
				_categoryPrice = value;
				OnPropertyChanged(nameof(CategoryPrice));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string TradeDiscountPercent
	{
		get
		{
			return _tradeDiscountPercent;
		}
		[MemberNotNull("_tradeDiscountPercent")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_tradeDiscountPercent, value))
			{
				OnPropertyChanging(nameof(TradeDiscountPercent));
				_tradeDiscountPercent = value;
				OnPropertyChanged(nameof(TradeDiscountPercent));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string BuyQuantity
	{
		get
		{
			return _buyQuantity;
		}
		[MemberNotNull("_buyQuantity")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_buyQuantity, value))
			{
				OnPropertyChanging(nameof(BuyQuantity));
				_buyQuantity = value;
				OnPropertyChanged(nameof(BuyQuantity));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string FreeQuantity
	{
		get
		{
			return _freeQuantity;
		}
		[MemberNotNull("_freeQuantity")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_freeQuantity, value))
			{
				OnPropertyChanging(nameof(FreeQuantity));
				_freeQuantity = value;
				OnPropertyChanged(nameof(FreeQuantity));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public DateTime? EffectiveFrom
	{
		get
		{
			return _effectiveFrom;
		}
		set
		{
			if (!EqualityComparer<DateTime?>.Default.Equals(_effectiveFrom, value))
			{
				OnPropertyChanging(nameof(EffectiveFrom));
				_effectiveFrom = value;
				OnPropertyChanged(nameof(EffectiveFrom));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public DateTime? EffectiveTo
	{
		get
		{
			return _effectiveTo;
		}
		set
		{
			if (!EqualityComparer<DateTime?>.Default.Equals(_effectiveTo, value))
			{
				OnPropertyChanging(nameof(EffectiveTo));
				_effectiveTo = value;
				OnPropertyChanged(nameof(EffectiveTo));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public decimal PurchaseRate
	{
		get
		{
			return _purchaseRate;
		}
		set
		{
			if (!EqualityComparer<decimal>.Default.Equals(_purchaseRate, value))
			{
				OnPropertyChanging(nameof(PurchaseRate));
				_purchaseRate = value;
				OnPropertyChanged(nameof(PurchaseRate));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public decimal? Ptr
	{
		get
		{
			return _ptr;
		}
		set
		{
			if (!EqualityComparer<decimal?>.Default.Equals(_ptr, value))
			{
				OnPropertyChanging(nameof(Ptr));
				_ptr = value;
				OnPropertyChanged(nameof(Ptr));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public decimal? Pts
	{
		get
		{
			return _pts;
		}
		set
		{
			if (!EqualityComparer<decimal?>.Default.Equals(_pts, value))
			{
				OnPropertyChanging(nameof(Pts));
				_pts = value;
				OnPropertyChanged(nameof(Pts));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public decimal Mrp
	{
		get
		{
			return _mrp;
		}
		set
		{
			if (!EqualityComparer<decimal>.Default.Equals(_mrp, value))
			{
				OnPropertyChanging(nameof(Mrp));
				_mrp = value;
				OnPropertyChanged(nameof(Mrp));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string Message
	{
		get
		{
			return _message;
		}
		[MemberNotNull("_message")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_message, value))
			{
				OnPropertyChanging(nameof(Message));
				_message = value;
				OnPropertyChanged(nameof(Message));
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
	public string NewPackLevelName
	{
		get
		{
			return _newPackLevelName;
		}
		[MemberNotNull("_newPackLevelName")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_newPackLevelName, value))
			{
				OnPropertyChanging(nameof(NewPackLevelName));
				_newPackLevelName = value;
				OnPropertyChanged(nameof(NewPackLevelName));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string NewPackLabel
	{
		get
		{
			return _newPackLabel;
		}
		[MemberNotNull("_newPackLabel")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_newPackLabel, value))
			{
				OnPropertyChanging(nameof(NewPackLabel));
				_newPackLabel = value;
				OnPropertyChanged(nameof(NewPackLabel));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string NewPackUnits
	{
		get
		{
			return _newPackUnits;
		}
		[MemberNotNull("_newPackUnits")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_newPackUnits, value))
			{
				OnPropertyChanging(nameof(NewPackUnits));
				_newPackUnits = value;
				OnPropertyChanged(nameof(NewPackUnits));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public Guid SelectedCustomerId
	{
		get
		{
			return _selectedCustomerId;
		}
		set
		{
			if (!EqualityComparer<Guid>.Default.Equals(_selectedCustomerId, value))
			{
				OnPropertyChanging(nameof(SelectedCustomerId));
				_selectedCustomerId = value;
				OnPropertyChanged(nameof(SelectedCustomerId));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand AddPackLevelCommand => addPackLevelCommand ?? (addPackLevelCommand = new AsyncRelayCommand(AddPackLevelAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand SaveBatchRatesCommand => saveBatchRatesCommand ?? (saveBatchRatesCommand = new AsyncRelayCommand(SaveBatchRatesAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand SaveCategoryPriceCommand => saveCategoryPriceCommand ?? (saveCategoryPriceCommand = new AsyncRelayCommand(SaveCategoryPriceAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand SaveTradeDiscountCommand => saveTradeDiscountCommand ?? (saveTradeDiscountCommand = new AsyncRelayCommand(SaveTradeDiscountAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand SaveSchemeCommand => saveSchemeCommand ?? (saveSchemeCommand = new AsyncRelayCommand(SaveSchemeAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand LoadRateHistoryCommand => loadRateHistoryCommand ?? (loadRateHistoryCommand = new AsyncRelayCommand(LoadRateHistoryAsync));

	public WholesalePricingPageViewModel(IServiceScopeFactory scopeFactory, CurrentSession session, IUserSessionService userSession)
	{
		_scopeFactory = scopeFactory;
		_session = session;
		_userSession = userSession;
	}

	public async Task LoadAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		using IServiceScope scope = _scopeFactory.CreateScope();
		PharmaBillDbContext requiredService = scope.ServiceProvider.GetRequiredService<PharmaBillDbContext>();
		Drugs.Clear();
		foreach (Drug item in await (from item in requiredService.Drugs.AsNoTracking()
			where item.IsActive
			orderby item.Name
			select item).ToListAsync(cancellationToken))
		{
			Drugs.Add(item);
		}
		if (SelectedDrug != null)
		{
			await LoadDrugAsync(SelectedDrug.Id, cancellationToken);
		}
	}

	private async Task AddPackLevelAsync()
	{
		if (SelectedDrug == null || !Enum.TryParse<PackLevel>(NewPackLevelName, out var level) || !decimal.TryParse(NewPackUnits, out var result) || result <= 0m)
		{
			ErrorMessage = "Select a medicine, pack level and positive units-per-pack.";
			return;
		}
		DrugPackLevel[] packLevels = (from item in PackLevels
			where item.Level != level
			select new DrugPackLevel
			{
				Level = item.Level,
				Label = item.Label,
				UnitsPerPack = item.UnitsPerPack
			}).Append(new DrugPackLevel
		{
			Level = level,
			Label = (string.IsNullOrWhiteSpace(NewPackLabel) ? level.ToString() : NewPackLabel.Trim()),
			UnitsPerPack = result
		}).ToArray();
		try
		{
			using IServiceScope scope = _scopeFactory.CreateScope();
			await scope.ServiceProvider.GetRequiredService<WholesalePricingService>().SavePackLevelsAsync(SelectedDrug.Id, packLevels);
			await LoadDrugAsync(SelectedDrug.Id, CancellationToken.None);
			Message = $"{level} pack conversion saved.";
			ErrorMessage = string.Empty;
		}
		catch (InvalidOperationException ex)
		{
			ErrorMessage = ex.Message;
		}
	}

	private async Task SaveBatchRatesAsync()
	{
		if (SelectedBatch == null)
		{
			ErrorMessage = "Select a batch before updating rates.";
			return;
		}
		try
		{
			using IServiceScope scope = _scopeFactory.CreateScope();
			await scope.ServiceProvider.GetRequiredService<WholesalePricingService>().SaveBatchRatesAsync(SelectedBatch.Id, new WholesaleBatchRate(PurchaseRate, Ptr, Pts, Mrp), (_session.User ?? throw new UnauthorizedAccessException("Sign in before editing rates.")).Id);
			Message = "Batch purchase rate, PTR, PTS and MRP saved.";
			ErrorMessage = string.Empty;
			await LoadDrugAsync(SelectedDrug.Id, CancellationToken.None);
		}
		catch (Exception ex) when ((ex is InvalidOperationException || ex is UnauthorizedAccessException) ? true : false)
		{
			ErrorMessage = ex.Message;
		}
	}

	private async Task SaveCategoryPriceAsync()
	{
		if (SelectedDrug == null || string.IsNullOrWhiteSpace(PriceCategory) || !decimal.TryParse(CategoryPrice, out var result))
		{
			ErrorMessage = "Select a medicine, category and valid category price.";
			return;
		}
		try
		{
			using IServiceScope scope = _scopeFactory.CreateScope();
			await scope.ServiceProvider.GetRequiredService<WholesalePricingService>().SaveCategoryPriceAsync(new CustomerCategoryPrice
			{
				DrugId = SelectedDrug.Id,
				PriceCategory = PriceCategory.Trim(),
				UnitPrice = result,
				EffectiveFrom = (EffectiveFrom.HasValue ? new DateOnly?(DateOnly.FromDateTime(EffectiveFrom.Value)) : ((DateOnly?)null)),
				EffectiveTo = (EffectiveTo.HasValue ? new DateOnly?(DateOnly.FromDateTime(EffectiveTo.Value)) : ((DateOnly?)null))
			});
			Message = "Customer category price saved.";
			ErrorMessage = string.Empty;
		}
		catch (InvalidOperationException ex)
		{
			ErrorMessage = ex.Message;
		}
	}

	private async Task SaveTradeDiscountAsync()
	{
		if (string.IsNullOrWhiteSpace(PriceCategory) || !decimal.TryParse(TradeDiscountPercent, out var result))
		{
			ErrorMessage = "Enter a price category and a valid discount percentage.";
			return;
		}
		try
		{
			using IServiceScope scope = _scopeFactory.CreateScope();
			await scope.ServiceProvider.GetRequiredService<WholesalePricingService>().SaveTradeDiscountAsync(new TradeDiscount
			{
				DrugId = SelectedDrug?.Id,
				PriceCategory = PriceCategory.Trim(),
				DiscountPercent = result,
				EffectiveFrom = (EffectiveFrom.HasValue ? new DateOnly?(DateOnly.FromDateTime(EffectiveFrom.Value)) : ((DateOnly?)null)),
				EffectiveTo = (EffectiveTo.HasValue ? new DateOnly?(DateOnly.FromDateTime(EffectiveTo.Value)) : ((DateOnly?)null))
			});
			Message = "Trade discount saved.";
			ErrorMessage = string.Empty;
		}
		catch (InvalidOperationException ex)
		{
			ErrorMessage = ex.Message;
		}
	}

	private async Task SaveSchemeAsync()
	{
		if (!decimal.TryParse(BuyQuantity, out var buy) || !decimal.TryParse(FreeQuantity, out var free) || !EffectiveFrom.HasValue || !EffectiveTo.HasValue)
		{
			ErrorMessage = "Enter scheme buy/free quantities and start/end dates.";
			return;
		}
		try
		{
			using IServiceScope scope = _scopeFactory.CreateScope();
			await scope.ServiceProvider.GetRequiredService<WholesalePricingService>().SaveSchemeAsync(new WholesaleScheme
			{
				DrugId = SelectedDrug?.Id,
				PriceCategory = (string.IsNullOrWhiteSpace(PriceCategory) ? null : PriceCategory.Trim()),
				BuyQuantity = buy,
				FreeQuantity = free,
				StartsOn = DateOnly.FromDateTime(EffectiveFrom.Value),
				EndsOn = DateOnly.FromDateTime(EffectiveTo.Value)
			});
			Message = $"Scheme saved: {buy:0.##}+{free:0.##}.";
			ErrorMessage = string.Empty;
		}
		catch (InvalidOperationException ex)
		{
			ErrorMessage = ex.Message;
		}
	}

	private async Task LoadRateHistoryAsync()
	{
		if (SelectedDrug == null || SelectedCustomerId == Guid.Empty)
		{
			ErrorMessage = "Enter/select a customer ID and medicine to view recent rates.";
			return;
		}
		using IServiceScope scope = _scopeFactory.CreateScope();
		IReadOnlyList<WholesaleRateHistoryRow> readOnlyList = await scope.ServiceProvider.GetRequiredService<WholesalePricingService>().GetLastRatesAsync(SelectedCustomerId, SelectedDrug.Id);
		RecentRates.Clear();
		foreach (WholesaleRateHistoryRow item in readOnlyList)
		{
			RecentRates.Add(item);
		}
	}

	private async Task LoadDrugAsync(Guid drugId, CancellationToken cancellationToken)
	{
		using IServiceScope scope = _scopeFactory.CreateScope();
		PharmaBillDbContext context = scope.ServiceProvider.GetRequiredService<PharmaBillDbContext>();
		List<Batch> list = await (from item in context.Batches.AsNoTracking()
			where item.DrugId == drugId
			orderby item.ExpiryDate, item.BatchNo
			select item).ToListAsync(cancellationToken);
		Batches.Clear();
		foreach (Batch item in list)
		{
			Batches.Add(item);
		}
		List<DrugPackLevel> list2 = await (from item in context.DrugPackLevels.AsNoTracking()
			where item.DrugId == drugId
			orderby item.Level
			select item).ToListAsync(cancellationToken);
		PackLevels.Clear();
		foreach (DrugPackLevel item2 in list2)
		{
			PackLevels.Add(item2);
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnSelectedDrugChanged(Drug? value)
	{
		if (value != null)
		{
			LoadDrugAsync(value.Id, CancellationToken.None);
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnSelectedBatchChanged(Batch? value)
	{
		if (value != null)
		{
			PurchaseRate = value.PurchasePrice;
			Ptr = value.Ptr;
			Pts = value.Pts;
			Mrp = value.Mrp.GetValueOrDefault();
		}
	}
}
