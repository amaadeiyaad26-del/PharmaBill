using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel.__Internals;

namespace PharmaBill.App.ViewModels;

public class WholesaleCreditLineDraft : ObservableObject
{
	[ObservableProperty]
	private decimal _quantity;

	[ObservableProperty]
	private bool _restock;

	public Guid InvoiceItemId { get; init; }

	public Guid BatchId { get; init; }

	public Guid DrugId { get; init; }

	public string DrugName { get; init; } = string.Empty;

	public string BatchNo { get; init; } = string.Empty;

	public decimal Remaining { get; init; }

	public decimal UnitCredit { get; init; }

	public bool Quarantine => !Restock;

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
	public bool Restock
	{
		get
		{
			return _restock;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_restock, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.Restock);
				_restock = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.Restock);
			}
		}
	}
}
