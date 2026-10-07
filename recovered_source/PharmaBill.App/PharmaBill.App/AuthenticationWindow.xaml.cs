using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Navigation;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using PharmaBill.App.Services;
using PharmaBill.App.ViewModels;
using PharmaBill.Core.Services.Auth;
using PharmaBill.Data.Persistence;
using PharmaBill.Data.Services;

namespace PharmaBill.App;

public partial class AuthenticationWindow : Window, IComponentConnector
{
	private readonly AuthenticationViewModel _viewModel;

	private readonly LoginPreferenceStore _preferenceStore;

	private readonly OwnerRecoveryService _recoveryService;

	private readonly IServiceScopeFactory _scopeFactory;

	private readonly DispatcherTimer _lockoutTimer;

	private bool _updatingSecret;

	public event EventHandler? Authenticated;

	public AuthenticationWindow(AuthenticationViewModel viewModel, LoginPreferenceStore preferenceStore, OwnerRecoveryService recoveryService, AppVersionInfo versionInfo, IServiceScopeFactory scopeFactory)
	{
		_viewModel = viewModel;
		_preferenceStore = preferenceStore;
		_recoveryService = recoveryService;
		_scopeFactory = scopeFactory;
		InitializeComponent();
		DataContext = viewModel;
		viewModel.VersionText = versionInfo.Display;
		viewModel.Username = preferenceStore.Load();
		viewModel.Authenticated += OnAuthenticated;
		viewModel.SocialSetupRequired += OnSocialSetupRequired;
		viewModel.SocialLinkAdminRequired += OnSocialLinkAdminRequired;
		viewModel.PropertyChanged += OnViewModelPropertyChanged;
		_lockoutTimer = new DispatcherTimer
		{
			Interval = TimeSpan.FromSeconds(1L)
		};
		_lockoutTimer.Tick += (object? _, EventArgs _) =>
		{
			_viewModel.RefreshLockout();
		};
		_lockoutTimer.Start();
		Closed += OnClosed;
	}

	private void OnAuthenticated(object? sender, EventArgs e)
	{
		Authenticated?.Invoke(this, EventArgs.Empty);
	}

	private void OnClosed(object? sender, EventArgs e)
	{
		_lockoutTimer.Stop();
		_viewModel.Authenticated -= OnAuthenticated;
		_viewModel.SocialSetupRequired -= OnSocialSetupRequired;
		_viewModel.SocialLinkAdminRequired -= OnSocialLinkAdminRequired;
		_viewModel.PropertyChanged -= OnViewModelPropertyChanged;
		_preferenceStore.Save(_viewModel.Username);
	}

	private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName != "Secret" || _viewModel.Secret.Length != 0)
		{
			return;
		}
		_updatingSecret = true;
		try
		{
			SecretBox.Clear();
			VisibleSecretBox.Clear();
		}
		finally
		{
			_updatingSecret = false;
		}
	}

	private void Window_Loaded(object sender, RoutedEventArgs e)
	{
		UsernameBox.Focus();
		UsernameBox.SelectAll();
		RefreshCapsLockHint();
	}

	private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
	{
		RefreshCapsLockHint();
		if (e.Key == Key.Escape)
		{
			Close();
			e.Handled = true;
		}
	}

	private void RefreshCapsLockHint()
	{
		_viewModel.IsCapsLockOn = Keyboard.IsKeyToggled(Key.Capital);
	}

	private void SecretBox_OnPasswordChanged(object sender, RoutedEventArgs e)
	{
		if (!_updatingSecret)
		{
			_updatingSecret = true;
			try
			{
				_viewModel.Secret = SecretBox.Password;
				VisibleSecretBox.Text = SecretBox.Password;
			}
			finally
			{
				_updatingSecret = false;
			}
		}
	}

	private void VisibleSecretBox_OnTextChanged(object sender, TextChangedEventArgs e)
	{
		if (!_updatingSecret)
		{
			_updatingSecret = true;
			try
			{
				_viewModel.Secret = VisibleSecretBox.Text;
				SecretBox.Password = VisibleSecretBox.Text;
			}
			finally
			{
				_updatingSecret = false;
			}
		}
	}

	private void ShowSecret_Click(object sender, RoutedEventArgs e)
	{
		_updatingSecret = true;
		try
		{
			bool flag = SecretBox.Visibility == Visibility.Visible;
			SecretBox.Visibility = (flag ? Visibility.Collapsed : Visibility.Visible);
			VisibleSecretBox.Visibility = ((!flag) ? Visibility.Collapsed : Visibility.Visible);
			ShowSecretButton.Content = (flag ? "◉" : "●");
			if (flag)
			{
				VisibleSecretBox.Focus();
				VisibleSecretBox.CaretIndex = VisibleSecretBox.Text.Length;
			}
			else
			{
				SecretBox.Focus();
			}
		}
		finally
		{
			_updatingSecret = false;
		}
	}

	private void ForgotPin_Click(object sender, RoutedEventArgs e)
	{
		if (new RecoveryResetWindow(_recoveryService, _viewModel.Username)
		{
			Owner = this
		}.ShowDialog() == true)
		{
			_viewModel.ErrorMessage = "PIN/password reset. Sign in with the new PIN or password.";
		}
	}

	private void OnSocialSetupRequired(object? sender, SocialAuthProfile profile)
	{
		try
		{
			using IServiceScope serviceScope = _scopeFactory.CreateScope();
			InitialSetupWindow requiredService = serviceScope.ServiceProvider.GetRequiredService<InitialSetupWindow>();
			requiredService.Owner = this;
			requiredService.PrefillSocial(profile);
			if (requiredService.ShowDialog() == true)
			{
				Authenticated?.Invoke(this, EventArgs.Empty);
			}
		}
		catch (Exception ex)
		{
			_viewModel.ErrorMessage = ex.Message;
		}
	}

	private async void OnSocialLinkAdminRequired(object? sender, SocialLinkAdminRequest request)
	{
		try
		{
			string text = (string.IsNullOrWhiteSpace(request.Profile.Email) ? "this Google account" : request.Profile.Email);
			if (MessageBox.Show(this, "No account is currently linked to " + text + ". Would you like to link this Google account to the primary Admin account?", "Link Admin account", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
			{
				PinPromptWindow pinPromptWindow = new PinPromptWindow("Enter the Admin PIN or password for " + request.AdminDisplayName + " to link Google sign-in.")
				{
					Owner = this,
					ShowUsername = false
				};
				if (pinPromptWindow.ShowDialog() == true)
				{
					await _viewModel.CompleteLinkAdminAsync(request, pinPromptWindow.Pin);
				}
			}
		}
		catch (Exception ex)
		{
			_viewModel.ErrorMessage = ex.Message;
		}
	}

	private async void CreateAccount_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			using IServiceScope scope = _scopeFactory.CreateScope();
			if (!(await PharmacySetupProbe.IsInitialSetupRequiredAsync(scope.ServiceProvider.GetRequiredService<PharmaBillDbContext>())) && MessageBox.Show(this, "An existing pharmacy database is already configured. Proceed to setup/reconfigure a pharmacy profile?", "Pharmacy already configured", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
			{
				return;
			}
			_viewModel.ErrorMessage = string.Empty;
			InitialSetupWindow requiredService = scope.ServiceProvider.GetRequiredService<InitialSetupWindow>();
			requiredService.Owner = this;
			if (requiredService.ShowDialog() == true)
			{
				Authenticated?.Invoke(this, EventArgs.Empty);
			}
		}
		catch (Exception ex)
		{
			_viewModel.ErrorMessage = ex.Message;
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
}
