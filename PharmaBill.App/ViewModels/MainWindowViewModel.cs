using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using PharmaBill.App.Services;
using PharmaBill.Core.Entities;
using PharmaBill.Core.Security;
using PharmaBill.Data.Persistence;
using PharmaBill.Data.Services;
using PharmaBill.Sync;

namespace PharmaBill.App.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly INavigationService _navigationService;
    private readonly ILanguageService _languageService;
    private readonly SettingsPageViewModel _settingsPage;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly CurrentSession _currentSession;
    private readonly ILogger<MainWindowViewModel> _logger;
    private readonly SyncFolderExchangeService _folderSync;
    private readonly CatalogSearchViewModel _catalogSearch;
    private readonly StockPageViewModel _stockPage;
    private readonly PurchasePageViewModel _purchasePage;
    private readonly WholesaleCustomerPageViewModel _wholesaleCustomerPage;
    private readonly WholesaleBillingPageViewModel _wholesaleBillingPage;
    private readonly WholesalePricingPageViewModel _wholesalePricingPage;
    private readonly WholesaleAccountsPageViewModel _wholesaleAccountsPage;
    private readonly WholesaleReturnsPageViewModel _wholesaleReturnsPage;
    private readonly StockInHandPageViewModel _stockInHandPage;
    private readonly WholesaleReportsPageViewModel _wholesaleReportsPage;
    private readonly ReportsPageViewModel _reportsPage;
    private readonly DashboardPageViewModel _dashboardPage;
    private readonly BackupPageViewModel _backupPage;
    private readonly SyncSettingsPageViewModel _syncPage;
    private readonly RetailBillingViewModel _retailBillingPage;
    private readonly IFilePickerService _filePickerService;
    private readonly TabularExportService _tabularExportService;
    private readonly DrugRecordsService _drugRecordsService;
    private readonly StatutoryRegisterService _statutoryRegisterService;
    private readonly SensitiveAccessService _sensitiveAccessService;
    private readonly IConfirmationService _confirmationService;
    private readonly DispatcherTimer _clockTimer;
    private BusinessMode _businessMode;

    public MainWindowViewModel(
        INavigationService navigationService,
        ILanguageService languageService,
        SettingsPageViewModel settingsPage,
        IServiceScopeFactory scopeFactory,
        CurrentSession currentSession,
        CatalogSearchViewModel catalogSearch,
        StockPageViewModel stockPage,
        PurchasePageViewModel purchasePage,
        WholesaleCustomerPageViewModel wholesaleCustomerPage,
        WholesaleBillingPageViewModel wholesaleBillingPage,
        WholesalePricingPageViewModel wholesalePricingPage,
        WholesaleAccountsPageViewModel wholesaleAccountsPage,
        WholesaleReturnsPageViewModel wholesaleReturnsPage,
        StockInHandPageViewModel stockInHandPage,
        WholesaleReportsPageViewModel wholesaleReportsPage,
        ReportsPageViewModel reportsPage,
        DashboardPageViewModel dashboardPage,
        BackupPageViewModel backupPage,
        SyncSettingsPageViewModel syncPage,
        RetailBillingViewModel retailBillingPage,
        IFilePickerService filePickerService,
        TabularExportService tabularExportService,
        DrugRecordsService drugRecordsService,
        StatutoryRegisterService statutoryRegisterService,
        SensitiveAccessService sensitiveAccessService,
        IConfirmationService confirmationService,
        SyncFolderExchangeService folderSync,
        ILogger<MainWindowViewModel> logger)
    {
        _navigationService = navigationService;
        _languageService = languageService;
        _settingsPage = settingsPage;
        _scopeFactory = scopeFactory;
        _currentSession = currentSession;
        _catalogSearch = catalogSearch;
        _stockPage = stockPage;
        _purchasePage = purchasePage;
        _wholesaleCustomerPage = wholesaleCustomerPage;
        _wholesaleBillingPage = wholesaleBillingPage;
        _wholesalePricingPage = wholesalePricingPage;
        _wholesaleAccountsPage = wholesaleAccountsPage;
        _wholesaleReturnsPage = wholesaleReturnsPage;
        _stockInHandPage = stockInHandPage;
        _wholesaleReportsPage = wholesaleReportsPage;
        _reportsPage = reportsPage;
        _dashboardPage = dashboardPage;
        _backupPage = backupPage;
        _syncPage = syncPage;
        _retailBillingPage = retailBillingPage;
        _filePickerService = filePickerService;
        _tabularExportService = tabularExportService;
        _drugRecordsService = drugRecordsService;
        _statutoryRegisterService = statutoryRegisterService;
        _sensitiveAccessService = sensitiveAccessService;
        _confirmationService = confirmationService;
        _folderSync = folderSync;
        _logger = logger;
        CurrentPage = CreatePage(navigationService.CurrentSectionKey);
        UpdateSearchVisibility(navigationService.CurrentSectionKey);
        FlowDirection = languageService.FlowDirection;
        UpdateClock();
        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _clockTimer.Tick += OnClockTick;
        _clockTimer.Start();
        _navigationService.SectionChanged += OnSectionChanged;
        _languageService.LanguageChanged += OnLanguageChanged;
        _settingsPage.BusinessModeChanged += OnBusinessModeChanged;
        _folderSync.SettingsChanged += OnFolderSyncSettingsChanged;
        _catalogSearch.StockChanged += OnCatalogStockChanged;
        _catalogSearch.ViewStockRequested += OnViewStockRequested;
    }

    private async void OnCatalogStockChanged(object? sender, EventArgs e)
    {
        try
        {
            await _stockPage.LoadAsync();
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Refreshing stock after adding stock failed.");
        }
    }

    private async void OnViewStockRequested(object? sender, string medicineName)
    {
        try
        {
            if (!ReferenceEquals(CurrentPage, _stockPage))
            {
                Navigate("Stock");
            }

            await _stockPage.ShowMedicineAsync(medicineName);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Showing stock for {Medicine} failed.", medicineName);
        }
    }

    private void OnFolderSyncSettingsChanged(SyncFolderSettings settings)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            FolderSyncWarning = settings.LastError ?? string.Empty;
        }
        else
        {
            dispatcher.BeginInvoke(() => FolderSyncWarning = settings.LastError ?? string.Empty);
        }
    }

    [ObservableProperty]
    private object _currentPage;

    [ObservableProperty]
    private FlowDirection _flowDirection;

    [ObservableProperty]
    private string _localDateTime = string.Empty;

    [ObservableProperty]
    private string _pharmacyName = "Your Pharmacy";

    [ObservableProperty]
    private string _loggedInUser = string.Empty;

    [ObservableProperty]
    private string _modeStatus = string.Empty;

    [ObservableProperty]
    private string _trialStatus = string.Empty;

    [ObservableProperty]
    private bool _isReadOnly;

    [ObservableProperty]
    private string _folderSyncWarning = string.Empty;

    public bool HasFolderSyncWarning => !string.IsNullOrWhiteSpace(FolderSyncWarning);

    partial void OnFolderSyncWarningChanged(string value) =>
        OnPropertyChanged(nameof(HasFolderSyncWarning));

    public string ReadOnlyBanner => "READ-ONLY MODE — trial or required licence expired. Viewing, searching, printing, exporting and backup are still available.";

    public ObservableCollection<NavigationItem> NavigationItems { get; } = [];

    public CatalogSearchViewModel CatalogSearch => _catalogSearch;

    public bool IsDebugBuild
    {
        get
        {
#if DEBUG
            return true;
#else
            return false;
#endif
        }
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var syncSettings = await _folderSync.GetSettingsAsync(cancellationToken);
        FolderSyncWarning = syncSettings.LastError ?? string.Empty;
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<PharmaBillDbContext>();
        var profile = await context.PharmacyProfiles.SingleAsync(cancellationToken);
        PharmacyName = profile.Name;
        LoggedInUser = _currentSession.User?.DisplayName ?? string.Empty;
        _businessMode = profile.BusinessMode;
        ApplyMode(profile.BusinessMode);
        _settingsPage.SelectedBusinessMode = profile.BusinessMode;

        var status = await scope.ServiceProvider.GetRequiredService<AccessService>()
            .GetStatusAsync(cancellationToken);
        TrialStatus = status switch
        {
            EntitlementStatus.TRIAL => "TRIAL",
            EntitlementStatus.SUBSCRIBED => "SUBSCRIBED",
            EntitlementStatus.GRACE_OFFLINE => "GRACE OFFLINE",
            _ => "EXPIRED"
        };
        IsReadOnly = status == EntitlementStatus.EXPIRED;
        await LoadCurrentPageAsync();
    }

    public void RefreshLoggedInUser() => LoggedInUser = _currentSession.User?.DisplayName ?? string.Empty;

    [RelayCommand]
    private void Navigate(string? sectionKey)
    {
        if (string.IsNullOrWhiteSpace(sectionKey))
        {
            throw new ArgumentException("A navigation section must be provided.", nameof(sectionKey));
        }

        _navigationService.Navigate(sectionKey);
    }

    private async void OnSectionChanged(object? sender, EventArgs e)
    {
        var section = _navigationService.CurrentSectionKey;
        CurrentPage = CreatePage(section);
        UpdateSearchVisibility(section);
        try
        {
            await LoadCurrentPageAsync();
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Loading page {Section} failed.", section);
        }
    }

    private void UpdateSearchVisibility(string section) =>
        _catalogSearch.IsVisible = section is "Billing" or "WholesaleBilling" or "Stock" or "Purchases";

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        FlowDirection = _languageService.FlowDirection;
        if (CurrentPage is SectionPageViewModel page && CurrentPage is not SettingsPageViewModel)
        {
            page.Title = _languageService.GetString($"Nav{_navigationService.CurrentSectionKey}");
        }
    }

    private async void OnBusinessModeChanged(object? sender, EventArgs e)
    {
        try
        {
            await InitializeAsync();
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Refreshing the application after a business-mode change failed.");
        }
    }

    private async void OnClockTick(object? sender, EventArgs e)
    {
        UpdateClock();
        try
        {
            await RefreshAccessStatusAsync();
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Refreshing entitlement status failed.");
        }
    }

    private async Task RefreshAccessStatusAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var status = await scope.ServiceProvider.GetRequiredService<AccessService>().GetStatusAsync();
        TrialStatus = status switch
        {
            EntitlementStatus.TRIAL => "TRIAL",
            EntitlementStatus.SUBSCRIBED => "SUBSCRIBED",
            EntitlementStatus.GRACE_OFFLINE => "GRACE OFFLINE",
            _ => "EXPIRED"
        };
        IsReadOnly = status == EntitlementStatus.EXPIRED;
    }

    private void ApplyMode(BusinessMode mode)
    {
        ModeStatus = mode switch
        {
            BusinessMode.Retail => "RETAIL",
            BusinessMode.Wholesaler => "WHOLESALER",
            _ => "RETAIL + WHOLESALE"
        };
        NavigationItems.Clear();
        NavigationItems.Add(new NavigationItem("Dashboard", "Dashboard"));
        if (mode == BusinessMode.Wholesaler &&
            _navigationService.CurrentSectionKey is "Billing" or "Registers" or "DrugRecords")
        {
            _navigationService.Navigate("WholesaleBilling");
        }
        if (mode != BusinessMode.Wholesaler)
        {
            NavigationItems.Add(new NavigationItem("Billing", _languageService.GetString("NavBilling")));
        }
        if (mode != BusinessMode.Retail)
        {
            NavigationItems.Add(new NavigationItem(
                "WholesaleBilling",
                _languageService.GetString("NavWholesaleSales")));
        }

        NavigationItems.Add(new NavigationItem("Stock", _languageService.GetString("NavStock")));
        NavigationItems.Add(new NavigationItem("Purchases", _languageService.GetString("NavPurchases")));
        NavigationItems.Add(new NavigationItem("Customers", _languageService.GetString("NavCustomers")));
        if (mode != BusinessMode.Retail)
        {
            NavigationItems.Add(new NavigationItem("Pricing", "Pack levels & pricing"));
            NavigationItems.Add(new NavigationItem("Accounts", "Accounts"));
            NavigationItems.Add(new NavigationItem("Returns", "Returns & credit notes"));
            NavigationItems.Add(new NavigationItem("StockInHand", "Stock in hand"));
        }
        if (mode != BusinessMode.Wholesaler)
        {
            NavigationItems.Add(new NavigationItem("Registers", _languageService.GetString("NavRegisters")));
            NavigationItems.Add(new NavigationItem("DrugRecords", _languageService.GetString("NavDrugRecords")));
        }

        NavigationItems.Add(new NavigationItem("Reports", _languageService.GetString("NavReports")));
        NavigationItems.Add(new NavigationItem("Backup", "Backup & move PC"));
        NavigationItems.Add(new NavigationItem("Sync", "Sync"));
        NavigationItems.Add(new NavigationItem("Inspector", _languageService.GetString("NavInspector")));
    }

    private object CreatePage(string sectionKey) =>
        sectionKey switch
        {
            "Dashboard" => _dashboardPage,
            "Backup" => _backupPage,
            "Sync" => _syncPage,
            "Settings" => _settingsPage,
            "Stock" => _stockPage,
            "Purchases" => _purchasePage,
            "Customers" => _wholesaleCustomerPage,
            "WholesaleBilling" => _wholesaleBillingPage,
            "Pricing" => _wholesalePricingPage,
            "Accounts" => _wholesaleAccountsPage,
            "Returns" => _wholesaleReturnsPage,
            "StockInHand" => _stockInHandPage,
            "Billing" => _retailBillingPage,
            "Reports" => _reportsPage,
            "DrugRecords" => new DrugRecordsPageViewModel(
                _drugRecordsService,
                _filePickerService,
                _tabularExportService,
                _sensitiveAccessService,
                _confirmationService,
                _currentSession),
            "Registers" => new StatutoryRegistersPageViewModel(
                _statutoryRegisterService,
                _sensitiveAccessService,
                _confirmationService,
                _filePickerService,
                _tabularExportService,
                _currentSession),
            "Inspector" => new InspectorPageViewModel(
                _scopeFactory,
                _sensitiveAccessService),
            _ => new SectionPageViewModel(_languageService.GetString(
                sectionKey == "WholesaleBilling" ? "NavWholesaleSales" : $"Nav{sectionKey}"))
        };

    private Task LoadCurrentPageAsync() => CurrentPage is ILoadablePage loadable
        ? loadable.LoadAsync()
        : Task.CompletedTask;

    private void UpdateClock() => LocalDateTime = DateTime.Now.ToString("f");
}

public sealed record NavigationItem(string SectionKey, string Title);
