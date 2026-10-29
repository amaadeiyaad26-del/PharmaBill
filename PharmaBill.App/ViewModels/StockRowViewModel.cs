using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using PharmaBill.Core.Inventory;
using PharmaBill.Data.Services;

namespace PharmaBill.App.ViewModels;

public sealed class StockRowViewModel : ObservableObject
{
	private readonly Action<StockRowViewModel>? _commitMrp;

	private decimal _mrp;

	private bool _writingMrp;

	private readonly Func<StockRowViewModel, DateOnly?, bool, Task<bool>>? _commitExpiry;

	private string _expiryEditText = string.Empty;

	private bool _noExpiryTick;

	private string _expiryError = string.Empty;

	public StockRowViewModel(Guid drugId, string medicine, string? schedule, StockBatchRow batch, string status, decimal reorderLevel, decimal totalStock, Action<StockRowViewModel>? commitMrp = null, Func<StockRowViewModel, DateOnly?, bool, Task<bool>>? commitExpiry = null)
	{
		_commitExpiry = commitExpiry;
		SaveExpiryCommand = new CommunityToolkit.Mvvm.Input.AsyncRelayCommand(SaveExpiryAsync);
		DrugId = drugId;
		Medicine = medicine;
		Schedule = schedule;
		Batch = batch;
		Status = status;
		ReorderLevel = reorderLevel;
		TotalStock = totalStock;
		_mrp = batch.Mrp;
		_commitMrp = commitMrp;
	}

	public Guid DrugId { get; }

	public string Medicine { get; }

	public string? Schedule { get; }

	public StockBatchRow Batch { get; private set; }

	public string Status { get; }

	public decimal ReorderLevel { get; }

	public decimal TotalStock { get; }

	public Guid BatchId => Batch.BatchId;

	public string BatchNo => Batch.BatchNo;

	public DateOnly? ExpiryDate => Batch.ExpiryDate;

	/// <summary>No expiry date and no recorded reason: shown flagged, with a box to give it a date.</summary>
	public bool NeedsExpiry => Batch.NeedsExpiry && Batch.BatchId != Guid.Empty;

	public string ExpiryLabel => Batch.ExpiryDate is DateOnly date ? date.ToString("MM/yyyy", CultureInfo.InvariantCulture)
		: Batch.NoExpiryReason == NoExpiryReason.DurableInstrument ? "None (instrument)"
		: Batch.NoExpiryReason == NoExpiryReason.NotPrintedOnPack ? "None (not on pack)"
		: "Needs a date";

	/// <summary>The date typed for a batch that has none: MM/YY.</summary>
	public string ExpiryEditText
	{
		get => _expiryEditText;
		set => SetProperty(ref _expiryEditText, value ?? string.Empty);
	}

	/// <summary>"No expiry printed on pack", for a batch whose pack genuinely shows none.</summary>
	public bool NoExpiryTick
	{
		get => _noExpiryTick;
		set => SetProperty(ref _noExpiryTick, value);
	}

	public string ExpiryError
	{
		get => _expiryError;
		private set => SetProperty(ref _expiryError, value);
	}

	public CommunityToolkit.Mvvm.Input.IAsyncRelayCommand SaveExpiryCommand { get; }

	private async Task SaveExpiryAsync()
	{
		DateOnly? expiry = null;
		if (ExpiryEditText.Trim().Length > 0)
		{
			if (!ExpiryRule.TryParseTyped(ExpiryEditText, out DateOnly parsed))
			{
				ExpiryError = "Type the expiry as MM/YY, for example 10/27.";
				return;
			}
			expiry = parsed;
		}
		else if (!NoExpiryTick)
		{
			ExpiryError = "Type the expiry from the pack (MM/YY), or tick \"" + ExpiryRule.NoExpiryPrintedLabel + "\".";
			return;
		}
		ExpiryError = string.Empty;
		if (_commitExpiry != null && await _commitExpiry(this, expiry, NoExpiryTick))
		{
			OnPropertyChanged(nameof(NeedsExpiry));
			OnPropertyChanged(nameof(ExpiryLabel));
			OnPropertyChanged(nameof(ExpiryDate));
		}
	}

	/// <summary>After a save: the batch as now stored.</summary>
	public void RememberSavedBatch(StockBatchRow batch) => Batch = batch;

	public decimal PurchaseRate => Batch.PurchaseRate;

	public decimal Quantity => Batch.Quantity;

	public bool IsShortage => StockShortage.IsShortage(TotalStock, ReorderLevel);

	public decimal Mrp => _mrp;

	public bool RequiresMrp => _mrp <= 0m;

	public string MrpLabel => RequiresMrp ? "✎ MRP Required / ₹0.00" : _mrp.ToString("₹0.00", CultureInfo.GetCultureInfo("en-IN"));

	public string MrpEditText
	{
		get => _mrp.ToString("0.00", CultureInfo.CurrentCulture);
		set
		{
			if (_writingMrp || !MrpAmount.TryParse(value, out decimal parsed) || parsed < 0m || parsed == _mrp)
			{
				return;
			}

			_mrp = parsed;
			NotifyMrp();
			_commitMrp?.Invoke(this);
		}
	}

	public void RememberSavedMrp(decimal mrp, StockBatchRow batch)
	{
		Batch = batch;
		_writingMrp = true;
		_mrp = mrp;
		NotifyMrp();
		_writingMrp = false;
	}

	private void NotifyMrp()
	{
		OnPropertyChanged(nameof(Mrp));
		OnPropertyChanged(nameof(RequiresMrp));
		OnPropertyChanged(nameof(MrpLabel));
		OnPropertyChanged(nameof(MrpEditText));
	}
}

internal static class MrpAmount
{
	public static bool TryParse(string? text, out decimal mrp)
	{
		mrp = 0m;
		if (string.IsNullOrWhiteSpace(text))
		{
			return false;
		}

		string cleaned = text.Trim().TrimStart('₹', ' ').Replace(",", string.Empty);
		return decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.CurrentCulture, out mrp)
			|| decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out mrp);
	}
}
