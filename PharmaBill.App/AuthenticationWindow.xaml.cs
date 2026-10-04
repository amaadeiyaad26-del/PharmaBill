using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using System.ComponentModel;
using PharmaBill.App.Services;
using PharmaBill.App.ViewModels;

namespace PharmaBill.App;

public partial class AuthenticationWindow : Window
{
    private readonly AuthenticationViewModel _viewModel;
    private readonly LoginPreferenceStore _preferenceStore;
    private readonly OwnerRecoveryService _recoveryService;
    private readonly DispatcherTimer _lockoutTimer;
    private bool _updatingSecret;

    public AuthenticationWindow(
        AuthenticationViewModel viewModel,
        LoginPreferenceStore preferenceStore,
        OwnerRecoveryService recoveryService,
        AppVersionInfo versionInfo)
    {
        _viewModel = viewModel;
        _preferenceStore = preferenceStore;
        _recoveryService = recoveryService;
        InitializeComponent();
        DataContext = viewModel;
        viewModel.VersionText = versionInfo.Display;
        viewModel.Username = preferenceStore.Load();
        viewModel.Authenticated += OnAuthenticated;
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

    private void VisibleSecretBox_OnTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
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
        var resetWindow = new RecoveryResetWindow(_recoveryService, _viewModel.Username)
        {
            Owner = this
        };
        if (resetWindow.ShowDialog() == true)
        {
            _viewModel.ErrorMessage = "PIN/password reset. Sign in with the new PIN or password.";
        }
    }
}
