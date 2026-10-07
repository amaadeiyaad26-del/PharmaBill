using System.Windows;
using PharmaBill.App.ViewModels;

namespace PharmaBill.App;

public partial class PrescriptionReviewWindow : Window
{
	private readonly PrescriptionReviewViewModel _viewModel;

	public PrescriptionReviewWindow(PrescriptionReviewViewModel viewModel)
	{
		InitializeComponent();
		_viewModel = viewModel;
		DataContext = viewModel;
	}

	private void ConfigureGemini_Click(object sender, RoutedEventArgs e)
	{
		_viewModel.OpenSettingsRequested = true;
		DialogResult = false;
	}

	private void AddToBill_Click(object sender, RoutedEventArgs e)
	{
		string text = _viewModel.Validate();
		if (text != null)
		{
			ErrorText.Text = text;
		}
		else
		{
			DialogResult = true;
		}
	}
}
