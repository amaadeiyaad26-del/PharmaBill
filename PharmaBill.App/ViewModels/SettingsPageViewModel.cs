using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using PharmaBill.App.Services;
using System.Collections.ObjectModel;
using Microsoft.Win32;
using PharmaBill.Core.Entities;
using PharmaBill.Core.Security;
using PharmaBill.Data.Services;

namespace PharmaBill.App.ViewModels;

public partial class SettingsPageViewModel : SectionPageViewModel
{
    private readonly IThemeService _themeService;
    private readonly ILanguageService _languageService;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly CurrentSession _currentSession;
    private readonly IConfirmationService _confirmationService;
    private readonly GeminiApiKeyStore _geminiApiKeyStore;
    private readonly DocumentOutputSettingsStore _documentOutputSettingsStore;
    private DocumentOutputSettings _documentOutputSettings;
    private readonly AppVersionInfo _versionInfo;

    public SettingsPageViewModel(
        IThemeService themeService,
        ILanguageService languageService,
        IServiceScopeFactory scopeFactory,
        CurrentSession currentSession,
        IConfirmationService confirmationService,
        GeminiApiKeyStore geminiApiKeyStore,
        DocumentOutputSettingsStore documentOutputSettingsStore,
        AppVersionInfo versionInfo)
        : base(languageService.GetString("NavSettings"))
    {
        _themeService = themeService;
        _languageService = languageService;
        _scopeFactory = scopeFactory;
        _currentSession = currentSession;
        _confirmationService = confirmationService;
        _geminiApiKeyStore = geminiApiKeyStore;
        _documentOutputSettingsStore = documentOutputSettingsStore;
        _versionInfo = versionInfo;
        _documentOutputSettings = documentOutputSettingsStore.Load();
        ThemeOptions = themeService.ThemeOptions;
        AccentOptions = themeService.AccentOptions;
        LanguageOptions = languageService.LanguageOptions;
        BusinessModes = Enum.GetValues<BusinessMode>();
        SelectedTheme = "Light";
        SelectedAccent = "Blue";
        SelectedLanguage = "English";
        _languageService.LanguageChanged += OnLanguageChanged;
        PaperSizeOptions = Enum.GetValues<DocumentPaperSize>();
        PrinterOptions = LoadPrinters();
        RetailMemoPaperSize = _documentOutputSettings.RetailMemoPaperSize;
        RetailInvoicePaperSize = _documentOutputSettings.RetailInvoicePaperSize;
        RetailMemoPrinter = _documentOutputSettings.RetailMemoPrinter ?? DefaultPrinterOption;
        RetailInvoicePrinter = _documentOutputSettings.RetailInvoicePrinter ?? DefaultPrinterOption;
        FooterText = _documentOutputSettings.FooterText;
        LogoPath = _documentOutputSettings.LogoPath ?? string.Empty;
        WhatsAppNumber = _documentOutputSettings.WhatsAppNumber;
        SmtpHost = _documentOutputSettings.SmtpHost;
        SmtpPort = _documentOutputSettings.SmtpPort;
        SmtpFromAddress = _documentOutputSettings.SmtpFromAddress;
        SmtpUserName = _documentOutputSettings.SmtpUserName;
        SmtpEnableSsl = _documentOutputSettings.SmtpEnableSsl;
        CashDrawerPulse = _documentOutputSettings.CashDrawerPulse;
    }

    public string VersionText => _versionInfo.Display;

    [ObservableProperty]
    private string _updateStatus = string.Empty;

    [ObservableProperty]
    private string _releaseNotesText = string.Empty;

    [ObservableProperty]
    private bool _isReleaseNotesVisible;

    [RelayCommand]
    private void CheckForUpdates() =>
        // TODO: query the release feed once a download location exists; the app must keep working offline.
        UpdateStatus = $"You have {_versionInfo.Display}. Automatic update checking is not set up yet; install newer versions from the PharmaBill installer.";

    [RelayCommand]
    private void ToggleReleaseNotes()
    {
        if (!IsReleaseNotesVisible)
        {
            ReleaseNotesText = _versionInfo.LoadReleaseNotes();
        }

        IsReleaseNotesVisible = !IsReleaseNotesVisible;
    }

    public IReadOnlyList<string> ThemeOptions { get; }

    public IReadOnlyList<string> AccentOptions { get; }

    public IReadOnlyList<string> LanguageOptions { get; }

    public IReadOnlyList<BusinessMode> BusinessModes { get; }

    public event EventHandler? BusinessModeChanged;

    private const string DefaultPrinterOption = "(Windows default)";

    public IReadOnlyList<DocumentPaperSize> PaperSizeOptions { get; }

    public ObservableCollection<string> PrinterOptions { get; }

    [ObservableProperty]
    private string _geminiApiKeyInput = string.Empty;

    [ObservableProperty]
    private string _geminiApiKeyStatus = string.Empty;

    [ObservableProperty]
    private DocumentPaperSize _retailMemoPaperSize;

    [ObservableProperty]
    private DocumentPaperSize _retailInvoicePaperSize;

    [ObservableProperty]
    private string _retailMemoPrinter = DefaultPrinterOption;

    [ObservableProperty]
    private string _retailInvoicePrinter = DefaultPrinterOption;

    [ObservableProperty]
    private string _footerText = string.Empty;

    [ObservableProperty]
    private string _logoPath = string.Empty;

    [ObservableProperty]
    private string _whatsAppNumber = string.Empty;

    [ObservableProperty]
    private string _smtpHost = string.Empty;

    [ObservableProperty]
    private int _smtpPort = 587;

    [ObservableProperty]
    private string _smtpFromAddress = string.Empty;

    [ObservableProperty]
    private string _smtpUserName = string.Empty;

    [ObservableProperty]
    private string _smtpPassword = string.Empty;

    [ObservableProperty]
    private bool _smtpEnableSsl = true;

    [ObservableProperty]
    private bool _cashDrawerPulse;

    [ObservableProperty]
    private string _documentOutputStatus = string.Empty;

    [RelayCommand]
    private void PickLogo()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select pharmacy logo",
            Filter = "Image files (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog() == true)
        {
            LogoPath = dialog.FileName;
        }
    }

    [RelayCommand]
    private void SaveDocumentOutputSettings()
    {
        if (!string.IsNullOrWhiteSpace(InspectorPinInput))
        {
            if (_currentSession.User?.Role != UserRole.Owner)
            {
                DocumentOutputStatus = "Only the owner can set the separate inspector PIN.";
                return;
            }

            if (InspectorPinInput.Length < 4 || InspectorPinInput != InspectorPinConfirmation)
            {
                DocumentOutputStatus = "Inspector PINs must match and contain at least four characters.";
                return;
            }

            _documentOutputSettingsStore.SaveInspectorPin(InspectorPinInput);
        }

        _documentOutputSettings = new DocumentOutputSettings
        {
            RetailMemoPaperSize = RetailMemoPaperSize,
            RetailInvoicePaperSize = RetailInvoicePaperSize,
            RetailMemoPrinter = RetailMemoPrinter == DefaultPrinterOption ? null : RetailMemoPrinter,
            RetailInvoicePrinter = RetailInvoicePrinter == DefaultPrinterOption ? null : RetailInvoicePrinter,
            FooterText = FooterText.Trim(),
            LogoPath = string.IsNullOrWhiteSpace(LogoPath) ? null : LogoPath,
            WhatsAppNumber = WhatsAppNumber.Trim(),
            SmtpHost = SmtpHost.Trim(),
            SmtpPort = SmtpPort,
            SmtpFromAddress = SmtpFromAddress.Trim(),
            SmtpUserName = SmtpUserName.Trim(),
            SmtpEnableSsl = SmtpEnableSsl,
            CashDrawerPulse = CashDrawerPulse,
            InspectorPinHash = string.IsNullOrWhiteSpace(InspectorPinInput)
                ? _documentOutputSettings.InspectorPinHash
                : null
        };
        if (!string.IsNullOrWhiteSpace(InspectorPinInput))
        {
            _documentOutputSettings.InspectorPinHash =
                _documentOutputSettingsStore.Load().InspectorPinHash;
        }

        _documentOutputSettingsStore.Save(_documentOutputSettings);
        _documentOutputSettingsStore.SaveSmtpPassword(SmtpPassword);
        SmtpPassword = string.Empty;
        InspectorPinInput = string.Empty;
        InspectorPinConfirmation = string.Empty;
        DocumentOutputStatus = "Document layouts, printers and sharing settings saved.";
    }

    [ObservableProperty]
    private string _inspectorPinInput = string.Empty;

    [ObservableProperty]
    private string _inspectorPinConfirmation = string.Empty;

    [RelayCommand]
    private void RemoveSmtpPassword()
    {
        _documentOutputSettingsStore.DeleteSmtpPassword();
        SmtpPassword = string.Empty;
        DocumentOutputStatus = "Saved SMTP password removed.";
    }

    private static ObservableCollection<string> LoadPrinters()
    {
        var printers = new ObservableCollection<string> { DefaultPrinterOption };
        using var printServer = new System.Printing.LocalPrintServer();
        foreach (var printer in printServer.GetPrintQueues())
        {
            if (!printers.Contains(printer.Name, StringComparer.OrdinalIgnoreCase))
            {
                printers.Add(printer.Name);
            }
        }

        return printers;
    }

    [RelayCommand]
    private void SaveGeminiApiKey()
    {
        if (string.IsNullOrWhiteSpace(GeminiApiKeyInput))
        {
            GeminiApiKeyStatus = "Enter a Gemini API key.";
            return;
        }

        _geminiApiKeyStore.Save(GeminiApiKeyInput);
        GeminiApiKeyInput = string.Empty;
        GeminiApiKeyStatus = "Gemini API key saved to this Windows user profile.";
    }

    [RelayCommand]
    private void RemoveGeminiApiKey()
    {
        _geminiApiKeyStore.Delete();
        GeminiApiKeyInput = string.Empty;
        GeminiApiKeyStatus = "Gemini API key removed.";
    }

    [ObservableProperty]
    private BusinessMode _selectedBusinessMode = BusinessMode.Retail;

    [ObservableProperty]
    private string _modeError = string.Empty;

    [RelayCommand]
    private async Task ChangeBusinessModeAsync()
    {
        ModeError = string.Empty;
        if (_currentSession.User is null ||
            !PermissionMatrix.Allows(_currentSession.User.Role, AppPermission.ChangeBusinessMode))
        {
            ModeError = "Only the owner or manager can change the business mode.";
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var modeService = scope.ServiceProvider.GetRequiredService<BusinessModeService>();
        var missing = await modeService.GetMissingRequirementsAsync(SelectedBusinessMode);
        if (missing.Count > 0)
        {
            ModeError = $"Mode change blocked. Missing: {string.Join(", ", missing)}.";
            return;
        }

        if (!_confirmationService.Confirm(
                $"Change the pharmacy business mode to {SelectedBusinessMode}? This will be recorded in the audit log.",
                "Confirm business mode change"))
        {
            return;
        }

        try
        {
            await modeService.ChangeModeAsync(
                SelectedBusinessMode,
                _currentSession.User.Id);
            BusinessModeChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception) when (exception is InvalidOperationException or UnauthorizedAccessException)
        {
            ModeError = exception.Message;
        }
    }

    [ObservableProperty]
    private string _selectedTheme;

    [ObservableProperty]
    private string _selectedAccent;

    [ObservableProperty]
    private string _selectedLanguage;

    partial void OnSelectedThemeChanged(string value) => _themeService.ApplyTheme(value);

    partial void OnSelectedAccentChanged(string value) => _themeService.ApplyAccent(value);

    partial void OnSelectedLanguageChanged(string value) => _languageService.ApplyLanguage(value);

    private void OnLanguageChanged(object? sender, EventArgs e) =>
        Title = _languageService.GetString("NavSettings");
}

