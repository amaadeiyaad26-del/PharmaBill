using System;
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

	public StockRowViewModel(Guid drugId, string medicine, string? schedule, StockBatchRow batch, string status, decimal reorderLevel, decimal totalStock, Action<StockRowViewModel>? commitMrp = null)
	{
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
