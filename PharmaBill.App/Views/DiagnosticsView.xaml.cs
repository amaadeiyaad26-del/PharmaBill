using System.Windows;
using System.Windows.Controls;

namespace PharmaBill.App.Views;

public partial class DiagnosticsView : UserControl
{
	public DiagnosticsView()
	{
		InitializeComponent();
	}

	private void OpenKeyGenerator_Click(object sender, RoutedEventArgs e)
	{
		var window = new DeveloperKeyGeneratorWindow
		{
			Owner = Window.GetWindow(this)
		};
		window.ShowDialog();
	}
}
