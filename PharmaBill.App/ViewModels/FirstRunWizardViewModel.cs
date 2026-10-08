using System.IO;
using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PharmaBill.App.Services;
using PharmaBill.Core.Entities;
using PharmaBill.Data.Services;

namespace PharmaBill.App.ViewModels;

public partial class FirstRunWizardViewModel : ObservableObject
{
    private readonly PharmacySetupService _setupService;
    private readonly IFilePickerService _filePicker;
    private readonly RecoveryCodeStore _recoveryCodeStore;

    public FirstRunWizardViewModel(
        PharmacySetupService setupService,
        IFilePickerService filePicker,
        RecoveryCodeStore recoveryCodeStore)
    {
        _setupService = setupService;
        _filePicker = filePicker;
        _recoveryCodeStore = recoveryCodeStore;
        BusinessModes = Enum.GetValues<BusinessMode>();
        LicenceTypeOptions = ["20", "21"];
    }

    public IReadOnlyList<BusinessMode> BusinessModes { get; }

    public ObservableCollection<string> LicenceTypeOptions { get; }

    public ObservableCollection<LicenceDraftViewModel> Licences { get; } = [];

    [ObservableProperty]
    private BusinessMode _selectedBusinessMode = BusinessMode.Retail;

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
    private string _ownerName = string.Empty;

    [ObservableProperty]
    private string _ownerUsername = string.Empty;

    [ObservableProperty]
    private string _ownerPhone = string.Empty;

    [ObservableProperty]
    private string _ownerSecret = string.Empty;

    [ObservableProperty]
    private string _ownerSecretConfirmation = string.Empty;

    [ObservableProperty]
    private string _idleLockMinutes = "10";

    [ObservableProperty]
    private string _wholesaleLicenceType = string.Empty;

    [ObservableProperty]
    private string _newLicenceType = string.Empty;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    public string RecoveryCode { get; private set; } = string.Empty;

    public event EventHandler? SetupCompleted;

    [RelayCommand]
    private void AddLicenceType()
    {
        var value = NewLicenceType.Trim();
        if (value.Length == 0)
        {
            ErrorMessage = "Enter a licence type before adding it.";
            return;
        }

        if (!LicenceTypeOptions.Contains(value, StringComparer.OrdinalIgnoreCase))
        {
            LicenceTypeOptions.Add(value);
        }

        WholesaleLicenceType = value;
        NewLicenceType = string.Empty;
        ErrorMessage = string.Empty;
    }

    [RelayCommand]
    private void AddLicence()
    {
        var type = WholesaleLicenceType;
        if (string.IsNullOrWhiteSpace(type))
        {
            ErrorMessage = "Select a wholesale licence type first, or use the Retail Form 20 / 21 buttons.";
            return;
        }

        Licences.Add(new LicenceDraftViewModel { LicenceType = type });
        ErrorMessage = string.Empty;
    }

    [RelayCommand]
    private void AddRetail20() => Licences.Add(new LicenceDraftViewModel { LicenceType = "20" });

    [RelayCommand]
    private void AddRetail21() => Licences.Add(new LicenceDraftViewModel { LicenceType = "21" });

    [RelayCommand]
    private void SelectDocument(LicenceDraftViewModel? licence)
    {
        if (licence is not null)
        {
            licence.DocumentPath = _filePicker.PickLicenceDocument();
        }
    }

    [RelayCommand]
    private void RemoveLicence(LicenceDraftViewModel? licence)
    {
        if (licence is not null)
        {
            Licences.Remove(licence);
        }
    }

    [RelayCommand]
    private async Task CompleteSetupAsync()
    {
        ErrorMessage = string.Empty;
        if (string.IsNullOrWhiteSpace(PharmacyName) ||
            string.IsNullOrWhiteSpace(OwnerName) ||
            string.IsNullOrWhiteSpace(OwnerUsername))
        {
            ErrorMessage = "Enter the pharmacy name and owner name and username.";
            return;
        }

        if (OwnerSecret.Length < 6)
        {
            ErrorMessage = "PIN or password must contain at least 6 characters.";
            return;
        }

        if (OwnerSecret != OwnerSecretConfirmation)
        {
            ErrorMessage = "PIN or password confirmation does not match.";
            return;
        }

        if (!int.TryParse(IdleLockMinutes, out var idleMinutes) || idleMinutes is < 1 or > 240)
        {
            ErrorMessage = "Lock timeout must be between 1 and 240 minutes.";
            return;
        }

        try
        {
            var profile = new PharmacyProfile
            {
                Name = PharmacyName.Trim(),
                LegalName = NullIfWhiteSpace(LegalName),
                Address = NullIfWhiteSpace(Address),
                Phone = NullIfWhiteSpace(Phone),
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
                WholesaleLicenceTypesJson = JsonSerializer.Serialize(
                    LicenceTypeOptions.Where(type => type is not "20" and not "21").ToArray())
            };
            var owner = new AppUser
            {
                DisplayName = OwnerName.Trim(),
                UserName = OwnerUsername.Trim(),
                Phone = NullIfWhiteSpace(OwnerPhone),
                Role = UserRole.Owner,
                IdleLockMinutes = idleMinutes
            };
            RecoveryCode = await _recoveryCodeStore.CreateAsync();
            try
            {
                await _setupService.CompleteSetupAsync(
                    profile,
                    Licences.Select(licence => licence.ToEntity()),
                    owner,
                    OwnerSecret);
            }
            catch
            {
                await _recoveryCodeStore.DeleteAsync();
                RecoveryCode = string.Empty;
                throw;
            }

            SetupCompleted?.Invoke(owner, EventArgs.Empty);
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or
                                          ArgumentException or CryptographicException or PlatformNotSupportedException)
        {
            ErrorMessage = exception.Message;
        }
    }

    private static string? NullIfWhiteSpace(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
