using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PharmaBill.App.ViewModels;

namespace PharmaBill.App;

public partial class PrescriptionReviewWindow : Window
{
	private readonly PrescriptionReviewViewModel _viewModel;

	public PrescriptionReviewWindow(PrescriptionReviewViewModel viewModel)
	{
		InitializeComponent();
		_viewModel = viewModel;
		viewModel.OwnerWindow = this;
		DataContext = viewModel;
	}

	private void ConfigureGemini_Click(object sender, RoutedEventArgs e)
	{
		_viewModel.OpenSettingsRequested = true;
		DialogResult = false;
	}

	private async void AddToBill_Click(object sender, RoutedEventArgs e)
	{
		if (await _viewModel.TryAcceptAsync())
		{
			DialogResult = true;
		}
	}

	private void Qty_PreviewMouseDown(object sender, MouseButtonEventArgs e)
	{
		if (sender is TextBox box && !box.IsKeyboardFocusWithin)
		{
			box.Focus();
			e.Handled = true;
		}
	}
}
