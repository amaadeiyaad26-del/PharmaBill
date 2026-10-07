using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.ComponentModel;
using PharmaBill.Data.Services;

namespace PharmaBill.App.ViewModels;

public sealed class DrugRecordsDrugChoiceRow(DrugRecordsDrugChoice drug) : ObservableObject
{
	private bool _isSelected;

	public Guid DrugId => drug.DrugId;

	public string Name => drug.Name;

	public string? Composition => drug.Composition;

	public string? BrandName => drug.BrandName;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool IsSelected
	{
		get
		{
			return _isSelected;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_isSelected, value))
			{
				OnPropertyChanging(nameof(IsSelected));
				_isSelected = value;
				OnPropertyChanged(nameof(IsSelected));
			}
		}
	}
}
