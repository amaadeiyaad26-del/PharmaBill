using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.ComponentModel;
using PharmaBill.App.Services;
using PharmaBill.Core;
using PharmaBill.Data.Services;

namespace PharmaBill.App.ViewModels;

public sealed class RetailBillLineViewModel : ObservableObject
{
	private decimal _quantity = 1m;

	private decimal _discountAmount;

	private decimal _discountPercent;

	private decimal _unitPrice;

	public RetailStockChoice Choice { get; }

	public string DrugName => Choice.DrugName;

	public string BatchNo => Choice.BatchNo;

	public DateOnly? ExpiryDate => Choice.ExpiryDate;

	public decimal Mrp => Choice.Mrp;

	public decimal StockQuantity => Choice.AvailableQuantity;

	public decimal GstRate => Choice.GstRate;

	public bool RequiresPrescription
	{
		get
		{
			switch (Choice.Schedule)
			{
			case "H1":
			case "X":
			case "NDPS":
				return true;
			default:
				return false;
			}
		}
	}

	public bool IsHabitForming => Choice.IsHabitForming;

	public string? Schedule => Choice.Schedule;

	public decimal GrossAmount => TaxSplit.GrossAmount;

	public decimal TaxAmount => TaxSplit.TaxAmount;

	public decimal NetAmount => GrossAmount - TaxAmount;

	private InclusiveTaxLine TaxSplit
	{
		get
		{
			if (!(Quantity <= 0m) && !(UnitPrice < 0m) && !(GstRate < 0m) && !(DiscountAmount < 0m) && !(DiscountAmount > Quantity * UnitPrice))
			{
				return RetailTaxCalculator.SplitInclusive(Quantity, UnitPrice, GstRate, DiscountAmount);
			}
			return default;
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public decimal Quantity
	{
		get
		{
			return _quantity;
		}
		set
		{
			if (!EqualityComparer<decimal>.Default.Equals(_quantity, value))
			{
				OnPropertyChanging(nameof(Quantity));
				_quantity = value;
				OnQuantityChanged(value);
				OnPropertyChanged(nameof(Quantity));
			}
		}
	}

	/// <summary>Discount percent of (Qty × Rate). Drives <see cref="DiscountAmount"/>.</summary>
	public decimal DiscountPercent
	{
		get
		{
			return _discountPercent;
		}
		set
		{
			decimal clamped = value < 0m ? 0m : (value > 100m ? 100m : value);
			if (!EqualityComparer<decimal>.Default.Equals(_discountPercent, clamped))
			{
				OnPropertyChanging(nameof(DiscountPercent));
				_discountPercent = clamped;
				SyncDiscountFromPercent();
				OnDiscountAmountChanged(_discountAmount);
				OnPropertyChanged(nameof(DiscountPercent));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public decimal DiscountAmount
	{
		get
		{
			return _discountAmount;
		}
		set
		{
			if (!EqualityComparer<decimal>.Default.Equals(_discountAmount, value))
			{
				OnPropertyChanging(nameof(DiscountAmount));
				_discountAmount = value < 0m ? 0m : value;
				decimal gross = Quantity * UnitPrice;
				_discountPercent = gross <= 0m ? 0m : decimal.Round(_discountAmount * 100m / gross, 2, MidpointRounding.AwayFromZero);
				OnDiscountAmountChanged(value);
				OnPropertyChanged(nameof(DiscountAmount));
				OnPropertyChanged(nameof(DiscountPercent));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public decimal UnitPrice
	{
		get
		{
			return _unitPrice;
		}
		set
		{
			if (!EqualityComparer<decimal>.Default.Equals(_unitPrice, value))
			{
				OnPropertyChanging(nameof(UnitPrice));
				_unitPrice = value;
				OnUnitPriceChanged(value);
				OnPropertyChanged(nameof(UnitPrice));
			}
		}
	}

	public RetailBillLineViewModel(RetailStockChoice choice)
	{
		Choice = choice;
		UnitPrice = ((choice.SalePrice.HasValue && choice.SalePrice.Value >= 0m && choice.SalePrice.Value <= choice.Mrp) ? choice.SalePrice.Value : choice.Mrp);
	}

	private RetailBillLineViewModel(RetailBillLineSnapshot snapshot)
	{
		Choice = snapshot.Choice;
		Quantity = snapshot.Quantity;
		DiscountAmount = snapshot.DiscountAmount;
		UnitPrice = snapshot.UnitPrice;
	}

	public string? Validate()
	{
		if (Quantity <= 0m)
		{
			return "Quantity must be greater than zero.";
		}
		if (Quantity > Choice.AvailableQuantity)
		{
			return $"Quantity for {DrugName} exceeds live stock ({Choice.AvailableQuantity}).";
		}
		if (Choice.ExpiryDate.HasValue && Choice.ExpiryDate.Value < DateOnly.FromDateTime(DateTime.Today))
		{
			return "Batch " + BatchNo + " is expired.";
		}
		if (UnitPrice < 0m || UnitPrice > Mrp)
		{
			return $"Price for {DrugName} cannot exceed MRP {MoneyFormat.Rupees(Mrp)}.";
		}
		if (Mrp <= 0m)
		{
			return "A valid MRP is required for " + DrugName + ".";
		}
		if (DiscountAmount < 0m || DiscountAmount > Quantity * UnitPrice)
		{
			return "Discount cannot be negative or exceed the line amount.";
		}
		return null;
	}

	public RetailSaleLineInput ToInput()
	{
		return new RetailSaleLineInput(Choice.DrugId, Choice.BatchId, Quantity, DiscountAmount, UnitPrice);
	}

	public RetailBillLineSnapshot ToSnapshot()
	{
		return new RetailBillLineSnapshot(Choice, Quantity, DiscountAmount, UnitPrice);
	}

	public static RetailBillLineViewModel FromSnapshot(RetailBillLineSnapshot snapshot)
	{
		return new RetailBillLineViewModel(snapshot);
	}

	private void UpdateAmounts()
	{
		OnPropertyChanged("GrossAmount");
		OnPropertyChanged("TaxAmount");
		OnPropertyChanged("NetAmount");
	}

	private void SyncDiscountFromPercent()
	{
		decimal gross = Quantity * UnitPrice;
		_discountAmount = gross <= 0m ? 0m : decimal.Round(gross * _discountPercent / 100m, 2, MidpointRounding.AwayFromZero);
		OnPropertyChanged(nameof(DiscountAmount));
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnQuantityChanged(decimal value)
	{
		SyncDiscountFromPercent();
		UpdateAmounts();
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnDiscountAmountChanged(decimal value)
	{
		UpdateAmounts();
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnUnitPriceChanged(decimal value)
	{
		SyncDiscountFromPercent();
		UpdateAmounts();
	}
}
