using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using PharmaBill.App.Services;
using PharmaBill.Core.Pos;
using PharmaBill.Data.Services;

namespace PharmaBill.App.ViewModels;

// The counter command bar: "sold 1 strip of pacimol to mr x" becomes a previewed bill line. Nothing is sold here: the
// line joins the bill like any other, and Save & Print issues the invoice and takes the stock, with every billing rule.
public sealed partial class RetailBillingViewModel
{
	/// <summary>"Batch B1 · exp 08/27 · 12 in stock · MRP ₹30.00" for the batch dropdown.</summary>
	public static readonly System.Windows.Data.IValueConverter BatchLabel = new BatchLabelConverter();

	public const string CommandHint = "Type a sale, e.g. 1 strip paracetamol to John · Enter to check, Enter again to add";

	private string _posCommandText = string.Empty;
	private PosCommandPreview? _posPreview;
	private string _posMessage = string.Empty;
	private bool _posBusy;
	private PosSaleCommand? _posMissingCommand;
	private AsyncRelayCommand? runPosCommandCommand;
	private AsyncRelayCommand? submitPosCommandCommand;
	private RelayCommand? confirmPosCommandCommand;
	private RelayCommand? cancelPosCommandCommand;
	private RelayCommand<PosMissingMedicine?>? addMissingViaPurchaseCommand;
	private AsyncRelayCommand<PosMissingMedicine?>? addMissingToStockCommand;

	public string PosCommandText
	{
		get => _posCommandText;
		set
		{
			if (SetProperty(ref _posCommandText, value))
			{
				// A changed command is a new question: the old preview no longer answers it.
				PosPreview = null;
				ClearPosMissing();
			}
		}
	}

	public PosCommandPreview? PosPreview
	{
		get => _posPreview;
		private set
		{
			if (SetProperty(ref _posPreview, value))
			{
				OnPropertyChanged(nameof(HasPosPreview));
			}
		}
	}

	public bool HasPosPreview => PosPreview != null;

	public string PosMessage
	{
		get => _posMessage;
		private set => SetProperty(ref _posMessage, value);
	}

	/// <summary>The medicine is not in sellable stock: the nearest matches, each with a way to get it into stock.</summary>
	public ObservableCollection<PosMissingMedicine> PosMissing { get; } = new ObservableCollection<PosMissingMedicine>();

	public bool HasPosMissing => PosMissing.Count > 0 || _posMissingCommand != null;

	public string PosMissingTitle => _posMissingCommand == null ? string.Empty : $"\"{_posMissingCommand.MedicineText}\" is not in stock";

	/// <summary>Enter in the command box.</summary>
	public IAsyncRelayCommand SubmitPosCommandCommand => submitPosCommandCommand ??= new AsyncRelayCommand(SubmitPosCommandAsync);

	public IAsyncRelayCommand RunPosCommandCommand => runPosCommandCommand ??= new AsyncRelayCommand(RunPosCommandAsync, () => !_posBusy);

	public IRelayCommand ConfirmPosCommandCommand => confirmPosCommandCommand ??= new RelayCommand(ConfirmPosCommand);

	public IRelayCommand CancelPosCommandCommand => cancelPosCommandCommand ??= new RelayCommand(CancelPosCommand);

	public IRelayCommand<PosMissingMedicine?> AddMissingViaPurchaseCommand => addMissingViaPurchaseCommand ??= new RelayCommand<PosMissingMedicine?>(AddMissingViaPurchase);

	public IAsyncRelayCommand<PosMissingMedicine?> AddMissingToStockCommand => addMissingToStockCommand ??= new AsyncRelayCommand<PosMissingMedicine?>(AddMissingToStockAsync);

	/// <summary>Enter in the command box: check the command, or, when its preview is showing, add it.</summary>
	public async Task SubmitPosCommandAsync()
	{
		if (PosPreview != null)
		{
			ConfirmPosCommand();
			return;
		}
		await RunPosCommandAsync();
	}

	private async Task RunPosCommandAsync()
	{
		PosPreview = null;
		ClearPosMissing();
		if (!PosCommandParser.TryParse(PosCommandText, out PosSaleCommand? command, out string error))
		{
			PosMessage = error;
			return;
		}

		_posBusy = true;
		runPosCommandCommand?.NotifyCanExecuteChanged();
		PosMessage = "Looking for " + command!.MedicineText + "…";
		try
		{
			using var scope = scopeFactory.CreateScope();
			PosMedicineMatch match = await scope.ServiceProvider.GetRequiredService<PosMedicineMatcher>().MatchAsync(command.MedicineText);
			if (match.InStock == null)
			{
				// Not a dead end: say what it is and offer the two ways to get it into stock.
				_posMissingCommand = command;
				foreach (PosMissingMedicine missing in match.NotInStock)
				{
					PosMissing.Add(missing);
				}
				OnPropertyChanged(nameof(HasPosMissing));
				OnPropertyChanged(nameof(PosMissingTitle));
				PosMessage = match.NotInStock.Count == 0
					? $"\"{command.MedicineText}\" is not in stock or in the drug bank. Check the spelling, or add it as a new medicine."
					: "Pick the medicine below and add it to stock; the sale continues once it is in.";
				return;
			}

			var notes = new List<string>();
			if (match.InStock.SpellingCorrected)
			{
				notes.Add($"\"{command.MedicineText}\" read as {match.InStock.DrugName}.");
			}
			decimal packs = command.Quantity;
			string quantityText = $"{command.Quantity:0.##} {command.UnitWord}{(command.Quantity == 1m ? string.Empty : "s")}";
			if (command.Unit == PosQuantityUnit.Piece)
			{
				int? perPack = PosCommandParser.PiecesPerPack(match.InStock.PackLabel);
				if (perPack == null)
				{
					PosMessage = $"Stock of {match.InStock.DrugName} is counted in packs, and its pack size is not on record. Type it in packs, e.g. 1 strip {match.InStock.DrugName}.";
					return;
				}
				packs = decimal.Round(command.Quantity / perPack.Value, 4, MidpointRounding.AwayFromZero);
				quantityText += $" ({packs:0.####} of a pack of {perPack})";
				notes.Add($"{command.Quantity:0.##} {command.UnitWord}s = {packs:0.####} pack ({match.InStock.PackLabel}).");
			}
			PosPreview = new PosCommandPreview(match.InStock, packs, quantityText, command.Customer, notes);
			PosMessage = PosPreview.HasBatchChoice
				? $"{match.InStock.Batches.Count} batches in stock: the earliest expiry is selected. Press Enter or Add to bill."
				: "Press Enter or Add to bill.";
		}
		catch (Exception exception)
		{
			PosMessage = PharmaBill.Core.UserMessage.Of(exception);
		}
		finally
		{
			_posBusy = false;
			runPosCommandCommand?.NotifyCanExecuteChanged();
		}
	}

	private void ConfirmPosCommand()
	{
		PosCommandPreview? preview = PosPreview;
		if (preview == null)
		{
			return;
		}
		if (!preview.EnoughStock)
		{
			PosMessage = preview.StockWarning;
			return;
		}

		int linesBefore = BillItems.Count;
		decimal quantityBefore = BillItems.Where(line => line.Choice.BatchId == preview.SelectedBatch.BatchId).Sum(line => line.Quantity);
		AddStockChoice(preview.SelectedBatch, preview.PackQuantity, preview.Batches, $"{preview.DrugName} · {preview.QuantityText} · {preview.BatchSummary}");
		bool added = BillItems.Count > linesBefore || BillItems.Where(line => line.Choice.BatchId == preview.SelectedBatch.BatchId).Sum(line => line.Quantity) > quantityBefore;
		if (!added)
		{
			// The bill refused it (a banned batch, or more than the batch holds with what is already on the bill).
			PosMessage = string.IsNullOrWhiteSpace(ErrorMessage) ? "The line could not be added." : ErrorMessage;
			return;
		}

		string customerNote = string.Empty;
		if (preview.HasCustomer)
		{
			if (string.IsNullOrWhiteSpace(PatientName))
			{
				PatientName = preview.Customer!;
			}
			else if (!string.Equals(PatientName.Trim(), preview.Customer, StringComparison.OrdinalIgnoreCase))
			{
				customerNote = $" The bill is already for {PatientName}; change the patient name if it is for {preview.Customer}.";
			}
		}
		PosMessage = $"Added {preview.DrugName} ({preview.QuantityText}) to the bill.{customerNote}";
		_posCommandText = string.Empty;
		OnPropertyChanged(nameof(PosCommandText));
		PosPreview = null;
	}

	private void CancelPosCommand()
	{
		PosPreview = null;
		ClearPosMissing();
		PosMessage = string.Empty;
	}

	// "Add via Purchase Entry": Purchases opens with this medicine searched, ready for its batch and expiry.
	private void AddMissingViaPurchase(PosMissingMedicine? medicine)
	{
		if (medicine == null)
		{
			return;
		}
		if (purchaseEntry != null)
		{
			purchaseEntry.MedicineName = medicine.Name;
		}
		PosMessage = $"Enter the purchase of {medicine.Name}; this bill stays as it is.";
		navigationService.Navigate("Purchases");
	}

	// "Add directly to stock": the stock form, filled with this medicine. Once saved, the same command is checked again.
	private async Task AddMissingToStockAsync(PosMissingMedicine? medicine)
	{
		if (medicine == null || addStockDialog == null)
		{
			return;
		}
		bool saved = await addStockDialog.ShowAsync(new AddStockRequest(medicine.DrugId, medicine.CatalogMedicineId, medicine.Name, medicine.Composition, medicine.Manufacturer));
		if (!saved)
		{
			return;
		}
		if (_posMissingCommand != null)
		{
			// Ask again by the name now in stock, keeping the quantity and customer.
			PosSaleCommand again = _posMissingCommand with { MedicineText = medicine.Name };
			_posCommandText = $"{again.Quantity:0.##} {again.UnitWord} {again.MedicineText}{(again.Customer == null ? string.Empty : " to " + again.Customer)}";
			OnPropertyChanged(nameof(PosCommandText));
		}
		await RunPosCommandAsync();
	}

	private void ClearPosMissing()
	{
		_posMissingCommand = null;
		PosMissing.Clear();
		OnPropertyChanged(nameof(HasPosMissing));
		OnPropertyChanged(nameof(PosMissingTitle));
	}
}

internal sealed class BatchLabelConverter : System.Windows.Data.IValueConverter
{
	public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
		value is RetailStockChoice batch ? PosCommandPreview.Describe(batch) : string.Empty;

	public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
