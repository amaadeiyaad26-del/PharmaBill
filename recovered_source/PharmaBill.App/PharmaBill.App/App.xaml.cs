using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PharmaBill.App.Services;
using PharmaBill.App.ViewModels;
using PharmaBill.Core.Ai;
using PharmaBill.Data;
using PharmaBill.Data.Persistence;
using PharmaBill.Data.Services;
using PharmaBill.Sync;
using QuestPDF;
using QuestPDF.Infrastructure;
using Serilog;
using Serilog.Events;

namespace PharmaBill.App;

public partial class App : Application
{
	private readonly string _logDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PharmaBill", "logs");

	private IHost? _host;

	private AuthenticationWindow? _loginWindow;

	private int _errorWindowOpen;

	protected override async void OnStartup(StartupEventArgs e)
	{
		ShutdownMode = ShutdownMode.OnExplicitShutdown;
		base.OnStartup(e);
		SoundHelper.Initialize();
		DispatcherUnhandledException += OnDispatcherUnhandledException;
		AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
		TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
		try
		{
			Directory.CreateDirectory(_logDirectory);
			Log.Logger = new LoggerConfiguration().MinimumLevel.Information().WriteTo.File(Path.Combine(_logDirectory, "pharmabill-.log"), LogEventLevel.Verbose, "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}", null, 1073741824L, null, buffered: false, shared: false, null, RollingInterval.Day, rollOnFileSizeLimit: false, 31).CreateLogger();
			Settings.License = LicenseType.Community;
			_host = Host.CreateDefaultBuilder().UseSerilog().ConfigureServices((IServiceCollection services) =>
			{
				services.AddSingleton<IThemeService, ThemeService>();
				services.AddSingleton<ILanguageService, LanguageService>();
				services.AddSingleton<INavigationService, NavigationService>();
				services.AddSingleton<SettingsPageViewModel>();
				services.AddSingleton<CurrentSession>();
				services.AddSingleton<IUserSessionService, UserSessionService>();
				services.AddSingleton<IConfirmationService, ConfirmationService>();
				services.AddSingleton<IPromptService, PromptService>();
				services.AddSingleton<IAddStockDialogService, AddStockDialogService>();
				services.AddSingleton<IInvoiceDetailDialogService, InvoiceDetailDialogService>();
				services.AddTransient<AddStockViewModel>();
				services.AddSingleton<IFilePickerService, FilePickerService>();
				services.AddSingleton<IOcrTextRecognizer, WindowsOcrTextRecognizer>();
				services.AddSingleton<IPrescriptionDialogService, PrescriptionDialogService>();
				services.AddSingleton<IAccountDialogService, AccountDialogService>();
				services.AddSingleton<IPaymentQrDialogService, PaymentQrDialogService>();
				services.AddTransient<InitialSetupViewModel>();
				services.AddTransient<AuthenticationViewModel>();
				services.AddTransient<CatalogImportViewModel>();
				services.AddTransient<DiagnosticsViewModel>();
				services.AddTransient<InitialSetupWindow>();
				services.AddTransient<AuthenticationWindow>();
				services.AddTransient<CatalogImportWindow>();
				services.AddTransient<DiagnosticsWindow>();
				services.AddTransient<CloudKeysWindow>();
				services.AddSingleton<SocialLoginCoordinator>();
				services.AddSingleton<LoginPreferenceStore>();
				services.AddSingleton<ActiveBillingDeskModeStore>();
				services.AddSingleton<RecoveryCodeStore>();
				services.AddSingleton<OwnerRecoveryService>();
				services.AddSingleton<CatalogSearchViewModel>();
				services.AddSingleton<PurchaseSpreadsheetReader>();
				services.AddSingleton<PurchasePageViewModel>();
				services.AddSingleton<StockPageViewModel>();
				services.AddSingleton<StockTransferPageViewModel>();
				services.AddTransient<WholesaleCustomerPageViewModel>();
				services.AddSingleton<WholesaleInvoiceDocumentService>();
				services.AddTransient<WholesaleBillingPageViewModel>();
				services.AddTransient<WholesalePricingPageViewModel>();
				services.AddTransient<WholesaleAccountsPageViewModel>();
				services.AddTransient<WholesaleReconciliationPageViewModel>();
				services.AddTransient<DunningDashboardPageViewModel>();
				services.AddTransient<WholesaleReturnsPageViewModel>();
				services.AddTransient<StockInHandPageViewModel>();
				services.AddTransient<WholesaleReportsPageViewModel>();
				services.AddTransient<ReportsPageViewModel>();
				services.AddTransient<GstReturnsPageViewModel>();
				services.AddTransient<DashboardPageViewModel>();
				services.AddTransient<BackupPageViewModel>();
				services.AddTransient<SyncSettingsPageViewModel>();
				services.AddSingleton<RetailBillingViewModel>();
				services.AddSingleton<RetailBillPdfService>();
				services.AddSingleton<IInvoicePrintService, InvoicePrintService>();
				services.AddSingleton<DocumentOutputSettingsStore>();
				services.AddSingleton<UserManualEmailQueueStore>();
				services.AddSingleton<IUserManualDialogService, UserManualDialogService>();
				services.AddSingleton<UpdateCheckPreferencesStore>();
				services.AddSingleton<IAppUpdateService, AppUpdateService>();
				services.AddSingleton<RegistrationNotificationService>();
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
				services.AddSingleton<ISubscriptionProvider, StoreSubscriptionProvider>();
				services.AddPharmaBillSync();
			})
				.Build();
			await _host.StartAsync();
			RegistrationNotificationService registrationNotifications = _host.Services.GetRequiredService<RegistrationNotificationService>();
			registrationNotifications.EnsureNetworkHook();
			Task.Run(async () =>
			{
				try
				{
					await registrationNotifications.FlushPendingAsync();
				}
				catch (Exception exception)
				{
					Log.Debug(exception, "Pending welcome-manual email flush skipped.");
				}
			});
			CurrentSession currentSession = _host.Services.GetRequiredService<CurrentSession>();
			bool setupRequired;
			using (IServiceScope scope = _host.Services.CreateScope())
			{
				setupRequired = await PharmacySetupProbe.IsInitialSetupRequiredAsync(scope.ServiceProvider.GetRequiredService<PharmaBillDbContext>());
			}
			await StartupFlow.RunAsync(setupRequired, ShowSetupAsync, () => currentSession.IsAuthenticated, ShowLoginAsync, (Action shutdownWhenMainCloses) =>
			{
				MainWindow mainWindow = (MainWindow)(MainWindow = _host.Services.GetRequiredService<MainWindow>());
				mainWindow.Closed += (object? _, EventArgs _) =>
				{
					shutdownWhenMainCloses();
				};
				mainWindow.Show();
				mainWindow.Activate();
				_loginWindow?.Close();
				_loginWindow = null;
				ScheduleBackgroundUpdateCheck();
			}, Shutdown);
		}
		catch (Exception ex)
		{
			Log.Fatal(ex, "The application could not start.");
			ShowError("PharmaBill could not start.", ex.ToString());
			Shutdown(-1);
		}
	}

	private void ScheduleBackgroundUpdateCheck()
	{
		if (_host == null)
		{
			return;
		}
		MainWindowViewModel mainVm = _host.Services.GetRequiredService<MainWindowViewModel>();
		Task.Run(async () =>
		{
			_ = 1;
			try
			{
				await Task.Delay(2500).ConfigureAwait(continueOnCapturedContext: false);
				await mainVm.CheckForAppUpdatesAsync().ConfigureAwait(continueOnCapturedContext: false);
			}
			catch (Exception exception)
			{
				Log.Debug(exception, "Startup update check skipped.");
			}
		});
	}

	private Task<bool> ShowSetupAsync()
	{
		using IServiceScope serviceScope = _host.Services.CreateScope();
		InitialSetupWindow requiredService = serviceScope.ServiceProvider.GetRequiredService<InitialSetupWindow>();
		requiredService.Owner = null;
		return Task.FromResult(requiredService.ShowDialog() == true);
	}

	private async Task<bool> ShowLoginAsync()
	{
		using IServiceScope scope = _host.Services.CreateScope();
		AuthenticationWindow authenticationWindow = (_loginWindow = scope.ServiceProvider.GetRequiredService<AuthenticationWindow>());
		TaskCompletionSource<bool> completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		((AuthenticationViewModel)authenticationWindow.DataContext).Authenticated += (object? _, EventArgs _) =>
		{
			completion.TrySetResult(result: true);
		};
		authenticationWindow.Closed += (object? _, EventArgs _) =>
		{
			completion.TrySetResult(result: false);
		};
		authenticationWindow.Show();
		authenticationWindow.Activate();
		return await completion.Task;
	}

	protected override async void OnExit(ExitEventArgs e)
	{
		if (_host != null)
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
		ShowError("Something went wrong. Your data has not been intentionally changed.", e.Exception.ToString());
		e.Handled = true;
	}

	private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
	{
		if (e.ExceptionObject is Exception ex)
		{
			Log.Fatal(ex, "An unhandled application exception occurred.");
			ShowError("PharmaBill encountered an unrecoverable error.", ex.ToString());
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
		ShowError("Something went wrong. Your data has not been intentionally changed.", e.Exception.ToString());
		e.SetObserved();
	}

	private void ShowError(string friendlyMessage, string details)
	{
		if (Dispatcher.CheckAccess())
		{
			Show();
		}
		else if (!Dispatcher.HasShutdownStarted && !Dispatcher.HasShutdownFinished)
		{
			Dispatcher.Invoke(Show);
		}
		void Show()
		{
			if (Interlocked.Exchange(ref _errorWindowOpen, 1) == 1)
			{
				return;
			}
			try
			{
				ErrorMessageWindow errorMessageWindow = new ErrorMessageWindow(friendlyMessage, details, _logDirectory);
				Window mainWindow = MainWindow;
				errorMessageWindow.Owner = ((mainWindow != null && mainWindow.IsVisible) ? MainWindow : null);
				errorMessageWindow.ShowDialog();
			}
			catch (Exception exception)
			{
				Log.Error(exception, "Could not show the application error window.");
				MessageBox.Show($"{friendlyMessage}{Environment.NewLine}{details}{Environment.NewLine}Logs: {_logDirectory}", "PharmaBill", MessageBoxButton.OK, MessageBoxImage.Hand);
			}
			finally
			{
				Interlocked.Exchange(ref _errorWindowOpen, 0);
			}
		}
	}
}
