using System.Windows;

namespace PharmaBill.App;

public partial class ActivationWindow : Window
{
	public ActivationWindow()
	{
		InitializeComponent();
		MachineIdBox.Text = LicenseManager.GetMachineId();
		CurrentStatusText.Text = LicenseManager.GetStatusSummary();
		LicenseKeyBox.Focus();
	}

	private void CopyMachineId_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			Clipboard.SetText(MachineIdBox.Text);
			StatusText.Foreground = System.Windows.Media.Brushes.SeaGreen;
			StatusText.Text = "Machine ID copied to clipboard.";
		}
		catch
		{
			StatusText.Foreground = System.Windows.Media.Brushes.Firebrick;
			StatusText.Text = "Could not copy Machine ID.";
		}
	}

	private void ContinueReadOnly_Click(object sender, RoutedEventArgs e)
	{
		DialogResult = false;
		Close();
	}

	private void Activate_Click(object sender, RoutedEventArgs e)
	{
		string key = LicenseKeyBox.Text?.Trim() ?? string.Empty;

		if (!LicenseManager.ActivateLicense(key, out string message))
		{
			StatusText.Foreground = System.Windows.Media.Brushes.Firebrick;
			StatusText.Text = message;
			return;
		}

		CurrentStatusText.Text = LicenseManager.GetStatusSummary();
		StatusText.Foreground = System.Windows.Media.Brushes.SeaGreen;
		StatusText.Text = message;
		DialogResult = true;
		Close();
	}
}
