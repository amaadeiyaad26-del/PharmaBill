using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.ComponentModel;

namespace PharmaBill.App.ViewModels;

public class WholesaleCreditLineDraft : ObservableObject
{
	private decimal _quantity;

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
				OnPropertyChanging(nameof(Quantity));
				_quantity = value;
				OnPropertyChanged(nameof(Quantity));
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
				OnPropertyChanging(nameof(Restock));
				_restock = value;
				OnPropertyChanged(nameof(Restock));
			}
		}
	}
}
