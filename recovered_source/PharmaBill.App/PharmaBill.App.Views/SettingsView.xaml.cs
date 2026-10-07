using System;
using System.CodeDom.Compiler;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Navigation;
using System.Windows.Threading;
using PharmaBill.App.ViewModels;

namespace PharmaBill.App.Views;

public partial class SettingsView : UserControl, IComponentConnector
{
	private SettingsPageViewModel? _boundSettings;

	public SettingsView()
	{
		InitializeComponent();
		DataContextChanged += OnDataContextChanged;
		Unloaded += (object _, RoutedEventArgs _) =>
		{
			DetachSettings();
		};
	}

	private void SettingsView_OnLoaded(object sender, RoutedEventArgs e)
	{
		AttachSettings(DataContext as SettingsPageViewModel);
	}

	private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
	{
		AttachSettings(e.NewValue as SettingsPageViewModel);
	}

	private void AttachSettings(SettingsPageViewModel? settings)
	{
		if (_boundSettings == settings)
		{
			TryRevealPendingTargets();
			return;
		}
		DetachSettings();
		_boundSettings = settings;
		if (_boundSettings != null)
		{
			_boundSettings.PropertyChanged += OnSettingsPropertyChanged;
			TryRevealPendingTargets();
		}
	}

	private void TryRevealPendingTargets()
	{
		if (_boundSettings != null)
		{
			if (!string.IsNullOrEmpty(_boundSettings.UpiCardRevealToken))
			{
				RevealUpiCard();
			}
			if (!string.IsNullOrEmpty(_boundSettings.FirmEmailRevealToken))
			{
				RevealFirmEmail();
			}
		}
	}

	private void RevealUpiCard()
	{
		Dispatcher.BeginInvoke((Action)(() =>
		{
			UpiDigitalPaymentsCard.BringIntoView();
			StoreUpiIdBox.Focus();
			StoreUpiIdBox.CaretIndex = StoreUpiIdBox.Text.Length;
		}), DispatcherPriority.Loaded);
	}

	private void RevealFirmEmail()
	{
		Dispatcher.BeginInvoke((Action)(() =>
		{
			FirmProfileCard.BringIntoView();
			PharmacyEmailBox.Focus();
			PharmacyEmailBox.CaretIndex = PharmacyEmailBox.Text.Length;
		}), DispatcherPriority.Loaded);
	}

	private void DetachSettings()
	{
		if (_boundSettings != null)
		{
			_boundSettings.PropertyChanged -= OnSettingsPropertyChanged;
			_boundSettings = null;
		}
	}

	private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName == "SettingsSearchFocusToken")
		{
			Dispatcher.BeginInvoke((Action)(() =>
			{
				SettingsSearchBox.Focus();
				SettingsSearchBox.SelectAll();
			}));
		}
		else if (e.PropertyName == "UpiCardRevealToken")
		{
			RevealUpiCard();
		}
		else if (e.PropertyName == "FirmEmailRevealToken")
		{
			RevealFirmEmail();
		}
	}

	private void JumpToPharmacyEmail_Click(object sender, RoutedEventArgs e)
	{
		if (DataContext is SettingsPageViewModel settingsPageViewModel)
		{
			settingsPageViewModel.RevealFirmEmail();
		}
	}

	private void SmtpPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
	{
		if (sender is PasswordBox passwordBox && DataContext is SettingsPageViewModel settingsPageViewModel)
		{
			settingsPageViewModel.SmtpPassword = passwordBox.Password;
		}
	}

	private void SaveDocumentOutputSettings_Click(object sender, RoutedEventArgs e)
	{
		if (DataContext is SettingsPageViewModel settingsPageViewModel)
		{
			settingsPageViewModel.SaveDocumentOutputSettingsCommand.Execute(null);
			SmtpPasswordBox?.Clear();
		}
	}

	private void InspectorPinBox_PasswordChanged(object sender, RoutedEventArgs e)
	{
		if (sender is PasswordBox passwordBox && DataContext is SettingsPageViewModel settingsPageViewModel)
		{
			settingsPageViewModel.InspectorPinInput = passwordBox.Password;
		}
	}

	private void InspectorPinConfirmationBox_PasswordChanged(object sender, RoutedEventArgs e)
	{
		if (sender is PasswordBox passwordBox && DataContext is SettingsPageViewModel settingsPageViewModel)
		{
			settingsPageViewModel.InspectorPinConfirmation = passwordBox.Password;
		}
	}

	private void SaveInspectorPin_Click(object sender, RoutedEventArgs e)
	{
		if (DataContext is SettingsPageViewModel settingsPageViewModel)
		{
			settingsPageViewModel.SaveDocumentOutputSettingsCommand.Execute(null);
			InspectorPinBox?.Clear();
			InspectorPinConfirmationBox?.Clear();
		}
	}

	private void SupportEmail_RequestNavigate(object sender, RequestNavigateEventArgs e)
	{
		Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri)
		{
			UseShellExecute = true
		});
		e.Handled = true;
	}

	[DebuggerNonUserCode]
	[GeneratedCode("PresentationBuildTasks", "10.0.12.0")]
	internal Delegate _CreateDelegate(Type delegateType, string handler)
	{
		return Delegate.CreateDelegate(delegateType, this, handler);
	}
}
