using System.Windows;
using System.Windows.Controls;
using PharmaBill.App.ViewModels;

namespace PharmaBill.App.Controls;

public partial class UpdateIndicator : UserControl
{
	public UpdateIndicator()
	{
		InitializeComponent();
	}

	private void Close_Click(object sender, RoutedEventArgs e)
	{
		if (IndicatorButton.DataContext is MainWindowViewModel viewModel)
		{
			viewModel.IsUpdateDetailsOpen = false;
		}
	}
}
