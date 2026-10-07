using System.Windows;
using PharmaBill.App.Services;

namespace PharmaBill.App;

public partial class PharmacistProfileWindow : Window
{
	public PharmacistProfileWindow(ProfileSummary summary)
	{
		InitializeComponent();
		DataContext = summary;
	}

	private void Lock_Click(object sender, RoutedEventArgs e)
	{
		DialogResult = true;
	}
}
