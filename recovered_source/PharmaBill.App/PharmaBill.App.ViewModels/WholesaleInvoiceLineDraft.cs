using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel.__Internals;

namespace PharmaBill.App.ViewModels;

public class WholesaleInvoiceLineDraft : ObservableObject
{
	[ObservableProperty]
	private WholesaleStockChoice? _stockChoice;

	[ObservableProperty]
	private decimal _quantity = 1m;

	[ObservableProperty]
	private decimal _freeQuantity;

	[ObservableProperty]
	private decimal _unitPrice;

	[ObservableProperty]
	private decimal _discountAmount;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public WholesaleStockChoice? StockChoice
	{
		get
		{
			return _stockChoice;
		}
		set
		{
			if (!EqualityComparer<WholesaleStockChoice>.Default.Equals(_stockChoice, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.StockChoice);
				_stockChoice = value;
				OnStockChoiceChanged(value);
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.StockChoice);
			}
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
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.Quantity);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public decimal FreeQuantity
	{
		get
		{
			return _freeQuantity;
		}
		set
		{
			if (!EqualityComparer<decimal>.Default.Equals(_freeQuantity, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.FreeQuantity);
				_freeQuantity = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.FreeQuantity);
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
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.UnitPrice);
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
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.DiscountAmount);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnStockChoiceChanged(WholesaleStockChoice? value)
	{
		if ((object)value != null)
		{
			UnitPrice = value.Mrp;
		}
	}
}
