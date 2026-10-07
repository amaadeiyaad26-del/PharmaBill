using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.ComponentModel;

namespace PharmaBill.App.ViewModels;

public sealed class PurchaseReturnLineDraft(BatchReturnOption? batch, decimal quantity) : ObservableObject
{
	private BatchReturnOption? _batch = batch;

	private decimal _quantity = quantity;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public BatchReturnOption? Batch
	{
		get
		{
			return _batch;
		}
		set
		{
			if (!EqualityComparer<BatchReturnOption>.Default.Equals(_batch, value))
			{
				OnPropertyChanging(nameof(Batch));
				_batch = value;
				OnPropertyChanged(nameof(Batch));
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
}
