using System;
using System.IO;
using System.Net.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PharmaBill.Core.Ai;
using PharmaBill.Core.Security;
using PharmaBill.Core.Sync;
using PharmaBill.Data.Persistence;
using PharmaBill.Data.Services;
using PharmaBill.Data.Services.Dunning;
using PharmaBill.Data.Services.Reconciliation;
using SQLitePCL;

namespace PharmaBill.Data;

public static class ServiceCollectionExtensions
{
	public static IServiceCollection AddPharmaBillData(this IServiceCollection services)
	{
		Batteries_V2.Init();
		AppDataPaths.EnsureCreatedAndMigrateLegacyLayout();
		string text = AppDataPaths.RootDirectory;
		Guid value = LoadOrCreateDeviceId(Path.Combine(text, "device.id"));
		DatabaseStorageOptions databaseStorageOptions = new DatabaseStorageOptions(text);
		DatabaseEncryptionKeyProvider databaseEncryptionKeyProvider = new DatabaseEncryptionKeyProvider(databaseStorageOptions.KeyPath);
		byte[] orCreateKey = databaseEncryptionKeyProvider.GetOrCreateKey();
		string connectionString = new SqliteConnectionStringBuilder
		{
			DataSource = databaseStorageOptions.DatabasePath,
			Mode = SqliteOpenMode.ReadWriteCreate,
			Password = Convert.ToHexString(orCreateKey)
		}.ToString();
		services.AddSingleton(new DatabaseDeviceId(value));
		services.AddSingleton(databaseStorageOptions);
		services.AddSingleton(databaseEncryptionKeyProvider);
		services.AddSingleton<HybridLogicalClockState>();
		services.AddSingleton<BranchSettingsStore>();
		services.AddSingleton<SaveChangesAuditInterceptor>();
		services.AddDbContext<PharmaBillDbContext>((IServiceProvider provider, DbContextOptionsBuilder options) =>
		{
			options.UseSqlite(connectionString).AddInterceptors(provider.GetRequiredService<SaveChangesAuditInterceptor>());
		});
		services.AddScoped<IUnitOfWork, UnitOfWork>();
		services.AddScoped<NumberSeriesService>();
		services.AddScoped<BranchService>();
		services.AddScoped<DbInitializer>();
		services.AddSingleton<ProtectedAccessStateStore>();
		services.AddSingleton<ISubscriptionProvider, StubSubscriptionProvider>();
		services.AddSingleton<ILicenseRuntimeGuard, NullLicenseRuntimeGuard>();
		services.AddScoped<AccessService>();
		services.AddScoped((Func<IServiceProvider, IEntitlementService>)((IServiceProvider provider) => provider.GetRequiredService<AccessService>()));
		services.AddScoped<AuthorizationService>();
		services.AddScoped<PharmacySetupService>();
		services.AddScoped<BusinessModeService>();
		services.AddScoped<AuthenticationService>();
		services.AddScoped<SocialLoginService>();
		services.AddScoped<CatalogImportService>();
		services.AddSingleton<CatalogueService>();
		services.AddScoped<CatalogSearchService>();
		services.AddScoped<InventoryService>();
		services.AddScoped<ShortageIndentService>();
		services.AddScoped<StorageLocationService>();
		services.AddScoped<StockTransferService>();
		services.AddScoped<PurchaseService>();
		services.AddScoped<AddStockService>();
		services.AddScoped<StockVerificationService>();
		services.AddScoped<RetailBillingService>();
		services.AddScoped<OperatorAccountService>();
		services.AddScoped<WholesaleCustomerService>();
		services.AddScoped<WholesalePricingService>();
		services.AddScoped<WholesaleInvoiceService>();
		services.AddScoped<WholesaleAccountsService>();
		services.AddScoped<IReconciliationService, ReconciliationService>();
		services.AddScoped<IDunningService, DunningService>();
		services.AddScoped<WholesaleReturnsService>();
		services.AddScoped<StockInHandService>();
		services.AddScoped<WholesaleGstReportsService>();
		services.AddScoped<GstReturnExportService>();
		services.AddScoped<ReportsDashboardService>();
		services.AddSingleton<AiAssistantOptions>();
		services.AddScoped<IAiPharmacyAssistantService, LocalPharmacyAssistantService>();
		services.AddSingleton<BackupSettingsStore>();
		services.AddSingleton<UsbBackupSettingsStore>();
		services.AddSingleton<UsbBackupService>();
		services.AddSingleton<DataRetentionSettingsStore>();
		services.AddScoped<EncryptedBackupService>();
		services.AddScoped<DataRetentionArchiveService>();
		services.AddSingleton<GeminiApiKeyStore>();
		services.AddScoped<IPrescriptionOcrService, PrescriptionOcrService>();
		services.AddSingleton(new HttpClient());
		services.AddSingleton<GeminiPurchaseImportService>();
		services.AddHostedService<DatabaseInitializationHostedService>();
		services.AddHostedService<AutomaticBackupHostedService>();
		return services;
	}

	private static Guid LoadOrCreateDeviceId(string path)
	{
		if (File.Exists(path))
		{
			if (Guid.TryParse(File.ReadAllText(path), out var result))
			{
				return result;
			}
			throw new InvalidDataException("The stored device identifier is invalid.");
		}
		Guid result2 = Guid.NewGuid();
		try
		{
			using FileStream stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
			using StreamWriter streamWriter = new StreamWriter(stream);
			streamWriter.Write(result2.ToString("D"));
			return result2;
		}
		catch (IOException) when (File.Exists(path))
		{
			return LoadOrCreateDeviceId(path);
		}
	}
}
