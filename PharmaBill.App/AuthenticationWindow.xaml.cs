using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Navigation;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using PharmaBill.App.Services;
using PharmaBill.App.ViewModels;
using PharmaBill.Core.Services.Auth;
using PharmaBill.Data.Persistence;
using PharmaBill.Data.Services;

namespace PharmaBill.App;

public partial class AuthenticationWindow : Window
{
    private readonly AuthenticationViewModel _viewModel;
    private readonly LoginPreferenceStore _preferenceStore;
    private readonly OwnerRecoveryService _recoveryService;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly DispatcherTimer _lockoutTimer;
    private bool _updatingSecret;

    public AuthenticationWindow(
        AuthenticationViewModel viewModel,
        LoginPreferenceStore preferenceStore,
        OwnerRecoveryService recoveryService,
        AppVersionInfo versionInfo,
        IServiceScopeFactory scopeFactory)
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
        _lockoutTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _lockoutTimer.Tick += (_, _) => _viewModel.RefreshLockout();
        _lockoutTimer.Start();
        Closed += OnClosed;
    }

    public event EventHandler? Authenticated;

    private void OnAuthenticated(object? sender, EventArgs e) => Authenticated?.Invoke(this, EventArgs.Empty);

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
        if (e.PropertyName != nameof(AuthenticationViewModel.Secret) || _viewModel.Secret.Length != 0)
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
        _viewModel.IsCapsLockOn = Keyboard.IsKeyToggled(Key.CapsLock);
    }

    private void SecretBox_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_updatingSecret)
        {
            return;
        }

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

    private void VisibleSecretBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_updatingSecret)
        {
            return;
        }

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

    private void ShowSecret_Click(object sender, RoutedEventArgs e)
    {
        _updatingSecret = true;
        try
        {
            var showSecret = SecretBox.Visibility == Visibility.Visible;
            SecretBox.Visibility = showSecret ? Visibility.Collapsed : Visibility.Visible;
            VisibleSecretBox.Visibility = showSecret ? Visibility.Visible : Visibility.Collapsed;
            ShowSecretButton.Content = showSecret ? "◉" : "●";
            if (showSecret)
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
        var resetWindow = new RecoveryResetWindow(_recoveryService, _viewModel.Username) { Owner = this };
        if (resetWindow.ShowDialog() == true)
        {
            _viewModel.ErrorMessage = "PIN/password reset. Sign in with the new PIN or password.";
        }
    }

    private void OnSocialSetupRequired(object? sender, SocialAuthProfile profile)
    {
        try
        {
            MessageBox.Show(
                this,
                "No pharmacy account is linked to this Google sign-in yet. Complete pharmacy setup, then link Google from the unlock screen.",
                "Pharmacy setup required",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            using var scope = _scopeFactory.CreateScope();
            var setup = scope.ServiceProvider.GetRequiredService<InitialSetupWindow>();
            setup.Owner = this;
            setup.PrefillSocial(profile);
            if (setup.ShowDialog() == true)
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
            var accountLabel = string.IsNullOrWhiteSpace(request.Profile.Email)
                ? "this Google account"
                : request.Profile.Email;
            if (MessageBox.Show(
                    this,
                    $"No account is currently linked to {accountLabel}. Would you like to link this Google account to the primary Admin account?",
                    "Link Admin account",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question) != MessageBoxResult.Yes)
            {
                return;
            }

            var pinPrompt = new PinPromptWindow(
                $"Enter the Admin PIN or password for {request.AdminDisplayName} to link Google sign-in.")
            {
                Owner = this
            };
            if (pinPrompt.ShowDialog() == true)
            {
                await _viewModel.CompleteLinkAdminAsync(request, pinPrompt.Pin);
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
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<PharmaBillDbContext>();
            if (!await PharmacySetupProbe.IsInitialSetupRequiredAsync(context)
                && MessageBox.Show(
                    this,
                    "An existing pharmacy database is already configured. Proceed to setup/reconfigure a pharmacy profile?",
                    "Pharmacy already configured",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question) != MessageBoxResult.Yes)
            {
                return;
            }

            _viewModel.ErrorMessage = string.Empty;
            var setup = scope.ServiceProvider.GetRequiredService<InitialSetupWindow>();
            setup.Owner = this;
            if (setup.ShowDialog() == true)
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
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }
}
