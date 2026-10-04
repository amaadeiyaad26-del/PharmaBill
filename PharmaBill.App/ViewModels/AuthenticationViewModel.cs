using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PharmaBill.App.Services;
using PharmaBill.Data.Services;

namespace PharmaBill.App.ViewModels;

public partial class AuthenticationViewModel(
    AuthenticationService authenticationService,
    CurrentSession currentSession) : ObservableObject
{
    private const int MaxFailedAttempts = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromSeconds(30);
    private int _failedAttempts;
    private DateTime? _lockedUntilUtc;

    [ObservableProperty]
    private string _username = string.Empty;

    [ObservableProperty]
    private string _secret = string.Empty;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanUnlock))]
    private bool _isLockedOut;

    [ObservableProperty]
    private int _lockoutSecondsRemaining;

    [ObservableProperty]
    private bool _isCapsLockOn;

    [ObservableProperty]
    private string _versionText = string.Empty;

    public bool CanUnlock => !IsLockedOut;

    public event EventHandler? Authenticated;

    [RelayCommand]
    private async Task AuthenticateAsync()
    {
        RefreshLockout();
        if (IsLockedOut)
        {
            return;
        }

        ErrorMessage = string.Empty;
        var user = await authenticationService.AuthenticateAsync(Username, Secret);
        if (user is null)
        {
            _failedAttempts++;
            if (_failedAttempts >= MaxFailedAttempts)
            {
                _lockedUntilUtc = DateTime.UtcNow + LockoutDuration;
                IsLockedOut = true;
                LockoutSecondsRemaining = (int)LockoutDuration.TotalSeconds;
                ErrorMessage = $"Too many wrong attempts. Try again in {LockoutSecondsRemaining} seconds.";
            }
            else
            {
                ErrorMessage = $"Wrong username or password ({MaxFailedAttempts - _failedAttempts} tries left)";
            }

            Secret = string.Empty;
            return;
        }

        _failedAttempts = 0;
        _lockedUntilUtc = null;
        currentSession.SignIn(user);
        Secret = string.Empty;
        Authenticated?.Invoke(this, EventArgs.Empty);
    }

    public void RefreshLockout()
    {
        if (_lockedUntilUtc is not { } until)
        {
            return;
        }

        LockoutSecondsRemaining = Math.Max(0, (int)Math.Ceiling((until - DateTime.UtcNow).TotalSeconds));
        if (LockoutSecondsRemaining == 0)
        {
            _lockedUntilUtc = null;
            _failedAttempts = 0;
            IsLockedOut = false;
            ErrorMessage = string.Empty;
        }
        else
        {
            ErrorMessage = $"Too many wrong attempts. Try again in {LockoutSecondsRemaining} seconds.";
        }
    }
}
