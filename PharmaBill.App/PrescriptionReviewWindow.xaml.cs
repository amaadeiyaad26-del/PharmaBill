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
		viewModel.FocusCounterExpiry = FocusCounterExpiry;
		DataContext = viewModel;
	}

	/// <summary>Puts the cursor in that row's expiry box, text selected, so the date is typed straight away.</summary>
	private void FocusCounterExpiry(PrescriptionReviewRow row)
	{
		Dispatcher.BeginInvoke(() =>
		{
			TextBox? box = FindExpiryBox(this, row);
			if (box != null)
			{
				box.Focus();
				Keyboard.Focus(box);
				box.SelectAll();
			}
		}, System.Windows.Threading.DispatcherPriority.Input);
	}

	internal static TextBox? FindExpiryBox(DependencyObject root, PrescriptionReviewRow row)
	{
		for (int index = 0; index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); index++)
		{
			DependencyObject child = System.Windows.Media.VisualTreeHelper.GetChild(root, index);
			if (child is TextBox { Name: "CounterExpiryBox" } box && box.DataContext == row)
			{
				return box;
			}
			if (FindExpiryBox(child, row) is TextBox found)
			{
				return found;
			}
		}
		return null;
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
