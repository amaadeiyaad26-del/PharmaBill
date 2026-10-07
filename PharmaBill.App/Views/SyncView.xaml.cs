using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace PharmaBill.App.Views;

public partial class SyncView : UserControl
{
	private DispatcherTimer? _toastTimer;

	public SyncView()
	{
		InitializeComponent();
	}

	private void OpenPlayStore_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			Clipboard.SetText("https://play.google.com/store/search?q=PharmaBill%20SAER&c=apps");
			ShowCopiedToast();
		}
		catch
		{
		}
		try
		{
			Process.Start(new ProcessStartInfo("https://play.google.com/store/search?q=PharmaBill%20SAER&c=apps")
			{
				UseShellExecute = true
			});
		}
		catch (Exception ex)
		{
			MessageBox.Show($"Could not open Google Play Store:{Environment.NewLine}{ex.Message}{Environment.NewLine}{Environment.NewLine}" + "https://play.google.com/store/search?q=PharmaBill%20SAER&c=apps", "PharmaBill", MessageBoxButton.OK, MessageBoxImage.Asterisk);
		}
	}

	private void ShowCopiedToast()
	{
		PlayStoreCopiedToast.Visibility = Visibility.Visible;
		_toastTimer?.Stop();
		_toastTimer = new DispatcherTimer
		{
			Interval = TimeSpan.FromSeconds(2.5)
		};
		_toastTimer.Tick += (object? _, EventArgs _) =>
		{
			_toastTimer.Stop();
			PlayStoreCopiedToast.Visibility = Visibility.Collapsed;
		};
		_toastTimer.Start();
	}

}
