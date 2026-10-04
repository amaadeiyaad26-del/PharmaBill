using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PharmaBill.App.Services;
using PharmaBill.App.ViewModels;
using PharmaBill.Data;
using PharmaBill.Data.Persistence;
using PharmaBill.Data.Services;
using PharmaBill.Sync;
using QuestPDF.Infrastructure;
using Serilog;

namespace PharmaBill.App;

public partial class App : Application
{
    private readonly string _logDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PharmaBill",
        "logs");

    private IHost? _host;
    private AuthenticationWindow? _loginWindow;

    protected override async void OnStartup(StartupEventArgs e)
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        try
        {
            Directory.CreateDirectory(_logDirectory);
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .WriteTo.File(Path.Combine(_logDirectory, "pharmabill-.log"),
                    rollingInterval: RollingInterval.Day)
                .CreateLogger();
            QuestPDF.Settings.License = LicenseType.Community;

            _host = Host.CreateDefaultBuilder()
                .UseSerilog()
                .ConfigureServices(services =>
                {
                    services.AddSingleton<IThemeService, ThemeService>();
                    services.AddSingleton<ILanguageService, LanguageService>();
                    services.AddSingleton<INavigationService, NavigationService>();
                    services.AddSingleton<SettingsPageViewModel>();
                    services.AddSingleton<CurrentSession>();
                    services.AddSingleton<IConfirmationService, ConfirmationService>();
                    services.AddSingleton<IPromptService, PromptService>();
                    services.AddSingleton<IAddStockDialogService, AddStockDialogService>();
                    services.AddTransient<AddStockViewModel>();
                    services.AddSingleton<IFilePickerService, FilePickerService>();
                    services.AddTransient<FirstRunWizardViewModel>();
                    services.AddTransient<AuthenticationViewModel>();
                    services.AddTransient<CatalogImportViewModel>();
                    services.AddTransient<DiagnosticsViewModel>();
                    services.AddTransient<FirstRunWizardWindow>();
                    services.AddTransient<AuthenticationWindow>();
                    services.AddTransient<CatalogImportWindow>();
                    services.AddTransient<DiagnosticsWindow>();
                    services.AddSingleton<LoginPreferenceStore>();
                    services.AddSingleton<RecoveryCodeStore>();
                    services.AddSingleton<OwnerRecoveryService>();
                    services.AddSingleton<CatalogSearchViewModel>();
                    services.AddSingleton<PurchaseSpreadsheetReader>();
                    services.AddSingleton<StockPageViewModel>();
                    services.AddSingleton<PurchasePageViewModel>();
                    services.AddTransient<WholesaleCustomerPageViewModel>();
                    services.AddSingleton<WholesaleInvoiceDocumentService>();
                    services.AddTransient<WholesaleBillingPageViewModel>();
                    services.AddTransient<WholesalePricingPageViewModel>();
                    services.AddTransient<WholesaleAccountsPageViewModel>();
                    services.AddTransient<WholesaleReturnsPageViewModel>();
                    services.AddTransient<StockInHandPageViewModel>();
                    services.AddTransient<WholesaleReportsPageViewModel>();
                    services.AddTransient<ReportsPageViewModel>();
                    services.AddTransient<DashboardPageViewModel>();
                    services.AddTransient<BackupPageViewModel>();
                    services.AddTransient<SyncSettingsPageViewModel>();
                    services.AddSingleton<RetailBillingViewModel>();
                    services.AddSingleton<RetailBillPdfService>();
                    services.AddSingleton<DocumentOutputSettingsStore>();
                    services.AddSingleton<AppVersionInfo>();
                    services.AddSingleton<SensitiveAccessService>();
                    services.AddSingleton<TabularExportService>();
                    services.AddSingleton<DrugRecordsService>();
                    services.AddSingleton<StatutoryRegisterService>();
                    services.AddTransient<RecentBillsViewModel>();
                    services.AddTransient<RecentBillsWindow>();
                    services.AddSingleton<MainWindowViewModel>();
                    services.AddSingleton<MainWindow>();
                    services.AddPharmaBillData();
                    services.AddPharmaBillSync();
                })
                .Build();

            await _host.StartAsync();
            var currentSession = _host.Services.GetRequiredService<CurrentSession>();
            bool setupRequired;
            using (var scope = _host.Services.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<PharmaBillDbContext>();
                setupRequired = !await context.PharmacyProfiles.AnyAsync();
            }

            await StartupFlow.RunAsync(
                setupRequired,
                ShowSetupAsync,
                () => currentSession.IsAuthenticated,
                ShowLoginAsync,
                shutdownWhenMainCloses =>
                {
                    var mainWindow = _host.Services.GetRequiredService<MainWindow>();
                    MainWindow = mainWindow;
                    mainWindow.Closed += (_, _) => shutdownWhenMainCloses();
                    mainWindow.Show();
                    mainWindow.Activate();
                    _loginWindow?.Close();
                    _loginWindow = null;
                },
                Shutdown);

            if (MainWindow is not MainWindow mainWindow || !mainWindow.IsVisible)
            {
                return;
            }

            var catalogPath = Path.Combine(AppContext.BaseDirectory, "Data", "medicine_catalog.csv");
            var infoPath = Path.Combine(AppContext.BaseDirectory, "Data", "medicine_info.csv");
            if (File.Exists(catalogPath) || File.Exists(infoPath))
            {
                using var scope = _host.Services.CreateScope();
                var importWindow = scope.ServiceProvider.GetRequiredService<CatalogImportWindow>();
                importWindow.Owner = mainWindow;
                importWindow.ShowDialog();
            }
        }
        catch (Exception exception)
        {
            Log.Fatal(exception, "The application could not start.");
            ShowError("PharmaBill could not start.", exception.ToString());
            Shutdown(-1);
        }
    }

    private Task<bool> ShowSetupAsync()
    {
        using var scope = _host!.Services.CreateScope();
        var setupWindow = scope.ServiceProvider.GetRequiredService<FirstRunWizardWindow>();
        setupWindow.Owner = null;
        return Task.FromResult(setupWindow.ShowDialog() == true);
    }

    private async Task<bool> ShowLoginAsync()
    {
        using var scope = _host!.Services.CreateScope();
        var loginWindow = scope.ServiceProvider.GetRequiredService<AuthenticationWindow>();
        _loginWindow = loginWindow;
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var viewModel = (AuthenticationViewModel)loginWindow.DataContext;
        viewModel.Authenticated += (_, _) => completion.TrySetResult(true);
        loginWindow.Closed += (_, _) => completion.TrySetResult(false);
        loginWindow.Show();
        loginWindow.Activate();
        return await completion.Task;
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }

        Log.CloseAndFlush();
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error(e.Exception, "An unhandled UI exception occurred.");
        ShowError("Something went wrong. Your data has not been intentionally changed.",
            e.Exception.ToString());
        e.Handled = true;
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            Log.Fatal(exception, "An unhandled application exception occurred.");
            ShowError("PharmaBill encountered an unrecoverable error.", exception.ToString());
        }
        else
        {
            Log.Fatal("An unhandled application exception occurred: {ExceptionObject}", e.ExceptionObject);
            ShowError("PharmaBill encountered an unrecoverable error.", e.ExceptionObject?.ToString() ?? "Unknown error.");
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Log.Error(e.Exception, "An unobserved task exception occurred.");
        ShowError("Something went wrong. Your data has not been intentionally changed.",
            e.Exception.ToString());
        e.SetObserved();
    }

    private int _errorWindowOpen;

    private void ShowError(string friendlyMessage, string details)
    {
        void Show()
        {
            // Only one error window at a time; follow-up errors are already in the log.
            if (Interlocked.Exchange(ref _errorWindowOpen, 1) == 1)
            {
                return;
            }

            try
            {
                new ErrorMessageWindow(friendlyMessage, details, _logDirectory)
                    { Owner = MainWindow?.IsVisible == true ? MainWindow : null }
                    .ShowDialog();
            }
            catch (Exception displayException)
            {
                Log.Error(displayException, "Could not show the application error window.");
                MessageBox.Show(
                    $"{friendlyMessage}{Environment.NewLine}{details}{Environment.NewLine}Logs: {_logDirectory}",
                    "PharmaBill",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                Interlocked.Exchange(ref _errorWindowOpen, 0);
            }
        }

        if (Dispatcher.CheckAccess())
        {
            Show();
        }
        else if (!Dispatcher.HasShutdownStarted && !Dispatcher.HasShutdownFinished)
        {
            Dispatcher.Invoke(Show);
        }
    }
}
