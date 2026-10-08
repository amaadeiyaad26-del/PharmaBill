using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Sync;

public sealed class ZeroConfigSyncCoordinator
{
	private readonly LocalSyncServer _localSync;

	private readonly LanSyncMdnsAdvertiser _mdns;

	private readonly CloudBackupOrchestrator _cloudBackup;

	private readonly SyncFolderExchangeService _folderSync;

	private readonly GoogleDriveSyncService _googleDrive;

	private readonly DatabaseDeviceId _deviceId;

	private readonly ILogger<ZeroConfigSyncCoordinator> _logger;

	private int _cloudFallbackArmed;

	public bool IsCloudFallbackActive => Volatile.Read(in _cloudFallbackArmed) == 1;

	public bool IsDiscoverable => _mdns.IsAdvertising;

	public string? DiscoveryInstanceName => _mdns.InstanceName;

	public event Action? StateChanged;

	public ZeroConfigSyncCoordinator(LocalSyncServer localSync, LanSyncMdnsAdvertiser mdns, CloudBackupOrchestrator cloudBackup, SyncFolderExchangeService folderSync, GoogleDriveSyncService googleDrive, DatabaseDeviceId deviceId, ILogger<ZeroConfigSyncCoordinator> logger)
	{
		_localSync = localSync;
		_mdns = mdns;
		_cloudBackup = cloudBackup;
		_folderSync = folderSync;
		_googleDrive = googleDrive;
		_deviceId = deviceId;
		_logger = logger;
		_localSync.StatusChanged += OnLocalSyncStatusChanged;
	}

	public async Task InitializeAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		// Start the LAN listener first so Sync Station is available quickly.
		if (_localSync.Status.State == LocalSyncState.Stopped)
		{
			await _localSync.StartAsync(5055, cancellationToken).ConfigureAwait(false);
		}
		ApplyTransportForStatus(_localSync.Status);

		// Firewall + cloud/folder fallback are deferred so they never stall app startup.
		_ = Task.Run(() =>
		{
			try
			{
				if (OperatingSystem.IsWindows())
				{
					WindowsFirewallPortOpener.Result result = WindowsFirewallPortOpener.EnsureAllowRule();
					WindowsFirewallPortOpener.LogResult(_logger, result);
				}
			}
			catch (Exception exception)
			{
				_logger.LogDebug(exception, "Deferred firewall rule check skipped.");
			}
		}, CancellationToken.None);

		_ = Task.Run(async () =>
		{
			try
			{
				await MaybeRunCloudFallbackAsync(triggeredByBillSave: false, CancellationToken.None).ConfigureAwait(false);
			}
			catch (Exception exception)
			{
				_logger.LogDebug(exception, "Deferred cloud/folder fallback skipped.");
			}
		}, CancellationToken.None);
	}

	public async Task OnBillOrLedgerSavedAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		await PublishPendingChangesQuietlyAsync(cancellationToken);
		await _cloudBackup.UploadToSelectedProvidersAsync(triggeredByBillSave: true, cancellationToken, forceWhenConnected: true);
		bool flag = IsCloudFallbackActive;
		if (!flag)
		{
			LocalSyncState state = _localSync.Status.State;
			bool flag2 = ((state == LocalSyncState.Stopped || state == LocalSyncState.Unavailable) ? true : false);
			flag = flag2;
		}
		if (flag)
		{
			await MaybeRunCloudFallbackAsync(triggeredByBillSave: true, cancellationToken);
		}
	}

	public async Task ShutdownAsync()
	{
		_mdns.Stop();
		await _localSync.StopAsync();
	}

	private void OnLocalSyncStatusChanged(LocalSyncStatus status)
	{
		ApplyTransportForStatus(status);
		StateChanged?.Invoke();
	}

	private void ApplyTransportForStatus(LocalSyncStatus status)
	{
		LocalSyncState state = status.State;
		if ((uint)(state - 1) <= 1u)
		{
			Volatile.Write(ref _cloudFallbackArmed, 0);
			if (!_mdns.IsAdvertising)
			{
				string machineName = Environment.MachineName;
				LanSyncMdnsAdvertiser mdns = _mdns;
				string deviceDisplayName = machineName;
				int port;
				if (status.State != LocalSyncState.Listening && _localSync.Port <= 0)
				{
					port = 5055;
				}
				else
				{
					port = ((_localSync.Port > 0) ? _localSync.Port : 5055);
				}
				mdns.Start(deviceDisplayName, port, _deviceId.Value);
			}
		}
		else
		{
			_mdns.Stop();
			Volatile.Write(ref _cloudFallbackArmed, 1);
			_logger.LogInformation("LAN sync unavailable ({State}); arming cloud / folder sync fallback.", status.State);
		}
		StateChanged?.Invoke();
	}

	private async Task MaybeRunCloudFallbackAsync(bool triggeredByBillSave, CancellationToken cancellationToken)
	{
		if (!IsCloudFallbackActive && !triggeredByBillSave)
		{
			return;
		}
		try
		{
			SyncFolderSettings syncFolderSettings = await _folderSync.GetSettingsAsync(cancellationToken);
			if (!string.IsNullOrWhiteSpace(syncFolderSettings.FolderPath))
			{
				if (!syncFolderSettings.AutoSyncEnabled)
				{
					await _folderSync.SetAutoSyncAsync(enabled: true, cancellationToken);
				}
				await _folderSync.SyncNowAsync(cancellationToken);
			}
		}
		catch (Exception exception)
		{
			_logger.LogDebug(exception, "Folder sync fallback skipped.");
		}
		try
		{
			if ((await _googleDrive.GetSettingsAsync(cancellationToken)).IsConnected)
			{
				await _cloudBackup.UploadToSelectedProvidersAsync(triggeredByBillSave, cancellationToken, forceWhenConnected: true);
			}
		}
		catch (Exception exception2)
		{
			_logger.LogDebug(exception2, "Drive sync fallback skipped.");
		}
	}

	private async Task PublishPendingChangesQuietlyAsync(CancellationToken cancellationToken)
	{
		_ = 3;
		try
		{
			SyncFolderSettings syncFolderSettings = await _folderSync.GetSettingsAsync(cancellationToken);
			if (string.IsNullOrWhiteSpace(syncFolderSettings.FolderPath))
			{
				string text = SyncFolderSettingsStore.FindDefaultFolder();
				if (!string.IsNullOrWhiteSpace(text))
				{
					await _folderSync.ConfigureAsync(text, autoSyncEnabled: true, cancellationToken);
					syncFolderSettings = await _folderSync.GetSettingsAsync(cancellationToken);
				}
			}
			if (!string.IsNullOrWhiteSpace(syncFolderSettings.FolderPath))
			{
				await _folderSync.SyncNowAsync(cancellationToken);
			}
		}
		catch (Exception exception)
		{
			_logger.LogDebug(exception, "Pending change publish skipped.");
		}
	}
}
