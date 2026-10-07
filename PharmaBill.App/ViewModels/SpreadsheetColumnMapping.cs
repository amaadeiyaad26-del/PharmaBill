using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.ComponentModel;

namespace PharmaBill.App.ViewModels;

public sealed class SpreadsheetColumnMapping(string target, bool isRequired, ObservableCollection<string> availableHeaders) : ObservableObject
{
	private ObservableCollection<string> _availableHeaders = availableHeaders;

	private string _sourceHeader = string.Empty;

	public string Target { get; } = target;

	public string Label
	{
		get
		{
			if (!(Target == "GST"))
			{
				return Target;
			}
			return "GST %";
		}
	}

	public bool IsRequired { get; } = isRequired;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public ObservableCollection<string> AvailableHeaders
	{
		get
		{
			return _availableHeaders;
		}
		[MemberNotNull("_availableHeaders")]
		set
		{
			if (!EqualityComparer<ObservableCollection<string>>.Default.Equals(_availableHeaders, value))
			{
				OnPropertyChanging(nameof(AvailableHeaders));
				_availableHeaders = value;
				OnPropertyChanged(nameof(AvailableHeaders));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string SourceHeader
	{
		get
		{
			return _sourceHeader;
		}
		[MemberNotNull("_sourceHeader")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_sourceHeader, value))
			{
				OnPropertyChanging(nameof(SourceHeader));
				_sourceHeader = value;
				OnPropertyChanged(nameof(SourceHeader));
			}
		}
	}
}
