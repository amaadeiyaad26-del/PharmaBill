using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;

namespace PharmaBill.App.ViewModels;

public class WholesaleInvoiceLineDraft : ObservableObject
{
	private WholesaleStockChoice? _stockChoice;

	private decimal _quantity = 1m;

	private decimal _freeQuantity;

	private decimal _unitPrice;

	private decimal _discountAmount;

	private decimal _discountPercent;

	private string _saleUnit = "Unit";

	public ObservableCollection<WholesaleStockChoice> BatchOptions { get; } = new ObservableCollection<WholesaleStockChoice>();

	public void SetBatchOptions(IEnumerable<WholesaleStockChoice> options)
	{
		Guid? selected = _stockChoice?.BatchId;
		BatchOptions.Clear();
		foreach (WholesaleStockChoice option in options.OrderBy(choice => choice.ExpiryDate ?? DateOnly.MaxValue).ThenBy(choice => choice.BatchNo, StringComparer.OrdinalIgnoreCase))
		{
			if (BatchOptions.All(choice => choice.BatchId != option.BatchId))
			{
				BatchOptions.Add(option);
			}
		}

		if (_stockChoice != null && BatchOptions.All(choice => choice.BatchId != _stockChoice.BatchId))
		{
			BatchOptions.Insert(0, _stockChoice);
		}
		else if (selected.HasValue)
		{
			WholesaleStockChoice? same = BatchOptions.FirstOrDefault(choice => choice.BatchId == selected.Value);
			if (same != null)
			{
				_stockChoice = same;
			}
		}

		OnPropertyChanged(nameof(BatchOptions));
		OnPropertyChanged(nameof(StockChoice));
	}

	public WholesaleStockChoice? StockChoice
	{
		get => _stockChoice;
		set
		{
			if (!EqualityComparer<WholesaleStockChoice>.Default.Equals(_stockChoice, value))
			{
				OnPropertyChanging(nameof(StockChoice));
				_stockChoice = value;
				OnStockChoiceChanged(value);
				OnPropertyChanged(nameof(StockChoice));
				OnPropertyChanged(nameof(Mrp));
				OnPropertyChanged(nameof(PackLabel));
				SyncDiscountFromPercent();
				RaiseComputed();
			}
		}
	}

	public decimal Quantity
	{
		get => _quantity;
		set
		{
			if (!EqualityComparer<decimal>.Default.Equals(_quantity, value))
			{
				OnPropertyChanging(nameof(Quantity));
				_quantity = value < 0m ? 0m : value;
				OnPropertyChanged(nameof(Quantity));
				SyncDiscountFromPercent();
				RaiseComputed();
			}
		}
	}

	public decimal FreeQuantity
	{
		get => _freeQuantity;
		set
		{
			if (!EqualityComparer<decimal>.Default.Equals(_freeQuantity, value))
			{
				OnPropertyChanging(nameof(FreeQuantity));
				_freeQuantity = value < 0m ? 0m : value;
				OnPropertyChanged(nameof(FreeQuantity));
				OnPropertyChanged(nameof(InventoryFreeQuantity));
			}
		}
	}

	/// <summary>Trade rate / PTR per base unit (must not exceed MRP).</summary>
	public decimal UnitPrice
	{
		get => _unitPrice;
		set
		{
			if (!EqualityComparer<decimal>.Default.Equals(_unitPrice, value))
			{
				OnPropertyChanging(nameof(UnitPrice));
				_unitPrice = value < 0m ? 0m : value;
				OnPropertyChanged(nameof(UnitPrice));
				SyncDiscountFromPercent();
				RaiseComputed();
			}
		}
	}

	/// <summary>Discount percent of (Qty × Rate). Drives <see cref="DiscountAmount"/>.</summary>
	public decimal DiscountPercent
	{
		get => _discountPercent;
		set
		{
			decimal clamped = value < 0m ? 0m : (value > 100m ? 100m : value);
			if (!EqualityComparer<decimal>.Default.Equals(_discountPercent, clamped))
			{
				OnPropertyChanging(nameof(DiscountPercent));
				_discountPercent = clamped;
				OnPropertyChanged(nameof(DiscountPercent));
				SyncDiscountFromPercent();
				RaiseComputed();
			}
		}
	}

	public decimal DiscountAmount
	{
		get => _discountAmount;
		set
		{
			if (!EqualityComparer<decimal>.Default.Equals(_discountAmount, value))
			{
				OnPropertyChanging(nameof(DiscountAmount));
				_discountAmount = value < 0m ? 0m : value;
				decimal gross = InventoryQuantity * UnitPrice;
				_discountPercent = gross <= 0m ? 0m : decimal.Round(_discountAmount * 100m / gross, 2, MidpointRounding.AwayFromZero);
				OnPropertyChanged(nameof(DiscountAmount));
				OnPropertyChanged(nameof(DiscountPercent));
				OnPropertyChanged(nameof(LineAmount));
			}
		}
	}

	/// <summary>Sale unit for pack conversion: Unit, Strip, or Box.</summary>
	public string SaleUnit
	{
		get => _saleUnit;
		set
		{
			string normalized = string.IsNullOrWhiteSpace(value) ? "Unit" : value.Trim();
			if (!EqualityComparer<string>.Default.Equals(_saleUnit, normalized))
			{
				OnPropertyChanging(nameof(SaleUnit));
				_saleUnit = normalized;
				OnPropertyChanged(nameof(SaleUnit));
				SyncDiscountFromPercent();
				RaiseComputed();
			}
		}
	}

	public event Action<WholesaleInvoiceLineDraft, decimal>? MrpCommitted;

	public decimal Mrp => StockChoice?.Mrp ?? 0m;

	public bool RequiresMrp => Mrp <= 0m;

	public string MrpText
	{
		get => Mrp.ToString("0.00", CultureInfo.CurrentCulture);
		set
		{
			if (StockChoice == null || !MrpAmount.TryParse(value, out decimal parsed) || parsed < 0m || parsed == Mrp)
			{
				return;
			}

			ApplySavedMrp(parsed);
			MrpCommitted?.Invoke(this, parsed);
		}
	}

	public void ApplySavedMrp(decimal mrp)
	{
		if (_stockChoice == null)
		{
			return;
		}

		_stockChoice = _stockChoice with { Mrp = mrp };
		if (_unitPrice <= 0m)
		{
			_unitPrice = mrp;
			OnPropertyChanged(nameof(UnitPrice));
		}

		OnPropertyChanged(nameof(StockChoice));
		OnPropertyChanged(nameof(Mrp));
		OnPropertyChanged(nameof(RequiresMrp));
		OnPropertyChanged(nameof(MrpText));
		RaiseComputed();
	}

	public string PackLabel => StockChoice?.PackLabel ?? "Unit";

	/// <summary>Base/inventory units = Qty × units-per-sale-unit (strip/box conversion).</summary>
	public decimal InventoryQuantity => Quantity * (StockChoice?.GetUnitsPerSaleUnit(SaleUnit) ?? 1m);

	public decimal InventoryFreeQuantity => FreeQuantity * (StockChoice?.GetUnitsPerSaleUnit(SaleUnit) ?? 1m);

	/// <summary>Taxable line amount before GST: (Rate × base qty) − discount.</summary>
	public decimal LineAmount => decimal.Round(InventoryQuantity * UnitPrice - DiscountAmount, 2, MidpointRounding.AwayFromZero);

	private void OnStockChoiceChanged(WholesaleStockChoice? value)
	{
		if (value != null)
		{
			UnitPrice = value.DefaultWholesaleRate;
		}
	}

	private void SyncDiscountFromPercent()
	{
		decimal gross = InventoryQuantity * UnitPrice;
		decimal amount = gross <= 0m ? 0m : decimal.Round(gross * _discountPercent / 100m, 2, MidpointRounding.AwayFromZero);
		if (!EqualityComparer<decimal>.Default.Equals(_discountAmount, amount))
		{
			_discountAmount = amount;
			OnPropertyChanged(nameof(DiscountAmount));
		}
	}

	private void RaiseComputed()
	{
		OnPropertyChanged(nameof(InventoryQuantity));
		OnPropertyChanged(nameof(InventoryFreeQuantity));
		OnPropertyChanged(nameof(LineAmount));
	}
}
