using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel.__Internals;

namespace PharmaBill.App.ViewModels;

public sealed class StockTransferLineDraft : ObservableObject
{
	[ObservableProperty]
	private decimal _transferQuantity;

	public Guid BatchId { get; init; }

	public string DrugName { get; init; } = string.Empty;

	public string BatchNo { get; init; } = string.Empty;

	public DateOnly? ExpiryDate { get; init; }

	public decimal AvailableQuantity { get; init; }

	public string? Rack { get; init; }

	public string Display => $"{DrugName} / {BatchNo} / Avail {AvailableQuantity:0.##}";

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public decimal TransferQuantity
	{
		get
		{
			return _transferQuantity;
		}
		set
		{
			if (!EqualityComparer<decimal>.Default.Equals(_transferQuantity, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.TransferQuantity);
				_transferQuantity = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.TransferQuantity);
			}
		}
	}
}
