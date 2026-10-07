using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PharmaBill.App.Services;
using PharmaBill.Core.Entities;
using PharmaBill.Core.Inventory;
using PharmaBill.Data.Persistence;
using PharmaBill.Data.Services;

namespace PharmaBill.App.ViewModels;

public sealed class StockPageViewModel(IServiceScopeFactory scopeFactory, CurrentSession currentSession, IUserSessionService userSession, IConfirmationService confirmationService, IPromptService promptService, IFilePickerService filePicker, TabularExportService exportService, INavigationService navigationService, PurchasePageViewModel purchasePage) : ObservableObject, ILoadablePage
{
	public const string SelectBatchMessage = "Select a batch in the grid first";

	public const string FilterAll = "All";

	public const string FilterShortage = "Shortage";

	public const string FilterNearExpiry = "Near expiry";

	private string _nameFilter = string.Empty;

	private string _batchFilter = string.Empty;

	private string? _scheduleFilter = "All";

	private string? _companyFilter;

	private string? _statusFilter = "All";

	private Guid? _supplierFilter;

	private string _errorMessage = string.Empty;

	private string _statusMessage = string.Empty;

	private StockBatchRow? _selectedBatch;

	private StockRowViewModel? _selectedRow;

	private string _actionQuantityText = string.Empty;

	private string _actionQuantityError = string.Empty;

	private string _actionReason = string.Empty;

	private string _actionReasonError = string.Empty;

	private string _verificationSessionNo = DefaultSessionName();

	private Guid? _activeVerificationSessionId;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand<string?>? setStockFilterCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? refreshCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? generatePurchaseIndentCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? createDraftPurchaseOrderCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? adjustStockCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? writeOffExpiredCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? startVerificationCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? postVerificationCommand;

	public bool CanViewPurchaseMargins => userSession.CanViewPurchaseMargins;

	public ObservableCollection<StockDrugRow> Items { get; } = new ObservableCollection<StockDrugRow>();

	public ObservableCollection<StockRowViewModel> Rows { get; } = new ObservableCollection<StockRowViewModel>();

	public ObservableCollection<Supplier> Suppliers { get; } = new ObservableCollection<Supplier>();

	public ObservableCollection<StockVerificationLineViewModel> VerificationItems { get; } = new ObservableCollection<StockVerificationLineViewModel>();

	public string[] StatusOptions { get; } = new string[6] { "All", "In stock", "Shortage", "Near expiry", "Critical", "Expired" };

	public string[] ScheduleOptions { get; } = new string[7] { "All", "OTC", "G", "H", "H1", "X", "NDPS" };

	public string[] ReasonOptions { get; } = new string[5] { "Damage", "Theft", "Counting error", "Expired", "Other" };

	public bool IsAllStockFilter => IsAllStatus(StatusFilter);

	public bool IsShortageFilter => IsShortageStatus(StatusFilter);

	public bool IsNearExpiryFilter => IsNearExpiryStatus(StatusFilter);

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string NameFilter
	{
		get
		{
			return _nameFilter;
		}
		[MemberNotNull("_nameFilter")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_nameFilter, value))
			{
				OnPropertyChanging(nameof(NameFilter));
				_nameFilter = value;
				OnNameFilterChanged(value);
				OnPropertyChanged(nameof(NameFilter));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string BatchFilter
	{
		get
		{
			return _batchFilter;
		}
		[MemberNotNull("_batchFilter")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_batchFilter, value))
			{
				OnPropertyChanging(nameof(BatchFilter));
				_batchFilter = value;
				OnBatchFilterChanged(value);
				OnPropertyChanged(nameof(BatchFilter));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string? ScheduleFilter
	{
		get
		{
			return _scheduleFilter;
		}
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_scheduleFilter, value))
			{
				OnPropertyChanging(nameof(ScheduleFilter));
				_scheduleFilter = value;
				OnScheduleFilterChanged(value);
				OnPropertyChanged(nameof(ScheduleFilter));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string? CompanyFilter
	{
		get
		{
			return _companyFilter;
		}
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_companyFilter, value))
			{
				OnPropertyChanging(nameof(CompanyFilter));
				_companyFilter = value;
				OnPropertyChanged(nameof(CompanyFilter));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string? StatusFilter
	{
		get
		{
			return _statusFilter;
		}
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_statusFilter, value))
			{
				OnPropertyChanging(nameof(StatusFilter));
				_statusFilter = value;
				OnStatusFilterChanged(value);
				OnPropertyChanged(nameof(StatusFilter));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public Guid? SupplierFilter
	{
		get
		{
			return _supplierFilter;
		}
		set
		{
			if (!EqualityComparer<Guid?>.Default.Equals(_supplierFilter, value))
			{
				OnPropertyChanging(nameof(SupplierFilter));
				_supplierFilter = value;
				OnPropertyChanged(nameof(SupplierFilter));
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
	public StockBatchRow? SelectedBatch
	{
		get
		{
			return _selectedBatch;
		}
		set
		{
			if (!EqualityComparer<StockBatchRow>.Default.Equals(_selectedBatch, value))
			{
				OnPropertyChanging(nameof(SelectedBatch));
				_selectedBatch = value;
				OnPropertyChanged(nameof(SelectedBatch));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public StockRowViewModel? SelectedRow
	{
		get
		{
			return _selectedRow;
		}
		set
		{
			if (!EqualityComparer<StockRowViewModel>.Default.Equals(_selectedRow, value))
			{
				OnPropertyChanging(nameof(SelectedRow));
				_selectedRow = value;
				OnSelectedRowChanged(value);
				OnPropertyChanged(nameof(SelectedRow));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string ActionQuantityText
	{
		get
		{
			return _actionQuantityText;
		}
		[MemberNotNull("_actionQuantityText")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_actionQuantityText, value))
			{
				OnPropertyChanging(nameof(ActionQuantityText));
				_actionQuantityText = value;
				OnPropertyChanged(nameof(ActionQuantityText));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string ActionQuantityError
	{
		get
		{
			return _actionQuantityError;
		}
		[MemberNotNull("_actionQuantityError")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_actionQuantityError, value))
			{
				OnPropertyChanging(nameof(ActionQuantityError));
				_actionQuantityError = value;
				OnPropertyChanged(nameof(ActionQuantityError));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string ActionReason
	{
		get
		{
			return _actionReason;
		}
		[MemberNotNull("_actionReason")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_actionReason, value))
			{
				OnPropertyChanging(nameof(ActionReason));
				_actionReason = value;
				OnPropertyChanged(nameof(ActionReason));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string ActionReasonError
	{
		get
		{
			return _actionReasonError;
		}
		[MemberNotNull("_actionReasonError")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_actionReasonError, value))
			{
				OnPropertyChanging(nameof(ActionReasonError));
				_actionReasonError = value;
				OnPropertyChanged(nameof(ActionReasonError));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string VerificationSessionNo
	{
		get
		{
			return _verificationSessionNo;
		}
		[MemberNotNull("_verificationSessionNo")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_verificationSessionNo, value))
			{
				OnPropertyChanging(nameof(VerificationSessionNo));
				_verificationSessionNo = value;
				OnPropertyChanged(nameof(VerificationSessionNo));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public Guid? ActiveVerificationSessionId
	{
		get
		{
			return _activeVerificationSessionId;
		}
		set
		{
			if (!EqualityComparer<Guid?>.Default.Equals(_activeVerificationSessionId, value))
			{
				OnPropertyChanging(nameof(ActiveVerificationSessionId));
				_activeVerificationSessionId = value;
				OnPropertyChanged(nameof(ActiveVerificationSessionId));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand<string?> SetStockFilterCommand => setStockFilterCommand ?? (setStockFilterCommand = new RelayCommand<string>(SetStockFilter));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand RefreshCommand => refreshCommand ?? (refreshCommand = new AsyncRelayCommand(RefreshAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand GeneratePurchaseIndentCommand => generatePurchaseIndentCommand ?? (generatePurchaseIndentCommand = new AsyncRelayCommand(GeneratePurchaseIndentAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand CreateDraftPurchaseOrderCommand => createDraftPurchaseOrderCommand ?? (createDraftPurchaseOrderCommand = new AsyncRelayCommand(CreateDraftPurchaseOrderAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand AdjustStockCommand => adjustStockCommand ?? (adjustStockCommand = new AsyncRelayCommand(AdjustStockAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand WriteOffExpiredCommand => writeOffExpiredCommand ?? (writeOffExpiredCommand = new AsyncRelayCommand(WriteOffExpiredAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand StartVerificationCommand => startVerificationCommand ?? (startVerificationCommand = new AsyncRelayCommand(StartVerificationAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand PostVerificationCommand => postVerificationCommand ?? (postVerificationCommand = new AsyncRelayCommand(PostVerificationAsync));

	public static string DefaultSessionName()
	{
		return $"Count {DateTime.Now:dd-MMM-yyyy HH:mm}";
	}

	public async Task LoadAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		_ = 1;
		try
		{
			using IServiceScope scope = scopeFactory.CreateScope();
			InventoryService inventory = scope.ServiceProvider.GetRequiredService<InventoryService>();
			PharmaBillDbContext requiredService = scope.ServiceProvider.GetRequiredService<PharmaBillDbContext>();
			Suppliers.Clear();
			foreach (Supplier item in await (from item in requiredService.Suppliers
				where item.IsActive
				orderby item.Name
				select item).ToListAsync(cancellationToken))
			{
				Suppliers.Add(item);
			}
			string scheduleFilter = ScheduleFilter;
			bool flag = ((scheduleFilter == null || scheduleFilter == "All") ? true : false);
			IReadOnlyList<StockDrugRow> readOnlyList = await inventory.GetStockAsync(flag ? null : ScheduleFilter, CompanyFilter, SupplierFilter, null, cancellationToken);
			Items.Clear();
			foreach (StockDrugRow item2 in readOnlyList)
			{
				Items.Add(item2);
			}
			ApplyFilters();
			ErrorMessage = string.Empty;
		}
		catch (Exception ex)
		{
			ErrorMessage = ex.Message;
		}
	}

	public void ApplyFilters()
	{
		Guid? selectedId = SelectedRow?.BatchId;
		string text = NameFilter.Trim();
		string text2 = BatchFilter.Trim();
		List<StockRowViewModel> list = new List<StockRowViewModel>();
		foreach (StockDrugRow item in Items)
		{
			if (text.Length > 0 && !item.DrugName.Contains(text, StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}
			foreach (StockBatchRow batch2 in item.Batches)
			{
				if (!(batch2.Quantity <= 0m) && (text2.Length <= 0 || batch2.BatchNo.Contains(text2, StringComparison.OrdinalIgnoreCase)))
				{
					string status = RowStatus(item, batch2);
					if (MatchesStatusFilter(StatusFilter, item, batch2))
					{
						list.Add(new StockRowViewModel(item.DrugId, item.DrugName, item.Schedule, batch2, status, item.ReorderLevel, item.TotalStock));
					}
				}
			}
		}
		if (IsShortageStatus(StatusFilter))
		{
			foreach (StockDrugRow drug in Items.Where((StockDrugRow item) => item.IsShortage))
			{
				if ((text.Length <= 0 || drug.DrugName.Contains(text, StringComparison.OrdinalIgnoreCase)) && !list.Any((StockRowViewModel row) => row.DrugId == drug.DrugId))
				{
					StockBatchRow batch = drug.Batches.OrderBy((StockBatchRow stockBatchRow) => stockBatchRow.ExpiryDate ?? DateOnly.MaxValue).FirstOrDefault() ?? new StockBatchRow(Guid.Empty, "—", null, 0m, null, 0m, 0m, null);
					list.Add(new StockRowViewModel(drug.DrugId, drug.DrugName, drug.Schedule, batch, "Shortage", drug.ReorderLevel, drug.TotalStock));
				}
			}
		}
		Rows.Clear();
		foreach (StockRowViewModel item2 in list)
		{
			Rows.Add(item2);
		}
		SelectedRow = Rows.FirstOrDefault((StockRowViewModel row) =>
		{
			Guid batchId = row.BatchId;
			Guid? guid = selectedId;
			return batchId == guid;
		});
	}

	public static string RowStatus(StockDrugRow drug, StockBatchRow batch)
	{
		return batch.ExpiryBand switch
		{
			BatchExpiryStatus.Critical => (batch.ExpiryDate.HasValue && batch.ExpiryDate.Value < DateOnly.FromDateTime(DateTime.Today)) ? "Expired" : "Critical", 
			BatchExpiryStatus.Warning => "Near expiry", 
			_ => drug.IsShortage ? "Shortage" : "OK", 
		};
	}

	public void ShowStatus(string status)
	{
		NameFilter = string.Empty;
		BatchFilter = string.Empty;
		StatusFilter = NormalizeStatus(status);
	}

	public async Task ShowMedicineAsync(string medicineName)
	{
		StatusFilter = "All";
		NameFilter = medicineName;
		await LoadAsync();
	}

	private void SetStockFilter(string? filter)
	{
		StatusFilter = NormalizeStatus(filter ?? "All");
	}

	private Task RefreshAsync()
	{
		return LoadAsync();
	}

	private async Task GeneratePurchaseIndentAsync()
	{
		StatusMessage = string.Empty;
		ErrorMessage = string.Empty;
		try
		{
			using IServiceScope scope = scopeFactory.CreateScope();
			List<PurchaseIndentItem> items = await scope.ServiceProvider.GetRequiredService<ShortageIndentService>().GetAutomatedShortageIndentAsync();
			if (items.Count == 0)
			{
				StatusMessage = "No shortage items to indent. Set reorder levels on medicines that need restocking.";
				return;
			}
			string[] columns = new string[7] { "Medicine", "Schedule", "On hand", "Reorder level", "Target stock", "Suggested order", "Supplier" };
			IReadOnlyList<string>[] rows = ((IEnumerable<PurchaseIndentItem>)items).Select((Func<PurchaseIndentItem, IReadOnlyList<string>>)((PurchaseIndentItem item) => new _003C_003Ez__ReadOnlyArray<string>(new string[7]
			{
				item.DrugName,
				item.Schedule ?? string.Empty,
				item.CurrentStock.ToString("0.##", CultureInfo.InvariantCulture),
				item.ReorderLevel.ToString("0.##", CultureInfo.InvariantCulture),
				item.TargetStockLevel.ToString("0.##", CultureInfo.InvariantCulture),
				item.SuggestedOrderQty.ToString("0.##", CultureInfo.InvariantCulture),
				item.SupplierName ?? "General requisition"
			}))).ToArray();
			Clipboard.SetText(ShortageIndentService.FormatIndentText(items));
			string destination = filePicker.PickExportDestination("csv", $"shortage-indent-{DateTime.Now:yyyyMMdd-HHmm}");
			if (destination != null)
			{
				await exportService.ExportAsync(destination, "Shortage purchase indent", columns, rows);
				StatusMessage = $"Purchase indent ({items.Count} lines) copied to clipboard and saved to {destination}.";
			}
			else
			{
				StatusMessage = $"Purchase indent ({items.Count} lines) copied to clipboard. Save was cancelled.";
			}
			StatusFilter = "Shortage";
		}
		catch (Exception ex)
		{
			ErrorMessage = ex.Message;
		}
	}

	private async Task CreateDraftPurchaseOrderAsync()
	{
		StatusMessage = string.Empty;
		ErrorMessage = string.Empty;
		try
		{
			using IServiceScope scope = scopeFactory.CreateScope();
			IReadOnlyList<PurchaseIndentGroup> readOnlyList = await scope.ServiceProvider.GetRequiredService<ShortageIndentService>().GetIndentGroupedBySupplierAsync();
			if (readOnlyList.Count == 0)
			{
				StatusMessage = "No shortage items to convert into a purchase draft.";
				return;
			}
			PurchaseIndentGroup selected;
			if (readOnlyList.Count == 1)
			{
				selected = readOnlyList[0];
			}
			else
			{
				string choice = promptService.AskText("Draft purchase order", "Choose which supplier group to open as a purchase draft." + Environment.NewLine + string.Join(Environment.NewLine, readOnlyList.Select((PurchaseIndentGroup group) => $"• {group.SupplierName} ({group.Items.Count})")), "Supplier name", readOnlyList.FirstOrDefault((PurchaseIndentGroup group) => group.SupplierId.HasValue)?.SupplierName ?? readOnlyList[0].SupplierName);
				if (choice == null)
				{
					return;
				}
				selected = readOnlyList.FirstOrDefault((PurchaseIndentGroup group) => group.SupplierName.Contains(choice.Trim(), StringComparison.OrdinalIgnoreCase)) ?? readOnlyList.FirstOrDefault((PurchaseIndentGroup group) => group.SupplierId.HasValue) ?? readOnlyList[0];
			}
			await purchasePage.LoadShortageIndentAsync(selected);
			navigationService.Navigate("Purchases");
			StatusMessage = $"Opened purchase draft for {selected.SupplierName} with {selected.Items.Count} shortage line(s).";
		}
		catch (Exception ex)
		{
			ErrorMessage = ex.Message;
		}
	}

	private async Task AdjustStockAsync()
	{
		StatusMessage = string.Empty;
		ActionQuantityError = string.Empty;
		ActionReasonError = string.Empty;
		if (!TryGetUser(out AppUser user))
		{
			return;
		}
		if ((object)SelectedRow == null)
		{
			ErrorMessage = "Select a batch in the grid first";
			return;
		}
		StockRowViewModel row = SelectedRow;
		decimal change;
		if (row.BatchId == Guid.Empty)
		{
			ErrorMessage = "This shortage row has no batch. Add stock or pick a batch for this medicine.";
		}
		else if (!decimal.TryParse(ActionQuantityText.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out change) || change == 0m)
		{
			ActionQuantityError = "Enter a quantity change other than 0 (use - to reduce)";
			ErrorMessage = string.Empty;
		}
		else if (row.Quantity + change < 0m)
		{
			ActionQuantityError = $"Stock cannot go below 0 (current stock {row.Quantity:0.##})";
			ErrorMessage = string.Empty;
		}
		else if (string.IsNullOrWhiteSpace(ActionReason))
		{
			ActionReasonError = "Reason is required";
			ErrorMessage = string.Empty;
		}
		else if (confirmationService.Confirm($"Apply stock adjustment of {change:+0.##;-0.##} to {row.Medicine}, batch {row.BatchNo}?", "Confirm stock adjustment"))
		{
			string reason = ActionReason.Trim();
			await RunInventoryActionAsync(async (IServiceScope scope) =>
			{
				await scope.ServiceProvider.GetRequiredService<InventoryService>().AdjustStockAsync(row.BatchId, change, reason, user.Id, user.Role);
			});
			if (ErrorMessage.Length == 0)
			{
				StatusMessage = $"Stock adjusted for {row.Medicine}, batch {row.BatchNo}.";
			}
		}
	}

	private async Task WriteOffExpiredAsync()
	{
		StatusMessage = string.Empty;
		if (!TryGetUser(out AppUser user))
		{
			return;
		}
		try
		{
			IReadOnlyList<ExpiredBatchRow> readOnlyList;
			using (IServiceScope scope = scopeFactory.CreateScope())
			{
				readOnlyList = await scope.ServiceProvider.GetRequiredService<InventoryService>().GetExpiredBatchesAsync();
			}
			if (readOnlyList.Count == 0)
			{
				ErrorMessage = string.Empty;
				StatusMessage = "No expired batches with stock to write off.";
				return;
			}
			string text = string.Join(Environment.NewLine, from row in readOnlyList.Take(15)
				select $"{row.DrugName} | batch {row.BatchNo} | exp {row.ExpiryDate:MM/yyyy} | qty {row.Quantity:0.##}");
			if (readOnlyList.Count > 15)
			{
				text += $"{Environment.NewLine}...and {readOnlyList.Count - 15} more";
			}
			if (confirmationService.Confirm($"Write off {readOnlyList.Count} expired batch(es)? A dump register entry is recorded for each.{Environment.NewLine}{Environment.NewLine}{text}", "Write off expired stock"))
			{
				string reason = (string.IsNullOrWhiteSpace(ActionReason) ? "Expired" : ActionReason.Trim());
				int value;
				using (IServiceScope scope = scopeFactory.CreateScope())
				{
					value = await scope.ServiceProvider.GetRequiredService<InventoryService>().WriteOffAllExpiredAsync(reason, user.Id, user.Role);
				}
				ErrorMessage = string.Empty;
				StatusMessage = $"{value} expired batch(es) written off.";
				await LoadAsync();
			}
		}
		catch (Exception ex)
		{
			ErrorMessage = ex.Message;
		}
	}

	private async Task StartVerificationAsync()
	{
		StatusMessage = string.Empty;
		if (!TryGetUser(out AppUser user) || string.IsNullOrWhiteSpace(VerificationSessionNo))
		{
			ErrorMessage = "Sign in and enter a count session name.";
			return;
		}
		try
		{
			using IServiceScope scope = scopeFactory.CreateScope();
			StockVerificationSession session = await scope.ServiceProvider.GetRequiredService<StockVerificationService>().StartAsync(VerificationSessionNo.Trim(), user.Id, user.Role);
			ActiveVerificationSessionId = session.Id;
			PharmaBillDbContext requiredService = scope.ServiceProvider.GetRequiredService<PharmaBillDbContext>();
			List<StockVerificationLineViewModel> list = await (from item in requiredService.StockVerificationItems
				where item.SessionId == session.Id
				join batch in requiredService.Batches on item.BatchId equals batch.Id
				select new
				{
					Item = item,
					Batch = batch
				} into row
				join drug in requiredService.Drugs on row.Batch.DrugId equals drug.Id
				select new StockVerificationLineViewModel(row.Item.BatchId, $"{drug.Name} / {row.Batch.BatchNo}", row.Item.ExpectedQuantity, row.Item.CountedQuantity)).ToListAsync();
			VerificationItems.Clear();
			foreach (StockVerificationLineViewModel item in list)
			{
				VerificationItems.Add(item);
			}
			ErrorMessage = string.Empty;
		}
		catch (Exception ex)
		{
			ErrorMessage = ex.Message;
		}
	}

	private async Task PostVerificationAsync()
	{
		StatusMessage = string.Empty;
		if (!TryGetUser(out AppUser user))
		{
			return;
		}
		if (!ActiveVerificationSessionId.HasValue)
		{
			ErrorMessage = "Start a physical count first.";
			return;
		}
		UserRole role = user.Role;
		if ((uint)role > 1u)
		{
			ErrorMessage = "Only the Owner or a Manager can post a physical count.";
			return;
		}
		List<StockVerificationLineViewModel> list = VerificationItems.Where((StockVerificationLineViewModel item) => item.Difference != 0m).ToList();
		if (list.Count == 0)
		{
			ErrorMessage = "No differences found. Enter the counted quantities first.";
			return;
		}
		string value = string.Join(Environment.NewLine, from item in list.Take(15)
			select $"{item.Display}: expected {item.ExpectedQuantity:0.##}, counted {item.CountedQuantity:0.##}, difference {item.Difference:+0.##;-0.##}");
		if (!confirmationService.Confirm($"Post these {list.Count} difference(s)?{Environment.NewLine}{Environment.NewLine}{value}", "Post physical count"))
		{
			return;
		}
		string reason = promptService.AskText("Reason for stock differences", "Enter the reason for these adjustments.", "Reason");
		if (reason == null)
		{
			return;
		}
		try
		{
			using IServiceScope scope = scopeFactory.CreateScope();
			StockVerificationService service = scope.ServiceProvider.GetRequiredService<StockVerificationService>();
			foreach (StockVerificationLineViewModel verificationItem in VerificationItems)
			{
				await service.SetCountedQuantityAsync(ActiveVerificationSessionId.Value, verificationItem.BatchId, verificationItem.CountedQuantity);
			}
			await service.PostAsync(ActiveVerificationSessionId.Value, user.Id, user.Role, reason);
			ActiveVerificationSessionId = null;
			VerificationItems.Clear();
			VerificationSessionNo = DefaultSessionName();
			ErrorMessage = string.Empty;
			StatusMessage = "Physical count posted.";
			await LoadAsync();
		}
		catch (Exception ex)
		{
			ErrorMessage = ex.Message;
		}
	}

	private bool TryGetUser(out AppUser user)
	{
		user = currentSession.User;
		if (user != null)
		{
			return true;
		}
		ErrorMessage = "Sign in again to manage stock.";
		return false;
	}

	private async Task RunInventoryActionAsync(Func<IServiceScope, Task> action)
	{
		_ = 1;
		try
		{
			using IServiceScope scope = scopeFactory.CreateScope();
			await action(scope);
			ActionQuantityText = string.Empty;
			ActionReason = string.Empty;
			ErrorMessage = string.Empty;
			await LoadAsync();
		}
		catch (Exception ex)
		{
			ErrorMessage = ex.Message;
		}
	}

	private static bool MatchesStatusFilter(string? filter, StockDrugRow drug, StockBatchRow batch)
	{
		return NormalizeStatus(filter) switch
		{
			"In stock" => true, 
			"Shortage" => drug.IsShortage, 
			"Near expiry" => batch.IsNearExpiry, 
			"Critical" => batch.ExpiryBand == BatchExpiryStatus.Critical && !(batch.ExpiryDate < DateOnly.FromDateTime(DateTime.Today)), 
			"Expired" => batch.ExpiryDate.HasValue && batch.ExpiryDate.Value < DateOnly.FromDateTime(DateTime.Today), 
			_ => true, 
		};
	}

	private static string NormalizeStatus(string? status)
	{
		string text = status?.Trim();
		if (text != null && (text == null || text.Length != 0))
		{
			switch (text)
			{
			case "All Stock":
				break;
			case "Low stock":
			case "Reorder":
				return "Shortage";
			case "Warning":
				return "Near expiry";
			default:
				return text;
			}
		}
		return "All";
	}

	private static bool IsAllStatus(string? status)
	{
		return NormalizeStatus(status) == "All";
	}

	private static bool IsShortageStatus(string? status)
	{
		return NormalizeStatus(status) == "Shortage";
	}

	private static bool IsNearExpiryStatus(string? status)
	{
		switch (NormalizeStatus(status))
		{
		case "Near expiry":
		case "Critical":
		case "Expired":
			return true;
		default:
			return false;
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnNameFilterChanged(string value)
	{
		ApplyFilters();
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnBatchFilterChanged(string value)
	{
		ApplyFilters();
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnScheduleFilterChanged(string? value)
	{
		LoadAsync();
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnStatusFilterChanged(string? value)
	{
		OnPropertyChanged("IsAllStockFilter");
		OnPropertyChanged("IsShortageFilter");
		OnPropertyChanged("IsNearExpiryFilter");
		ApplyFilters();
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnSelectedRowChanged(StockRowViewModel? value)
	{
		SelectedBatch = value?.Batch;
	}
}
