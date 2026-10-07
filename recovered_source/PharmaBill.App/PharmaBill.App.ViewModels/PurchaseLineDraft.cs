using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel.__Internals;
using PharmaBill.Core.Entities;
using PharmaBill.Data.Services;

namespace PharmaBill.App.ViewModels;

public sealed class PurchaseLineDraft(Drug? drug) : ObservableObject
{
	[ObservableProperty]
	private Drug? _drug = drug;

	[ObservableProperty]
	private string _batchNo = string.Empty;

	[ObservableProperty]
	private string _expiry = string.Empty;

	[ObservableProperty]
	private decimal _quantity;

	[ObservableProperty]
	private decimal _freeQuantity;

	[ObservableProperty]
	private decimal _mrp;

	[ObservableProperty]
	private decimal _rate;

	[ObservableProperty]
	private decimal _gstRate;

	[ObservableProperty]
	private decimal _discountAmount;

	[ObservableProperty]
	private string? _rack;

	public decimal BaseAmount => Round(Quantity * Rate - DiscountAmount);

	public decimal TaxAmount => Round(BaseAmount * GstRate / 100m);

	public decimal Amount => BaseAmount;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public Drug? Drug
	{
		get
		{
			return _drug;
		}
		set
		{
			if (!EqualityComparer<PharmaBill.Core.Entities.Drug>.Default.Equals(_drug, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.Drug);
				_drug = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.Drug);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string BatchNo
	{
		get
		{
			return _batchNo;
		}
		[MemberNotNull("_batchNo")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_batchNo, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.BatchNo);
				_batchNo = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.BatchNo);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string Expiry
	{
		get
		{
			return _expiry;
		}
		[MemberNotNull("_expiry")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_expiry, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.Expiry);
				_expiry = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.Expiry);
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
				OnQuantityChanged(value);
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
	public decimal Mrp
	{
		get
		{
			return _mrp;
		}
		set
		{
			if (!EqualityComparer<decimal>.Default.Equals(_mrp, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.Mrp);
				_mrp = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.Mrp);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public decimal Rate
	{
		get
		{
			return _rate;
		}
		set
		{
			if (!EqualityComparer<decimal>.Default.Equals(_rate, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.Rate);
				_rate = value;
				OnRateChanged(value);
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.Rate);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public decimal GstRate
	{
		get
		{
			return _gstRate;
		}
		set
		{
			if (!EqualityComparer<decimal>.Default.Equals(_gstRate, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.GstRate);
				_gstRate = value;
				OnGstRateChanged(value);
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.GstRate);
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
	public string? Rack
	{
		get
		{
			return _rack;
		}
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_rack, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.Rack);
				_rack = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.Rack);
			}
		}
	}

	public bool TryCreateInput(out PurchaseLineInput input)
	{
		input = null;
		if (Drug == null || !DateOnly.TryParse(Expiry, CultureInfo.CurrentCulture, DateTimeStyles.None, out var result))
		{
			return false;
		}
		input = new PurchaseLineInput(Drug.Id, BatchNo, result, Quantity, FreeQuantity, Mrp, Rate, GstRate, DiscountAmount, Amount, Rack);
		return true;
	}

	private void NotifyAmounts()
	{
		OnPropertyChanged("BaseAmount");
		OnPropertyChanged("TaxAmount");
		OnPropertyChanged("Amount");
	}

	private static decimal Round(decimal value)
	{
		return decimal.Round(value, 2, MidpointRounding.AwayFromZero);
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnQuantityChanged(decimal value)
	{
		NotifyAmounts();
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnRateChanged(decimal value)
	{
		NotifyAmounts();
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnGstRateChanged(decimal value)
	{
		NotifyAmounts();
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnDiscountAmountChanged(decimal value)
	{
		NotifyAmounts();
	}
}
