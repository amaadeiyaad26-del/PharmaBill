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

public class StockInHandPageViewModel(IServiceScopeFactory scopeFactory, IUserSessionService userSession) : ObservableObject, ILoadablePage
{
	private DateTime _asOfDate = DateTime.Today;

	private DateTime? _fromDate;

	private DateTime? _deadStockBefore;

	private Supplier? _selectedSupplier;

	private string _scheduleFilter = string.Empty;

	private string _manufacturerFilter = string.Empty;

	private string _errorMessage = string.Empty;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? refreshCommand;

	public bool CanViewPurchaseMargins => userSession.CanViewPurchaseMargins;

	public ObservableCollection<StockInHandRow> Rows { get; } = new ObservableCollection<StockInHandRow>();

	public ObservableCollection<ReorderSuggestion> ReorderSuggestions { get; } = new ObservableCollection<ReorderSuggestion>();

	public ObservableCollection<DeadStockAlert> DeadStock { get; } = new ObservableCollection<DeadStockAlert>();

	public ObservableCollection<Supplier> Suppliers { get; } = new ObservableCollection<Supplier>();

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public DateTime AsOfDate
	{
		get
		{
			return _asOfDate;
		}
		set
		{
			if (!EqualityComparer<DateTime>.Default.Equals(_asOfDate, value))
			{
				OnPropertyChanging(nameof(AsOfDate));
				_asOfDate = value;
				OnPropertyChanged(nameof(AsOfDate));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public DateTime? FromDate
	{
		get
		{
			return _fromDate;
		}
		set
		{
			if (!EqualityComparer<DateTime?>.Default.Equals(_fromDate, value))
			{
				OnPropertyChanging(nameof(FromDate));
				_fromDate = value;
				OnPropertyChanged(nameof(FromDate));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public DateTime? DeadStockBefore
	{
		get
		{
			return _deadStockBefore;
		}
		set
		{
			if (!EqualityComparer<DateTime?>.Default.Equals(_deadStockBefore, value))
			{
				OnPropertyChanging(nameof(DeadStockBefore));
				_deadStockBefore = value;
				OnPropertyChanged(nameof(DeadStockBefore));
			}
		}
	}

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
	public string ScheduleFilter
	{
		get
		{
			return _scheduleFilter;
		}
		[MemberNotNull("_scheduleFilter")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_scheduleFilter, value))
			{
				OnPropertyChanging(nameof(ScheduleFilter));
				_scheduleFilter = value;
				OnPropertyChanged(nameof(ScheduleFilter));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string ManufacturerFilter
	{
		get
		{
			return _manufacturerFilter;
		}
		[MemberNotNull("_manufacturerFilter")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_manufacturerFilter, value))
			{
				OnPropertyChanging(nameof(ManufacturerFilter));
				_manufacturerFilter = value;
				OnPropertyChanged(nameof(ManufacturerFilter));
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

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand RefreshCommand => refreshCommand ?? (refreshCommand = new AsyncRelayCommand(RefreshAsync));

	public async Task LoadAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		using IServiceScope scope = scopeFactory.CreateScope();
		PharmaBillDbContext requiredService = scope.ServiceProvider.GetRequiredService<PharmaBillDbContext>();
		Suppliers.Clear();
		foreach (Supplier item in await (from item in requiredService.Suppliers.AsNoTracking()
			where item.IsActive
			orderby item.Name
			select item).ToListAsync(cancellationToken))
		{
			Suppliers.Add(item);
		}
		await RefreshAsync(cancellationToken);
	}

	private async Task RefreshAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		_ = 2;
		try
		{
			using IServiceScope scope = scopeFactory.CreateScope();
			StockInHandService service = scope.ServiceProvider.GetRequiredService<StockInHandService>();
			DateTime asOfUtc = AsOfDate.Date.AddDays(1.0).ToUniversalTime();
			DateTime? fromUtc = (FromDate.HasValue ? new DateTime?(FromDate.Value.Date.ToUniversalTime()) : ((DateTime?)null));
			Guid? supplierId = SelectedSupplier?.Id;
			string schedule = (string.IsNullOrWhiteSpace(ScheduleFilter) ? null : ScheduleFilter.Trim());
			string manufacturer = (string.IsNullOrWhiteSpace(ManufacturerFilter) ? null : ManufacturerFilter.Trim());
			IReadOnlyList<StockInHandRow> readOnlyList = await service.GetAsOfAsync(asOfUtc, fromUtc, null, schedule, supplierId, manufacturer, cancellationToken);
			Rows.Clear();
			foreach (StockInHandRow item in readOnlyList)
			{
				Rows.Add(item);
			}
			ReorderSuggestions.Clear();
			foreach (ReorderSuggestion item2 in await service.GetReorderSuggestionsAsync(cancellationToken))
			{
				ReorderSuggestions.Add(item2);
			}
			DeadStock.Clear();
			if (DeadStockBefore.HasValue)
			{
				DateTime inactiveBeforeUtc = DeadStockBefore.Value.Date.ToUniversalTime();
				foreach (DeadStockAlert item3 in await service.GetDeadStockAsync(inactiveBeforeUtc, cancellationToken))
				{
					DeadStock.Add(item3);
				}
			}
			ErrorMessage = string.Empty;
		}
		catch (Exception ex) when ((ex is ArgumentException || ex is InvalidOperationException) ? true : false)
		{
			ErrorMessage = ex.Message;
		}
	}
}
