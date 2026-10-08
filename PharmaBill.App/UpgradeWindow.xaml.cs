using System;
using System.Diagnostics;
using System.Reflection;
using System.Windows;

namespace PharmaBill.App;

public partial class UpgradeWindow : Window
{
	public UpgradeWindow()
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

	private void SendEmailRequest_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			string machineId = MachineIdBox.Text?.Trim() ?? LicenseManager.GetMachineId();
			string version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.1.0";
			string subject = Uri.EscapeDataString("PharmaBill Annual Subscription Request (₹2,500/yr)");
			string body = Uri.EscapeDataString(
				"Hello PharmaBill Team,\r\n" +
				"\r\n" +
				"I would like to activate / renew the Annual Subscription plan (₹2,500/year).\r\n" +
				$"Machine ID: {machineId}\r\n" +
				$"App Version: {version}");

			Process.Start(new ProcessStartInfo($"mailto:pharma.bill26@gmail.com?subject={subject}&body={body}")
			{
				UseShellExecute = true
			});

			StatusText.Foreground = System.Windows.Media.Brushes.SeaGreen;
			StatusText.Text = "Email draft opened. Send it, then paste your renewal key below when you receive it.";
		}
		catch (Exception ex)
		{
			StatusText.Foreground = System.Windows.Media.Brushes.Firebrick;
			StatusText.Text = "Could not open email client: " + ex.Message;
		}
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

	private void Close_Click(object sender, RoutedEventArgs e)
	{
		DialogResult = false;
		Close();
	}
}
