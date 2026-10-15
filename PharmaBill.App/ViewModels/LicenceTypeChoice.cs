using CommunityToolkit.Mvvm.ComponentModel;

namespace PharmaBill.App.ViewModels;

/// <summary>One tick-box in the buyer-type licence rule editor.</summary>
public sealed class LicenceTypeChoice(string type, bool isSelected) : ObservableObject
{
	private bool _isSelected = isSelected;

	public string Type { get; } = type;

	public bool IsSelected
	{
		get => _isSelected;
		set => SetProperty(ref _isSelected, value);
	}
}
