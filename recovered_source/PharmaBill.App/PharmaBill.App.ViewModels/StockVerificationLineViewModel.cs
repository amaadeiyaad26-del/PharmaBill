using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel.__Internals;

namespace PharmaBill.App.ViewModels;

public sealed class StockVerificationLineViewModel(Guid batchId, string display, decimal expectedQuantity, decimal countedQuantity) : ObservableObject
{
	[ObservableProperty]
	[NotifyPropertyChangedFor("Difference")]
	private decimal _countedQuantity = countedQuantity;

	public Guid BatchId { get; } = batchId;

	public string Display { get; } = display;

	public decimal ExpectedQuantity { get; } = expectedQuantity;

	public decimal Difference => CountedQuantity - ExpectedQuantity;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public decimal CountedQuantity
	{
		get
		{
			return _countedQuantity;
		}
		set
		{
			if (!EqualityComparer<decimal>.Default.Equals(_countedQuantity, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.CountedQuantity);
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.Difference);
				_countedQuantity = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.CountedQuantity);
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.Difference);
			}
		}
	}
}
