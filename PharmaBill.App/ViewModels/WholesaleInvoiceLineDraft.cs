using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.ComponentModel;

namespace PharmaBill.App.ViewModels;

public class WholesaleInvoiceLineDraft : ObservableObject
{
	private WholesaleStockChoice? _stockChoice;

	private decimal _quantity = 1m;

	private decimal _freeQuantity;

	private decimal _unitPrice;

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
				OnPropertyChanging(nameof(StockChoice));
				_stockChoice = value;
				OnStockChoiceChanged(value);
				OnPropertyChanged(nameof(StockChoice));
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
				OnPropertyChanging(nameof(Quantity));
				_quantity = value;
				OnPropertyChanged(nameof(Quantity));
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
				OnPropertyChanging(nameof(FreeQuantity));
				_freeQuantity = value;
				OnPropertyChanged(nameof(FreeQuantity));
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
				OnPropertyChanged(nameof(UnitPrice));
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
				_discountAmount = value;
				OnPropertyChanged(nameof(DiscountAmount));
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
