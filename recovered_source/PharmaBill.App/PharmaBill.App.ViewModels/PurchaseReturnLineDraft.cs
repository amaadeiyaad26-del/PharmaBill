using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel.__Internals;

namespace PharmaBill.App.ViewModels;

public sealed class PurchaseReturnLineDraft(BatchReturnOption? batch, decimal quantity) : ObservableObject
{
	[ObservableProperty]
	private BatchReturnOption? _batch = batch;

	[ObservableProperty]
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
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.Batch);
				_batch = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.Batch);
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
}
