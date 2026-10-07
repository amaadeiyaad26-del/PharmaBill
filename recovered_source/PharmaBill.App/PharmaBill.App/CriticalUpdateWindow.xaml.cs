using System.Windows;
using System.Windows.Markup;

namespace PharmaBill.App;

public partial class CriticalUpdateWindow : Window, IComponentConnector
{
	public bool UpdateRequested { get; private set; }

	public CriticalUpdateWindow(string message)
	{
		InitializeComponent();
		MessageText.Text = message;
	}

	private void UpdateNow_Click(object sender, RoutedEventArgs e)
	{
		UpdateRequested = true;
		DialogResult = true;
		Close();
	}

	private void Exit_Click(object sender, RoutedEventArgs e)
	{
		UpdateRequested = false;
		DialogResult = false;
		Close();
	}
}
