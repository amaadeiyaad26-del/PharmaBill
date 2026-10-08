using System;
using System.Windows;
using System.Windows.Controls;
using PharmaBill.App.Licensing;

namespace PharmaBill.App;

public partial class DeveloperKeyGeneratorWindow : Window
{
	public DeveloperKeyGeneratorWindow()
	{
		InitializeComponent();
		DurationBox.SelectedIndex = 1;
		MachineIdBox.Focus();

		if (!AdminKeyGenerator.CanSign)
		{
			StatusText.Foreground = System.Windows.Media.Brushes.Firebrick;
			StatusText.Text = "This client build cannot sign keys (private key not compiled in). Use Debug/MASTER_ADMIN_BUILD or tools/licensing.";
		}
	}

	private void Generate_Click(object sender, RoutedEventArgs e)
	{
		string machineId = MachineIdBox.Text?.Trim() ?? string.Empty;
		if (string.IsNullOrWhiteSpace(machineId))
		{
			StatusText.Foreground = System.Windows.Media.Brushes.Firebrick;
			StatusText.Text = "Enter the customer Machine ID (e.g. PB-MID-XXXXXXXXXXXX).";
			GeneratedKeyBox.Text = string.Empty;
			ExpiryHintText.Text = string.Empty;
			return;
		}

		if (!AdminKeyGenerator.CanSign)
		{
			StatusText.Foreground = System.Windows.Media.Brushes.Firebrick;
			StatusText.Text = "Admin signing unavailable in this build. Private key must not ship with client releases.";
			return;
		}

		try
		{
			LicenseManager.LicenseDuration duration = ResolveDuration();
			DateTime asOf = DateTime.UtcNow;
			string key = AdminKeyGenerator.GenerateKey(machineId, duration, asOf);
			GeneratedKeyBox.Text = key;

			ExpiryHintText.Text = duration switch
			{
				LicenseManager.LicenseDuration.OneMonth =>
					$"RSA-signed 30-day grant. Nominal end {asOf.AddDays(30):dd-MMM-yyyy} UTC.",
				LicenseManager.LicenseDuration.Lifetime =>
					"RSA-signed lifetime licence (PBILL2 payload with LIFETIME).",
				_ =>
					$"RSA-signed 365-day annual grant. Nominal end {asOf.AddDays(LicenseManager.AnnualGrantDays):dd-MMM-yyyy} UTC. Renewals extend ValidUntil by 365 days."
			};

			if (!LicenseManager.ValidateLicense(machineId, key, out string message))
			{
				StatusText.Foreground = System.Windows.Media.Brushes.Firebrick;
				StatusText.Text = "Generated key failed public-key self-check: " + message;
				return;
			}

			StatusText.Foreground = System.Windows.Media.Brushes.SeaGreen;
			StatusText.Text = message;
		}
		catch (Exception ex)
		{
			StatusText.Foreground = System.Windows.Media.Brushes.Firebrick;
			StatusText.Text = "Could not generate key: " + ex.Message;
		}
	}

	private LicenseManager.LicenseDuration ResolveDuration()
	{
		if (DurationBox.SelectedItem is ComboBoxItem { Tag: string tag })
		{
			return tag switch
			{
				"OneMonth" => LicenseManager.LicenseDuration.OneMonth,
				"Lifetime" => LicenseManager.LicenseDuration.Lifetime,
				_ => LicenseManager.LicenseDuration.OneYear
			};
		}

		return LicenseManager.LicenseDuration.OneYear;
	}

	private void CopyKey_Click(object sender, RoutedEventArgs e)
	{
		string key = GeneratedKeyBox.Text?.Trim() ?? string.Empty;
		if (string.IsNullOrWhiteSpace(key))
		{
			StatusText.Foreground = System.Windows.Media.Brushes.Firebrick;
			StatusText.Text = "Generate a key before copying.";
			return;
		}

		try
		{
			Clipboard.SetText(key);
			StatusText.Foreground = System.Windows.Media.Brushes.SeaGreen;
			StatusText.Text = "Key copied to clipboard.";
		}
		catch
		{
			StatusText.Foreground = System.Windows.Media.Brushes.Firebrick;
			StatusText.Text = "Could not copy key.";
		}
	}
}