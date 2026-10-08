using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PharmaBill.App.Services;
using PharmaBill.Core.Entities;
using PharmaBill.Core.Services.Auth;
using PharmaBill.Data.Services;

namespace PharmaBill.App.ViewModels;

public partial class InitialSetupViewModel : ObservableObject
{
	private readonly PharmacySetupService _setupService;
	private readonly RecoveryCodeStore _recoveryCodeStore;
	private readonly SocialLoginCoordinator _socialLoginCoordinator;
	private readonly CurrentSession _currentSession;
	private readonly RegistrationNotificationService _registrationNotifications;

	[ObservableProperty]
	private BusinessMode _selectedBusinessMode;

	[ObservableProperty]
	private string _pharmacyName = string.Empty;

	[ObservableProperty]
	private string _legalName = string.Empty;

	[ObservableProperty]
	private string _address = string.Empty;

	[ObservableProperty]
	private string _phone = string.Empty;

	[ObservableProperty]
	private string _email = string.Empty;

	[ObservableProperty]
	private string _gstin = string.Empty;

	[ObservableProperty]
	private string _drugLicence20 = string.Empty;

	[ObservableProperty]
	private string _drugLicence21 = string.Empty;

	[ObservableProperty]
	private string _drugLicence20B = string.Empty;

	[ObservableProperty]
	private string _drugLicence21B = string.Empty;

	[ObservableProperty]
	private string _pan = string.Empty;

	[ObservableProperty]
	private string _competentPersonName = string.Empty;

	[ObservableProperty]
	private string _competentPersonQualification = string.Empty;

	[ObservableProperty]
	private string _competentPersonRegistrationNumber = string.Empty;

	[ObservableProperty]
	private string _bankName = string.Empty;

	[ObservableProperty]
	private string _bankAccountName = string.Empty;

	[ObservableProperty]
	private string _bankAccountNumber = string.Empty;

	[ObservableProperty]
	private string _bankIfsc = string.Empty;

	[ObservableProperty]
	private string _upiId = string.Empty;

	[ObservableProperty]
	private string _invoicePrefix = "WIN1";

	[ObservableProperty]
	private string _adminFullName = string.Empty;

	[ObservableProperty]
	private string _adminUsername = string.Empty;

	[ObservableProperty]
	private string _adminPhone = string.Empty;

	[ObservableProperty]
	private string _adminSecret = string.Empty;

	[ObservableProperty]
	private string _adminSecretConfirmation = string.Empty;

	[ObservableProperty]
	private string _idleLockMinutes = "10";

	[ObservableProperty]
	private string _errorMessage = string.Empty;

	[ObservableProperty]
	private bool _isSocialOnboarding;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(CanUseSocialSignIn))]
	[NotifyCanExecuteChangedFor(nameof(SignInWithGoogleCommand))]
	private bool _isSocialBusy;

	[ObservableProperty]
	private string? _socialAuthProvider;

	[ObservableProperty]
	private string? _socialSubjectId;

	public string RecoveryCode { get; private set; } = string.Empty;

	public bool CanUseSocialSignIn => !IsSocialBusy;

	public bool ShowRetailLicenceFields =>
		SelectedBusinessMode is BusinessMode.Retail or BusinessMode.Both;

	public bool ShowWholesaleLicenceFields =>
		SelectedBusinessMode is BusinessMode.Wholesaler or BusinessMode.Both;

	public string PageTitle => SelectedBusinessMode switch
	{
		BusinessMode.Wholesaler => "Set up your Wholesale Distribution",
		BusinessMode.Both => "Set up your Pharmacy & Wholesale Enterprise",
		_ => "Set up your pharmacy",
	};

	public string DetailsGroupHeader => SelectedBusinessMode switch
	{
		BusinessMode.Wholesaler => "Distributor & Firm Details",
		BusinessMode.Both => "Pharmacy & Wholesale Enterprise Details",
		_ => "Pharmacy details",
	};

	public string NameFieldLabel => SelectedBusinessMode switch
	{
		BusinessMode.Wholesaler => "Firm / Wholesaler Name *",
		BusinessMode.Both => "Pharmacy / Firm Name *",
		_ => "Pharmacy / store name *",
	};

	public bool IsRetailStoreSelected
	{
		get => SelectedBusinessMode == BusinessMode.Retail;
		set
		{
			if (value)
			{
				SelectedBusinessMode = BusinessMode.Retail;
			}
		}
	}

	public bool IsWholesaleStoreSelected
	{
		get => SelectedBusinessMode == BusinessMode.Wholesaler;
		set
		{
			if (value)
			{
				SelectedBusinessMode = BusinessMode.Wholesaler;
			}
		}
	}

	public bool IsBothStoreSelected
	{
		get => SelectedBusinessMode == BusinessMode.Both;
		set
		{
			if (value)
			{
				SelectedBusinessMode = BusinessMode.Both;
			}
		}
	}

	public event EventHandler? SetupCompleted;

	public InitialSetupViewModel(
		PharmacySetupService setupService,
		RecoveryCodeStore recoveryCodeStore,
		SocialLoginCoordinator socialLoginCoordinator,
		CurrentSession currentSession,
		RegistrationNotificationService registrationNotifications)
	{
		_setupService = setupService;
		_recoveryCodeStore = recoveryCodeStore;
		_socialLoginCoordinator = socialLoginCoordinator;
		_currentSession = currentSession;
		_registrationNotifications = registrationNotifications;
	}

	public void ApplySocialPrefill(SocialAuthProfile profile)
	{
		IsSocialOnboarding = true;
		SocialAuthProvider = profile.Provider;
		SocialSubjectId = profile.SubjectId;
		AdminFullName = string.IsNullOrWhiteSpace(profile.Name) ? (profile.Email ?? string.Empty) : profile.Name;
		Email = profile.Email ?? string.Empty;
		AdminUsername = DeriveUsername(profile.Email);
		ErrorMessage = string.Empty;
	}

	[RelayCommand(CanExecute = nameof(CanUseSocialSignIn))]
	private async Task SignInWithGoogleAsync()
	{
		await HandleSocialSignInAsync(await _socialLoginCoordinator.SignInWithGoogleAsync());
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
				if (_currentSession.User != null)
				{
					SetupCompleted?.Invoke(_currentSession.User, EventArgs.Empty);
				}
				return;
			case SocialLoginOutcomeKind.SetupRequired:
				if (outcome.Profile != null)
				{
					ApplySocialPrefill(outcome.Profile);
				}
				return;
			case SocialLoginOutcomeKind.LinkAdminRequired:
				ErrorMessage = "This Google account is not linked yet. Close setup and use Unlock → Continue with Google to link it to the primary Admin.";
				return;
			case SocialLoginOutcomeKind.Offline:
			case SocialLoginOutcomeKind.Error:
				ErrorMessage = outcome.Message ?? "Social sign-in failed.";
				return;
			}
		}
		finally
		{
			IsSocialBusy = false;
		}

		await Task.CompletedTask;
	}

	[RelayCommand]
	private async Task CompleteSetupAsync()
	{
		ErrorMessage = string.Empty;

		if (string.IsNullOrWhiteSpace(PharmacyName) || string.IsNullOrWhiteSpace(Phone))
		{
			ErrorMessage = SelectedBusinessMode == BusinessMode.Wholesaler
				? "Enter firm name and phone number."
				: "Enter pharmacy / store name and phone number.";
			return;
		}

		if (string.IsNullOrWhiteSpace(AdminFullName) || string.IsNullOrWhiteSpace(AdminUsername))
		{
			ErrorMessage = "Enter admin full name and username.";
			return;
		}

		if (!HasRequiredLicenceNumbers(out string licenceError))
		{
			ErrorMessage = licenceError;
			return;
		}

		if (AdminSecret.Length < 4)
		{
			ErrorMessage = "PIN or password must contain at least 4 characters.";
			return;
		}

		if (AdminSecret != AdminSecretConfirmation)
		{
			ErrorMessage = "PIN or password confirmation does not match.";
			return;
		}

		if (!int.TryParse(IdleLockMinutes, out int idleMinutes) || idleMinutes < 1 || idleMinutes > 240)
		{
			ErrorMessage = "Lock timeout must be between 1 and 240 minutes.";
			return;
		}

		try
		{
			List<LicenceRecord> licences = BuildLicenceRecords();
			PharmacyProfile profile = new PharmacyProfile
			{
				Name = PharmacyName.Trim(),
				LegalName = NullIfWhiteSpace(LegalName),
				Address = NullIfWhiteSpace(Address),
				Phone = Phone.Trim(),
				Email = NullIfWhiteSpace(Email),
				Gstin = NullIfWhiteSpace(Gstin),
				Pan = NullIfWhiteSpace(Pan),
				BusinessMode = SelectedBusinessMode,
				CompetentPersonName = NullIfWhiteSpace(CompetentPersonName),
				CompetentPersonQualification = NullIfWhiteSpace(CompetentPersonQualification),
				CompetentPersonRegistrationNumber = NullIfWhiteSpace(CompetentPersonRegistrationNumber),
				BankName = NullIfWhiteSpace(BankName),
				BankAccountName = NullIfWhiteSpace(BankAccountName),
				BankAccountNumber = NullIfWhiteSpace(BankAccountNumber),
				BankIfsc = NullIfWhiteSpace(BankIfsc),
				UpiId = NullIfWhiteSpace(UpiId),
				InvoicePrefix = string.IsNullOrWhiteSpace(InvoicePrefix) ? "WIN1" : InvoicePrefix.Trim(),
				WholesaleLicenceTypesJson = JsonSerializer.Serialize(BuildWholesaleLicenceTypes()),
			};
			AppUser admin = new AppUser
			{
				DisplayName = AdminFullName.Trim(),
				UserName = AdminUsername.Trim(),
				Phone = NullIfWhiteSpace(AdminPhone),
				Email = NullIfWhiteSpace(Email),
				Role = UserRole.Owner,
				IdleLockMinutes = idleMinutes,
				AuthProvider = NullIfWhiteSpace(SocialAuthProvider ?? string.Empty),
				ProviderSubjectId = NullIfWhiteSpace(SocialSubjectId ?? string.Empty),
			};

			RecoveryCode = await _recoveryCodeStore.CreateAsync();
			try
			{
				await _setupService.CompleteSetupAsync(profile, licences, admin, AdminSecret);
			}
			catch
			{
				await _recoveryCodeStore.DeleteAsync();
				RecoveryCode = string.Empty;
				throw;
			}

			if (!string.IsNullOrWhiteSpace(profile.Email))
			{
				_registrationNotifications.ScheduleWelcomeManualEmail(profile.Email, profile.Name, admin.DisplayName);
			}

			SetupCompleted?.Invoke(admin, EventArgs.Empty);
		}
		catch (Exception ex) when (ex is InvalidOperationException or IOException or ArgumentException or CryptographicException or PlatformNotSupportedException)
		{
			ErrorMessage = ex.Message;
		}
	}

	partial void OnSelectedBusinessModeChanged(BusinessMode value)
	{
		OnPropertyChanged(nameof(IsRetailStoreSelected));
		OnPropertyChanged(nameof(IsWholesaleStoreSelected));
		OnPropertyChanged(nameof(IsBothStoreSelected));
		OnPropertyChanged(nameof(PageTitle));
		OnPropertyChanged(nameof(DetailsGroupHeader));
		OnPropertyChanged(nameof(NameFieldLabel));
		OnPropertyChanged(nameof(ShowRetailLicenceFields));
		OnPropertyChanged(nameof(ShowWholesaleLicenceFields));
	}

	private bool HasRequiredLicenceNumbers(out string error)
	{
		bool needsRetail = SelectedBusinessMode is BusinessMode.Retail or BusinessMode.Both;
		bool needsWholesale = SelectedBusinessMode is BusinessMode.Wholesaler or BusinessMode.Both;
		bool hasRetail = !string.IsNullOrWhiteSpace(DrugLicence20) || !string.IsNullOrWhiteSpace(DrugLicence21);
		bool hasWholesale = !string.IsNullOrWhiteSpace(DrugLicence20B) || !string.IsNullOrWhiteSpace(DrugLicence21B);

		if (needsRetail && !hasRetail)
		{
			error = "Enter at least one retail drug licence number (Form 20 or Form 21).";
			return false;
		}

		if (needsWholesale && !hasWholesale)
		{
			error = "Enter at least one wholesale drug licence number (Form 20B or Form 21B).";
			return false;
		}

		error = string.Empty;
		return true;
	}

	private List<LicenceRecord> BuildLicenceRecords()
	{
		List<LicenceRecord> licences = new();
		AddLicenceIfPresent(licences, "20", DrugLicence20);
		AddLicenceIfPresent(licences, "21", DrugLicence21);
		AddLicenceIfPresent(licences, "20B", DrugLicence20B);
		AddLicenceIfPresent(licences, "21B", DrugLicence21B);
		return licences;
	}

	private static void AddLicenceIfPresent(List<LicenceRecord> licences, string licenceType, string licenceNumber)
	{
		if (string.IsNullOrWhiteSpace(licenceNumber))
		{
			return;
		}

		licences.Add(new LicenceRecord
		{
			LicenceType = licenceType,
			LicenceNumber = licenceNumber.Trim(),
			IssuedOn = null,
			ExpiresOn = null,
			DocumentPath = null,
		});
	}

	private string[] BuildWholesaleLicenceTypes()
	{
		if (!ShowWholesaleLicenceFields)
		{
			return Array.Empty<string>();
		}

		return new[] { "20B", "21B" };
	}

	private static string DeriveUsername(string? email)
	{
		if (string.IsNullOrWhiteSpace(email) || !email.Contains('@', StringComparison.Ordinal))
		{
			return $"admin-{Guid.NewGuid():N}"[..12];
		}

		string local = email.Split('@')[0].Trim();
		return local.Length < 3 ? email.Replace('@', '.') : local;
	}

	private static string? NullIfWhiteSpace(string value) =>
		string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
