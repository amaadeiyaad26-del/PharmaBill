using System;
using System.Diagnostics;
using System.Windows;
using PharmaBill.App.Services;
using PharmaBill.App.ViewModels;
using PharmaBill.Core.Entities;
using PharmaBill.Core.Services.Auth;

namespace PharmaBill.App;

public partial class InitialSetupWindow : Window
{
	private readonly CurrentSession _currentSession;

	public InitialSetupWindow(InitialSetupViewModel viewModel, CurrentSession currentSession)
	{
		InitialSetupWindow initialSetupWindow = this;
		_currentSession = currentSession;
		InitializeComponent();
		DataContext = viewModel;
		viewModel.SetupCompleted += OnSetupCompleted;
		Closed += (object? _, EventArgs _) =>
		{
			viewModel.SetupCompleted -= initialSetupWindow.OnSetupCompleted;
		};
	}

	public void PrefillSocial(SocialAuthProfile profile)
	{
		if (DataContext is InitialSetupViewModel initialSetupViewModel)
		{
			initialSetupViewModel.ApplySocialPrefill(profile);
		}
	}

	private void SecretBox_OnPasswordChanged(object sender, RoutedEventArgs e)
	{
		if (DataContext is InitialSetupViewModel initialSetupViewModel)
		{
			initialSetupViewModel.AdminSecret = SecretBox.Password;
		}
	}

	private void ConfirmSecretBox_OnPasswordChanged(object sender, RoutedEventArgs e)
	{
		if (DataContext is InitialSetupViewModel initialSetupViewModel)
		{
			initialSetupViewModel.AdminSecretConfirmation = ConfirmSecretBox.Password;
		}
	}

	private void OnSetupCompleted(object? sender, EventArgs e)
	{
		if (DataContext is InitialSetupViewModel initialSetupViewModel)
		{
			RecoveryCodeWindow recoveryCodeWindow = new RecoveryCodeWindow(initialSetupViewModel.RecoveryCode);
			recoveryCodeWindow.Owner = this;
			recoveryCodeWindow.ShowDialog();
		}
		if (sender is AppUser user)
		{
			_currentSession.SignIn(user);
		}
		DialogResult = true;
		Close();
	}

}
