using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.ComponentModel;

namespace PharmaBill.App.ViewModels;

public sealed class StockVerificationLineViewModel(Guid batchId, string display, decimal expectedQuantity, decimal countedQuantity) : ObservableObject
{
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
				OnPropertyChanging(nameof(CountedQuantity));
				OnPropertyChanging(nameof(Difference));
				_countedQuantity = value;
				OnPropertyChanged(nameof(CountedQuantity));
				OnPropertyChanged(nameof(Difference));
			}
		}
	}
}
