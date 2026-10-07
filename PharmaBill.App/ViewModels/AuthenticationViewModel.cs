using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PharmaBill.App.Services;
using PharmaBill.Core.Entities;
using PharmaBill.Core.Services.Auth;
using PharmaBill.Data.Services;

namespace PharmaBill.App.ViewModels;

public class AuthenticationViewModel(AuthenticationService authenticationService, CurrentSession currentSession, SocialLoginCoordinator socialLoginCoordinator) : ObservableObject
{
	private const int MaxFailedAttempts = 5;

	private static readonly TimeSpan LockoutDuration = TimeSpan.FromSeconds(30L);

	private int _failedAttempts;

	private DateTime? _lockedUntilUtc;

	private string _username = string.Empty;

	private string _secret = string.Empty;

	private string _errorMessage = string.Empty;

	private bool _isLockedOut;

	private int _lockoutSecondsRemaining;

	private bool _isCapsLockOn;

	private string _versionText = string.Empty;

	private bool _isSocialBusy;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? signInWithGoogleCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? authenticateCommand;

	public bool CanUnlock => !IsLockedOut;

	public bool CanUseSocialSignIn => !IsSocialBusy;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string Username
	{
		get
		{
			return _username;
		}
		[MemberNotNull("_username")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_username, value))
			{
				OnPropertyChanging(nameof(Username));
				_username = value;
				OnPropertyChanged(nameof(Username));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string Secret
	{
		get
		{
			return _secret;
		}
		[MemberNotNull("_secret")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_secret, value))
			{
				OnPropertyChanging(nameof(Secret));
				_secret = value;
				OnPropertyChanged(nameof(Secret));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string ErrorMessage
	{
		get
		{
			return _errorMessage;
		}
		[MemberNotNull("_errorMessage")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_errorMessage, value))
			{
				OnPropertyChanging(nameof(ErrorMessage));
				_errorMessage = value;
				OnPropertyChanged(nameof(ErrorMessage));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool IsLockedOut
	{
		get
		{
			return _isLockedOut;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_isLockedOut, value))
			{
				OnPropertyChanging(nameof(IsLockedOut));
				OnPropertyChanging(nameof(CanUnlock));
				_isLockedOut = value;
				OnPropertyChanged(nameof(IsLockedOut));
				OnPropertyChanged(nameof(CanUnlock));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public int LockoutSecondsRemaining
	{
		get
		{
			return _lockoutSecondsRemaining;
		}
		set
		{
			if (!EqualityComparer<int>.Default.Equals(_lockoutSecondsRemaining, value))
			{
				OnPropertyChanging(nameof(LockoutSecondsRemaining));
				_lockoutSecondsRemaining = value;
				OnPropertyChanged(nameof(LockoutSecondsRemaining));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool IsCapsLockOn
	{
		get
		{
			return _isCapsLockOn;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_isCapsLockOn, value))
			{
				OnPropertyChanging(nameof(IsCapsLockOn));
				_isCapsLockOn = value;
				OnPropertyChanged(nameof(IsCapsLockOn));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string VersionText
	{
		get
		{
			return _versionText;
		}
		[MemberNotNull("_versionText")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_versionText, value))
			{
				OnPropertyChanging(nameof(VersionText));
				_versionText = value;
				OnPropertyChanged(nameof(VersionText));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool IsSocialBusy
	{
		get
		{
			return _isSocialBusy;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_isSocialBusy, value))
			{
				OnPropertyChanging(nameof(IsSocialBusy));
				OnPropertyChanging(nameof(CanUseSocialSignIn));
				_isSocialBusy = value;
				OnPropertyChanged(nameof(IsSocialBusy));
				OnPropertyChanged(nameof(CanUseSocialSignIn));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand SignInWithGoogleCommand => signInWithGoogleCommand ?? (signInWithGoogleCommand = new AsyncRelayCommand(SignInWithGoogleAsync, () => CanUseSocialSignIn));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand AuthenticateCommand => authenticateCommand ?? (authenticateCommand = new AsyncRelayCommand(AuthenticateAsync));

	public event EventHandler? Authenticated;

	public event EventHandler<SocialAuthProfile>? SocialSetupRequired;

	public event EventHandler<SocialLinkAdminRequest>? SocialLinkAdminRequired;

	private async Task SignInWithGoogleAsync()
	{
		await HandleSocialSignInAsync(await socialLoginCoordinator.SignInWithGoogleAsync());
	}

	private async Task AuthenticateAsync()
	{
		RefreshLockout();
		if (IsLockedOut)
		{
			return;
		}
		ErrorMessage = string.Empty;
		AppUser appUser = await authenticationService.AuthenticateAsync(Username, Secret);
		if (appUser == null)
		{
			_failedAttempts++;
			if (_failedAttempts >= 5)
			{
				_lockedUntilUtc = DateTime.UtcNow + LockoutDuration;
				IsLockedOut = true;
				LockoutSecondsRemaining = (int)LockoutDuration.TotalSeconds;
				ErrorMessage = $"Too many wrong attempts. Try again in {LockoutSecondsRemaining} seconds.";
			}
			else
			{
				ErrorMessage = $"Wrong username or password ({5 - _failedAttempts} tries left)";
			}
			Secret = string.Empty;
		}
		else
		{
			_failedAttempts = 0;
			_lockedUntilUtc = null;
			currentSession.SignIn(appUser);
			Secret = string.Empty;
			Authenticated?.Invoke(this, EventArgs.Empty);
		}
	}

	public async Task CompleteLinkAdminAsync(SocialLinkAdminRequest request, string adminSecret)
	{
		IsSocialBusy = true;
		try
		{
			ErrorMessage = string.Empty;
			SocialLoginOutcome socialLoginOutcome = await socialLoginCoordinator.LinkPrimaryAdminAsync(request.AdminId, request.Profile, adminSecret);
			if (socialLoginOutcome.Kind == SocialLoginOutcomeKind.SignedIn)
			{
				Authenticated?.Invoke(this, EventArgs.Empty);
			}
			else
			{
				ErrorMessage = socialLoginOutcome.Message ?? "Could not link the Google account.";
			}
		}
		finally
		{
			IsSocialBusy = false;
		}
	}

	private async Task HandleSocialSignInAsync(SocialLoginOutcome outcome)
	{
		IsSocialBusy = true;
		try
		{
			ErrorMessage = string.Empty;
			switch (outcome.Kind)
			{
			case SocialLoginOutcomeKind.SignedIn:
				Authenticated?.Invoke(this, EventArgs.Empty);
				return;
			case SocialLoginOutcomeKind.SetupRequired:
				if ((object)outcome.Profile != null)
				{
					SocialSetupRequired?.Invoke(this, outcome.Profile);
					return;
				}
				break;
			case SocialLoginOutcomeKind.LinkAdminRequired:
				if ((object)outcome.Profile != null)
				{
					Guid? linkableAdminId = outcome.LinkableAdminId;
					if (linkableAdminId.HasValue)
					{
						Guid valueOrDefault = linkableAdminId.GetValueOrDefault();
						SocialLinkAdminRequired?.Invoke(this, new SocialLinkAdminRequest(outcome.Profile, valueOrDefault, string.IsNullOrWhiteSpace(outcome.LinkableAdminDisplayName) ? "Admin" : outcome.LinkableAdminDisplayName));
						return;
					}
				}
				break;
			case SocialLoginOutcomeKind.Offline:
			case SocialLoginOutcomeKind.Error:
				ErrorMessage = outcome.Message ?? "Social sign-in failed.";
				return;
			case SocialLoginOutcomeKind.Cancelled:
				return;
			}
		}
		finally
		{
			IsSocialBusy = false;
		}
		await Task.CompletedTask;
	}

	public void RefreshLockout()
	{
		DateTime? lockedUntilUtc = _lockedUntilUtc;
		if (lockedUntilUtc.HasValue)
		{
			DateTime valueOrDefault = lockedUntilUtc.GetValueOrDefault();
			LockoutSecondsRemaining = Math.Max(0, (int)Math.Ceiling((valueOrDefault - DateTime.UtcNow).TotalSeconds));
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
}
