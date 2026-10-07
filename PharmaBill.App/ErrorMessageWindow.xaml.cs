using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using PharmaBill.App.Services;

namespace PharmaBill.App;

public partial class ErrorMessageWindow : Window
{
	private readonly string _logDirectory;

	private DispatcherTimer? _toastTimer;

	public ErrorMessageWindow(string message, string details, string logDirectory)
	{
		_logDirectory = logDirectory;
		InitializeComponent();
		FriendlyMessage.Text = message;
		ErrorDetails.Text = details;
		DataContext = new
		{
			LogPath = logDirectory
		};
	}

	private void OpenLogFolder_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			Directory.CreateDirectory(_logDirectory);
			Process.Start(new ProcessStartInfo("explorer.exe", _logDirectory)
			{
				UseShellExecute = true
			});
		}
		catch (Exception ex)
		{
			MessageBox.Show("Could not open the log folder: " + ex.Message + Environment.NewLine + _logDirectory, "PharmaBill", MessageBoxButton.OK, MessageBoxImage.Hand);
		}
	}

	private void CopyErrorDetails_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			Clipboard.SetText($"{FriendlyMessage.Text}{Environment.NewLine}{Environment.NewLine}Log folder: {_logDirectory}{Environment.NewLine}{Environment.NewLine}" + ErrorDetails.Text);
			ShowCopiedToast("Error details copied!");
		}
		catch (Exception ex)
		{
			MessageBox.Show("Could not copy error details: " + ex.Message, "PharmaBill", MessageBoxButton.OK, MessageBoxImage.Exclamation);
		}
	}

	private void SupportEmail_Click(object sender, RoutedEventArgs e)
	{
		e.Handled = true;
		DeveloperSupport.ContactAndCopyEmail();
		ShowCopiedToast("Copied to clipboard!");
	}

	private void ShowCopiedToast(string text)
	{
		CopiedToastRun.Text = text;
		_toastTimer?.Stop();
		_toastTimer = new DispatcherTimer
		{
			Interval = TimeSpan.FromSeconds(2.5)
		};
		_toastTimer.Tick += (object? _, EventArgs _) =>
		{
			_toastTimer.Stop();
			CopiedToastRun.Text = string.Empty;
		};
		_toastTimer.Start();
	}
}
