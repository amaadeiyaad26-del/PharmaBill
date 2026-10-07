using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
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

public sealed class StockTransferPageViewModel(IServiceScopeFactory scopeFactory, CurrentSession currentSession, IConfirmationService confirmationService, IFilePickerService filePicker, TabularExportService exportService, IPromptService promptService) : ObservableObject, ILoadablePage
{
	private StorageLocation? _sourceLocation;

	private StorageLocation? _destinationLocation;

	private DateTime _transferDate = DateTime.Today;

	private string _medicineFilter = string.Empty;

	private string _notes = string.Empty;

	private string _errorMessage = string.Empty;

	private string _statusMessage = string.Empty;

	private StockTransferLineDraft? _selectedAvailableBatch;

	private string _addQuantityText = "1";

	private Guid? _lastTransferId;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? refreshCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? refreshAvailableBatchesCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? addLineCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand<StockTransferLineDraft?>? removeLineCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? confirmTransferCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? printChallanCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? addGodownCommand;

	public ObservableCollection<StorageLocation> Locations { get; } = new ObservableCollection<StorageLocation>();

	public ObservableCollection<StockTransferLineDraft> AvailableBatches { get; } = new ObservableCollection<StockTransferLineDraft>();

	public ObservableCollection<StockTransferLineDraft> TransferLines { get; } = new ObservableCollection<StockTransferLineDraft>();

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public StorageLocation? SourceLocation
	{
		get
		{
			return _sourceLocation;
		}
		set
		{
			if (!EqualityComparer<StorageLocation>.Default.Equals(_sourceLocation, value))
			{
				OnPropertyChanging(nameof(SourceLocation));
				_sourceLocation = value;
				OnSourceLocationChanged(value);
				OnPropertyChanged(nameof(SourceLocation));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public StorageLocation? DestinationLocation
	{
		get
		{
			return _destinationLocation;
		}
		set
		{
			if (!EqualityComparer<StorageLocation>.Default.Equals(_destinationLocation, value))
			{
				OnPropertyChanging(nameof(DestinationLocation));
				_destinationLocation = value;
				OnPropertyChanged(nameof(DestinationLocation));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public DateTime TransferDate
	{
		get
		{
			return _transferDate;
		}
		set
		{
			if (!EqualityComparer<DateTime>.Default.Equals(_transferDate, value))
			{
				OnPropertyChanging(nameof(TransferDate));
				_transferDate = value;
				OnPropertyChanged(nameof(TransferDate));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string MedicineFilter
	{
		get
		{
			return _medicineFilter;
		}
		[MemberNotNull("_medicineFilter")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_medicineFilter, value))
			{
				OnPropertyChanging(nameof(MedicineFilter));
				_medicineFilter = value;
				OnPropertyChanged(nameof(MedicineFilter));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string Notes
	{
		get
		{
			return _notes;
		}
		[MemberNotNull("_notes")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_notes, value))
			{
				OnPropertyChanging(nameof(Notes));
				_notes = value;
				OnPropertyChanged(nameof(Notes));
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
	public StockTransferLineDraft? SelectedAvailableBatch
	{
		get
		{
			return _selectedAvailableBatch;
		}
		set
		{
			if (!EqualityComparer<StockTransferLineDraft>.Default.Equals(_selectedAvailableBatch, value))
			{
				OnPropertyChanging(nameof(SelectedAvailableBatch));
				_selectedAvailableBatch = value;
				OnPropertyChanged(nameof(SelectedAvailableBatch));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string AddQuantityText
	{
		get
		{
			return _addQuantityText;
		}
		[MemberNotNull("_addQuantityText")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_addQuantityText, value))
			{
				OnPropertyChanging(nameof(AddQuantityText));
				_addQuantityText = value;
				OnPropertyChanged(nameof(AddQuantityText));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public Guid? LastTransferId
	{
		get
		{
			return _lastTransferId;
		}
		set
		{
			if (!EqualityComparer<Guid?>.Default.Equals(_lastTransferId, value))
			{
				OnPropertyChanging(nameof(LastTransferId));
				_lastTransferId = value;
				OnPropertyChanged(nameof(LastTransferId));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand RefreshCommand => refreshCommand ?? (refreshCommand = new AsyncRelayCommand(RefreshAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand RefreshAvailableBatchesCommand => refreshAvailableBatchesCommand ?? (refreshAvailableBatchesCommand = new AsyncRelayCommand(RefreshAvailableBatchesAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand AddLineCommand => addLineCommand ?? (addLineCommand = new RelayCommand(AddLine));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand<StockTransferLineDraft?> RemoveLineCommand => removeLineCommand ?? (removeLineCommand = new RelayCommand<StockTransferLineDraft>(RemoveLine));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand ConfirmTransferCommand => confirmTransferCommand ?? (confirmTransferCommand = new AsyncRelayCommand(ConfirmTransferAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand PrintChallanCommand => printChallanCommand ?? (printChallanCommand = new AsyncRelayCommand(PrintChallanAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand AddGodownCommand => addGodownCommand ?? (addGodownCommand = new AsyncRelayCommand(AddGodownAsync));

	public async Task LoadAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		_ = 1;
		try
		{
			using IServiceScope scope = scopeFactory.CreateScope();
			StorageLocationService requiredService = scope.ServiceProvider.GetRequiredService<StorageLocationService>();
			Locations.Clear();
			foreach (StorageLocation item in await requiredService.GetActiveLocationsAsync(cancellationToken))
			{
				Locations.Add(item);
			}
			if (SourceLocation == null)
			{
				SourceLocation = Locations.FirstOrDefault((StorageLocation item) => !item.IsDefaultRetailLocation) ?? Locations.FirstOrDefault();
			}
			if (DestinationLocation == null)
			{
				DestinationLocation = Locations.FirstOrDefault((StorageLocation item) => item.IsDefaultRetailLocation) ?? Locations.Skip(1).FirstOrDefault() ?? Locations.FirstOrDefault();
			}
			await RefreshAvailableBatchesAsync(cancellationToken);
			ErrorMessage = string.Empty;
		}
		catch (Exception ex)
		{
			ErrorMessage = ex.Message;
		}
	}

	private Task RefreshAsync()
	{
		return LoadAsync();
	}

	private async Task RefreshAvailableBatchesAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		AvailableBatches.Clear();
		if (SourceLocation == null)
		{
			return;
		}
		try
		{
			using IServiceScope scope = scopeFactory.CreateScope();
			foreach (var item in await scope.ServiceProvider.GetRequiredService<StockTransferService>().GetSaleableBatchesAtLocationAsync(SourceLocation.Id, MedicineFilter, cancellationToken))
			{
				AvailableBatches.Add(new StockTransferLineDraft
				{
					BatchId = item.Item1,
					DrugName = item.Item2,
					BatchNo = item.Item3,
					ExpiryDate = item.Item4,
					AvailableQuantity = item.Item5,
					Rack = item.Item6,
					TransferQuantity = Math.Min(1m, item.Item5)
				});
			}
		}
		catch (Exception ex)
		{
			ErrorMessage = ex.Message;
		}
	}

	private void AddLine()
	{
		if (SelectedAvailableBatch == null)
		{
			ErrorMessage = "Select a batch from the source location.";
			return;
		}
		if (!decimal.TryParse(AddQuantityText.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var result) || result <= 0m)
		{
			ErrorMessage = "Enter a transfer quantity greater than zero.";
			return;
		}
		if (result > SelectedAvailableBatch.AvailableQuantity)
		{
			ErrorMessage = $"Only {SelectedAvailableBatch.AvailableQuantity:0.##} available at source.";
			return;
		}
		StockTransferLineDraft stockTransferLineDraft = TransferLines.FirstOrDefault((StockTransferLineDraft line) => line.BatchId == SelectedAvailableBatch.BatchId);
		if (stockTransferLineDraft != null)
		{
			stockTransferLineDraft.TransferQuantity = Math.Min(stockTransferLineDraft.AvailableQuantity, stockTransferLineDraft.TransferQuantity + result);
		}
		else
		{
			TransferLines.Add(new StockTransferLineDraft
			{
				BatchId = SelectedAvailableBatch.BatchId,
				DrugName = SelectedAvailableBatch.DrugName,
				BatchNo = SelectedAvailableBatch.BatchNo,
				ExpiryDate = SelectedAvailableBatch.ExpiryDate,
				AvailableQuantity = SelectedAvailableBatch.AvailableQuantity,
				Rack = SelectedAvailableBatch.Rack,
				TransferQuantity = result
			});
		}
		ErrorMessage = string.Empty;
		StatusMessage = "Queued " + SelectedAvailableBatch.DrugName + " for transfer.";
	}

	private void RemoveLine(StockTransferLineDraft? line)
	{
		if (line != null)
		{
			TransferLines.Remove(line);
		}
	}

	private async Task ConfirmTransferAsync()
	{
		StatusMessage = string.Empty;
		ErrorMessage = string.Empty;
		if (currentSession.User == null)
		{
			ErrorMessage = "Sign in again to transfer stock.";
		}
		else if (SourceLocation == null || DestinationLocation == null)
		{
			ErrorMessage = "Select source and destination locations.";
		}
		else if (TransferLines.Count == 0)
		{
			ErrorMessage = "Add at least one batch to the transfer challan.";
		}
		else
		{
			if (!confirmationService.Confirm($"Transfer {TransferLines.Count} line(s) from {SourceLocation.Name} to {DestinationLocation.Name}?", "Confirm stock transfer"))
			{
				return;
			}
			try
			{
				using IServiceScope scope = scopeFactory.CreateScope();
				StockTransfer stockTransfer = await scope.ServiceProvider.GetRequiredService<StockTransferService>().ConfirmTransferAsync(new SaveStockTransferInput(SourceLocation.Id, DestinationLocation.Id, DateOnly.FromDateTime(TransferDate.Date), TransferLines.Select((StockTransferLineDraft line) => new StockTransferLineInput(line.BatchId, line.TransferQuantity)).ToArray(), Notes), currentSession.User.Id, currentSession.User.Role);
				LastTransferId = stockTransfer.Id;
				TransferLines.Clear();
				StatusMessage = "Transfer " + stockTransfer.TransferNumber + " posted.";
				await RefreshAvailableBatchesAsync();
			}
			catch (Exception ex)
			{
				ErrorMessage = ex.Message;
			}
		}
	}

	private async Task PrintChallanAsync()
	{
		if (!LastTransferId.HasValue)
		{
			ErrorMessage = "Confirm a transfer first to print its challan.";
			return;
		}
		try
		{
			using IServiceScope scope = scopeFactory.CreateScope();
			StockTransferSlip stockTransferSlip = await scope.ServiceProvider.GetRequiredService<StockTransferService>().GetTransferSlipAsync(LastTransferId.Value);
			if ((object)stockTransferSlip == null)
			{
				ErrorMessage = "Transfer slip was not found.";
				return;
			}
			string destination = filePicker.PickExportDestination("pdf", "transfer-" + stockTransferSlip.TransferNumber.Replace('/', '-'));
			if (destination == null)
			{
				return;
			}
			string[] columns = new string[5] { "Medicine", "Batch", "Expiry", "Qty", "Rack" };
			IReadOnlyList<string>[] rows = ((IEnumerable<StockTransferSlipLine>)stockTransferSlip.Lines).Select((Func<StockTransferSlipLine, IReadOnlyList<string>>)((StockTransferSlipLine line) => new _003C_003Ez__ReadOnlyArray<string>(new string[5]
			{
				line.DrugName,
				line.BatchNo,
				line.ExpiryDate?.ToString("MM/yyyy") ?? string.Empty,
				line.Quantity.ToString("0.##", CultureInfo.InvariantCulture),
				line.Rack ?? string.Empty
			}))).ToArray();
			await exportService.ExportAsync(destination, $"Stock Transfer {stockTransferSlip.TransferNumber} · {stockTransferSlip.SourceLocationName} → {stockTransferSlip.DestinationLocationName}", columns, rows);
			StatusMessage = "Transfer challan saved to " + destination + ".";
		}
		catch (Exception ex)
		{
			ErrorMessage = ex.Message;
		}
	}

	private async Task AddGodownAsync()
	{
		string text = promptService.AskText("Add godown", "Enter the godown / warehouse name.", "Name", "Basement Godown");
		if (text == null)
		{
			return;
		}
		string text2 = promptService.AskText("Location code", "Short code for this location (letters/numbers).", "Code", "GODWN");
		if (text2 == null)
		{
			return;
		}
		try
		{
			using IServiceScope scope = scopeFactory.CreateScope();
			StorageLocation storageLocation = await scope.ServiceProvider.GetRequiredService<StorageLocationService>().AddLocationAsync(text, text2, isDefaultRetail: false);
			Locations.Add(storageLocation);
			StatusMessage = $"Added location {storageLocation.Name} ({storageLocation.Code}).";
			ErrorMessage = string.Empty;
		}
		catch (Exception ex)
		{
			ErrorMessage = ex.Message;
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnSourceLocationChanged(StorageLocation? value)
	{
		RefreshAvailableBatchesAsync();
	}
}
