using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using PharmaBill.App.Services;

namespace PharmaBill.App.Controls;

public partial class DeveloperSupportBanner : UserControl
{
	private DispatcherTimer? _toastTimer;

	public DeveloperSupportBanner()
	{
		InitializeComponent();
	}

	private void EmailLink_Click(object sender, RoutedEventArgs e)
	{
		e.Handled = true;
		ContactDeveloperSupport();
	}

	private void EmailDeveloper_Click(object sender, RoutedEventArgs e)
	{
		ContactDeveloperSupport();
	}

	private void ContactDeveloperSupport()
	{
		DeveloperSupport.ContactAndCopyEmail();
		ShowCopiedToast();
	}

	private void ShowCopiedToast()
	{
		CopiedToast.Visibility = Visibility.Visible;
		_toastTimer?.Stop();
		_toastTimer = new DispatcherTimer
		{
			Interval = TimeSpan.FromSeconds(2.5)
		};
		_toastTimer.Tick += (object? _, EventArgs _) =>
		{
			_toastTimer.Stop();
			CopiedToast.Visibility = Visibility.Collapsed;
		};
		_toastTimer.Start();
	}
}
