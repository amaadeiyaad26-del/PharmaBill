using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using PharmaBill.Core.Entities;
using PharmaBill.Data.Services;

namespace PharmaBill.App.ViewModels;

public sealed class PurchaseLineDraft(Drug? drug) : ObservableObject
{
	private Drug? _drug = drug;

	private string _medicineName = drug?.Name?.Trim() ?? string.Empty;

	private string _batchNo = string.Empty;

	private string _expiry = string.Empty;

	private decimal _quantity;

	private decimal _freeQuantity;

	private decimal _mrp;

	private decimal _rate;

	private decimal _gstRate;

	private decimal _discountAmount;

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
				OnPropertyChanging(nameof(Drug));
				_drug = value;
				_medicineName = value?.Name?.Trim() ?? string.Empty;
				OnPropertyChanged(nameof(Drug));
				OnPropertyChanged(nameof(MedicineName));
			}
		}
	}

	/// <summary>Flattened display name for the purchases grid (ComboBox column cannot reliably show Drug.Name).</summary>
	public string MedicineName
	{
		get
		{
			if (!string.IsNullOrWhiteSpace(_medicineName))
			{
				return _medicineName;
			}

			return Drug?.Name?.Trim() ?? string.Empty;
		}
		set
		{
			string next = value?.Trim() ?? string.Empty;
			if (!EqualityComparer<string>.Default.Equals(_medicineName, next))
			{
				OnPropertyChanging(nameof(MedicineName));
				_medicineName = next;
				OnPropertyChanged(nameof(MedicineName));
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
				OnPropertyChanging(nameof(BatchNo));
				_batchNo = value;
				OnPropertyChanged(nameof(BatchNo));
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
				OnPropertyChanging(nameof(Expiry));
				_expiry = value;
				OnPropertyChanged(nameof(Expiry));
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
				OnQuantityChanged(value);
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
				OnPropertyChanging(nameof(Mrp));
				_mrp = value;
				OnPropertyChanged(nameof(Mrp));
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
				OnPropertyChanging(nameof(Rate));
				_rate = value;
				OnRateChanged(value);
				OnPropertyChanged(nameof(Rate));
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
				OnPropertyChanging(nameof(GstRate));
				_gstRate = value;
				OnGstRateChanged(value);
				OnPropertyChanged(nameof(GstRate));
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
				OnDiscountAmountChanged(value);
				OnPropertyChanged(nameof(DiscountAmount));
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
				OnPropertyChanging(nameof(Rack));
				_rack = value;
				OnPropertyChanged(nameof(Rack));
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
