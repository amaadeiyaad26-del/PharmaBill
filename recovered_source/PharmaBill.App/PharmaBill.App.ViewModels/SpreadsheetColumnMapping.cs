using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel.__Internals;

namespace PharmaBill.App.ViewModels;

public sealed class SpreadsheetColumnMapping(string target, bool isRequired, ObservableCollection<string> availableHeaders) : ObservableObject
{
	[ObservableProperty]
	private ObservableCollection<string> _availableHeaders = availableHeaders;

	[ObservableProperty]
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
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.AvailableHeaders);
				_availableHeaders = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.AvailableHeaders);
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
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.SourceHeader);
				_sourceHeader = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.SourceHeader);
			}
		}
	}
}
