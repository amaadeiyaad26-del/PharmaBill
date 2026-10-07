using System.Windows;
using System.Windows.Markup;
using PharmaBill.App.Services;

namespace PharmaBill.App;

public partial class PharmacistProfileWindow : Window, IComponentConnector
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
