using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel.__Internals;
using PharmaBill.App.Services;
using PharmaBill.Core;
using PharmaBill.Data.Services;

namespace PharmaBill.App.ViewModels;

public sealed class RetailBillLineViewModel : ObservableObject
{
	[ObservableProperty]
	private decimal _quantity = 1m;

	[ObservableProperty]
	private decimal _discountAmount;

	[ObservableProperty]
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
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.Quantity);
				_quantity = value;
				OnQuantityChanged(value);
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.Quantity);
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
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.DiscountAmount);
				_discountAmount = value;
				OnDiscountAmountChanged(value);
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.DiscountAmount);
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
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.UnitPrice);
				_unitPrice = value;
				OnUnitPriceChanged(value);
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.UnitPrice);
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

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnQuantityChanged(decimal value)
	{
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
		UpdateAmounts();
	}
}
