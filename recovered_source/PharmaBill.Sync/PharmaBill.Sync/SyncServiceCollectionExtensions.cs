using System;
using Microsoft.Extensions.DependencyInjection;
using PharmaBill.Core.Services.Auth;

namespace PharmaBill.Sync;

public static class SyncServiceCollectionExtensions
{
	public static IServiceCollection AddPharmaBillSync(this IServiceCollection services)
	{
		services.AddSingleton<HybridLogicalClock>();
		services.AddSingleton<SyncPayloadSerializer>();
		services.AddSingleton<ProtectedPeerKeyStore>();
		services.AddSingleton<FileTransferQueue>();
		services.AddScoped<SyncDeviceService>();
		services.AddScoped<ChangeApplier>();
		services.AddScoped<SyncPackageService>();
		services.AddSingleton<SyncFolderSettingsStore>();
		services.AddSingleton<SyncFolderExchangeService>();
		services.AddHostedService<SyncFolderPollingService>();
		services.AddSingleton<CloudSyncSettingsStore>();
		services.AddSingleton<ICloudHttpClientProvider, DefaultCloudHttpClientProvider>();
		services.AddSingleton<CloudSyncService>();
		services.AddHostedService<CloudSyncWorker>();
		services.AddHostedService<SyncClockInitializationHostedService>();
		services.AddSingleton<LocalSyncServer>();
		services.AddSingleton<LanSyncMdnsAdvertiser>();
		services.AddSingleton<CloudOAuthClientStore>();
		services.AddSingleton<ISocialOAuthSettings, SocialOAuthSettingsProvider>();
		services.AddSingleton<SocialAuthService>();
		services.AddSingleton<GoogleDriveSyncSettingsStore>();
		services.AddSingleton<GoogleDriveSyncService>();
		services.AddSingleton<CloudAutoBackupPreferencesStore>();
		services.AddSingleton((Func<IServiceProvider, ICloudStorageService>)((IServiceProvider provider) => new GoogleDriveCloudStorageAdapter(provider.GetRequiredService<GoogleDriveSyncService>())));
		services.AddSingleton<CloudBackupOrchestrator>();
		services.AddSingleton<BranchNetworkSyncService>();
		services.AddSingleton<ZeroConfigSyncCoordinator>();
		services.AddHostedService<LocalSyncHostedService>();
		return services;
	}
}
