using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PharmaBill.Core.Entities;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Sync;

public sealed class SyncFolderExchangeService(IServiceScopeFactory scopeFactory, SyncFolderSettingsStore settingsStore, ILogger<SyncFolderExchangeService> logger)
{
	private sealed record StableFileObservation(long Length, DateTime LastWriteTimeUtc, int StableObservations = 0);

	private sealed record IncomingResult(int Packages, int Applied, string? Warning);

	private sealed record OutgoingResult(int Packages, string? Warning = null);

	public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(60L);

	public static readonly TimeSpan OutgoingInterval = TimeSpan.FromMinutes(10L);

	private readonly SemaphoreSlim _exchangeGate = new SemaphoreSlim(1, 1);

	private readonly ConcurrentDictionary<string, StableFileObservation> _observations = new ConcurrentDictionary<string, StableFileObservation>(StringComparer.OrdinalIgnoreCase);

	public event Action<SyncFolderSettings>? SettingsChanged;

	public Task<SyncFolderSettings> GetSettingsAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		return settingsStore.LoadAsync(cancellationToken);
	}

	public async Task ConfigureAsync(string folderPath, bool autoSyncEnabled, CancellationToken cancellationToken = default(CancellationToken))
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(folderPath, "folderPath");
		string fullPath = Path.GetFullPath(folderPath);
		SyncFolderSettings settings = await settingsStore.LoadAsync(cancellationToken)with
		{
			FolderPath = fullPath,
			AutoSyncEnabled = autoSyncEnabled
		};
		await SaveSettingsAsync(settings, cancellationToken);
	}

	public async Task SetAutoSyncAsync(bool enabled, CancellationToken cancellationToken = default(CancellationToken))
	{
		SyncFolderSettings syncFolderSettings = await settingsStore.LoadAsync(cancellationToken);
		await SaveSettingsAsync(syncFolderSettings with
		{
			AutoSyncEnabled = enabled
		}, cancellationToken);
	}

	public async Task SyncNowAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		await _exchangeGate.WaitAsync(cancellationToken);
		try
		{
			SyncFolderSettings settings = await settingsStore.LoadAsync(cancellationToken);
			if (string.IsNullOrWhiteSpace(settings.FolderPath))
			{
				throw new InvalidOperationException("Choose a sync folder before starting a sync.");
			}
			await SaveSettingsAsync(settings with
			{
				Status = "Syncing",
				LastError = null
			}, cancellationToken);
			IncomingResult incoming = await ProcessIncomingPackagesAsync(settings.FolderPath, cancellationToken);
			OutgoingResult outgoing = await PublishOutgoingPackagesAsync(settings.FolderPath, cancellationToken);
			SyncFolderSettings syncFolderSettings = await settingsStore.LoadAsync(cancellationToken);
			string text = incoming.Warning ?? outgoing.Warning;
			await SaveSettingsAsync(syncFolderSettings with
			{
				Status = ((text == null) ? "Idle" : "Error"),
				LastSyncAtUtc = DateTime.UtcNow,
				LastOutgoingAtUtc = DateTime.UtcNow,
				LastOutgoingPackages = outgoing.Packages,
				LastIncomingPackages = incoming.Packages,
				LastAppliedChanges = incoming.Applied,
				LastError = text
			}, cancellationToken);
		}
		catch (Exception exception)
		{
			await RecordErrorAsync(exception, cancellationToken);
			throw;
		}
		finally
		{
			_exchangeGate.Release();
		}
	}

	public async Task PollAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		SyncFolderSettings settings = await settingsStore.LoadAsync(cancellationToken);
		if (!settings.AutoSyncEnabled || string.IsNullOrWhiteSpace(settings.FolderPath) || !(await _exchangeGate.WaitAsync(0, cancellationToken)))
		{
			return;
		}
		try
		{
			await SaveSettingsAsync(settings with
			{
				Status = "Syncing"
			}, cancellationToken);
			IncomingResult incoming = await ProcessIncomingPackagesAsync(settings.FolderPath, cancellationToken);
			bool shouldPublish = !settings.LastOutgoingAtUtc.HasValue || DateTime.UtcNow - settings.LastOutgoingAtUtc.Value >= OutgoingInterval;
			OutgoingResult outgoingResult = ((!shouldPublish) ? new OutgoingResult(0) : (await PublishOutgoingPackagesAsync(settings.FolderPath, cancellationToken)));
			OutgoingResult outgoing = outgoingResult;
			SyncFolderSettings syncFolderSettings = await settingsStore.LoadAsync(cancellationToken);
			string text = incoming.Warning ?? outgoing.Warning ?? ((incoming.Packages > 0 || outgoing.Packages > 0) ? null : syncFolderSettings.LastError);
			await SaveSettingsAsync(syncFolderSettings with
			{
				Status = ((text == null) ? "Idle" : "Error"),
				LastSyncAtUtc = ((incoming.Packages > 0 || outgoing.Packages > 0) ? new DateTime?(DateTime.UtcNow) : syncFolderSettings.LastSyncAtUtc),
				LastOutgoingAtUtc = (shouldPublish ? new DateTime?(DateTime.UtcNow) : syncFolderSettings.LastOutgoingAtUtc),
				LastOutgoingPackages = outgoing.Packages,
				LastIncomingPackages = incoming.Packages,
				LastAppliedChanges = incoming.Applied,
				LastError = text
			}, cancellationToken);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception exception)
		{
			await RecordErrorAsync(exception, cancellationToken);
			logger.LogError(exception, "Background folder sync failed.");
		}
		finally
		{
			_exchangeGate.Release();
		}
	}

	public async Task<int> ProcessIncomingPackagesOnceAsync(string folderPath, CancellationToken cancellationToken = default(CancellationToken))
	{
		IncomingResult incomingResult = await ProcessIncomingPackagesAsync(Path.GetFullPath(folderPath), cancellationToken);
		if (incomingResult.Warning != null)
		{
			throw new InvalidDataException(incomingResult.Warning);
		}
		return incomingResult.Packages;
	}

	private async Task<IncomingResult> ProcessIncomingPackagesAsync(string folderPath, CancellationToken cancellationToken)
	{
		string path = Path.Combine(folderPath, "from_android");
		string archiveDirectory = Path.Combine(folderPath, "archive");
		string errorsDirectory = Path.Combine(folderPath, "errors");
		Directory.CreateDirectory(path);
		Directory.CreateDirectory(archiveDirectory);
		Directory.CreateDirectory(errorsDirectory);
		int packages = 0;
		int appliedChanges = 0;
		string warning = null;
		foreach (string packagePath in Directory.EnumerateFiles(path, "*.pbsync", SearchOption.TopDirectoryOnly))
		{
			cancellationToken.ThrowIfCancellationRequested();
			FileInfo fileInfo = new FileInfo(packagePath);
			StableFileObservation observation = new StableFileObservation(fileInfo.Length, fileInfo.LastWriteTimeUtc);
			if (_observations.AddOrUpdate(packagePath, observation with
			{
				StableObservations = 1
			}, (string _, StableFileObservation existing) => (existing.Length != observation.Length || !(existing.LastWriteTimeUtc == observation.LastWriteTimeUtc)) ? observation with
			{
				StableObservations = 1
			} : existing with
			{
				StableObservations = existing.StableObservations + 1
			}).StableObservations < 2 || !TryAcquireExclusiveRead(packagePath))
			{
				continue;
			}
			_observations.TryRemove(packagePath, out StableFileObservation _);
			try
			{
				using IServiceScope scope = scopeFactory.CreateScope();
				SyncImportResult syncImportResult = await scope.ServiceProvider.GetRequiredService<SyncPackageService>().ImportAsync(packagePath, cancellationToken);
				MoveToUniqueDirectory(packagePath, archiveDirectory);
				packages++;
				appliedChanges += syncImportResult.Applied;
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex2) when (IsPackageDataError(ex2))
			{
				string path2 = MoveToUniqueDirectory(packagePath, errorsDirectory);
				warning = $"A sync package was moved to errors: {Path.GetFileName(path2)} ({ex2.Message})";
				logger.LogWarning(ex2, "Quarantined invalid incoming sync package {PackagePath}.", packagePath);
			}
		}
		return new IncomingResult(packages, appliedChanges, warning);
	}

	private async Task<OutgoingResult> PublishOutgoingPackagesAsync(string folderPath, CancellationToken cancellationToken)
	{
		string outgoingDirectory = Path.Combine(folderPath, "from_windows");
		Directory.CreateDirectory(outgoingDirectory);
		using IServiceScope scope = scopeFactory.CreateScope();
		PharmaBillDbContext context = scope.ServiceProvider.GetRequiredService<PharmaBillDbContext>();
		List<Guid> pendingChangeIds = await (from change in context.ChangeLogs.AsNoTracking()
			where (int)change.SyncState == 0
			orderby change.ChangedAtUtc
			select change.Id).ToListAsync(cancellationToken);
		if (pendingChangeIds.Count == 0)
		{
			return new OutgoingResult(0);
		}
		SyncDeviceService devices = scope.ServiceProvider.GetRequiredService<SyncDeviceService>();
		IReadOnlyList<DeviceInfo> source = await devices.GetDevicesAsync(cancellationToken);
		SyncPackageService packageService = scope.ServiceProvider.GetRequiredService<SyncPackageService>();
		int packages = 0;
		foreach (DeviceInfo item in source.Where((DeviceInfo device) => !device.IsCurrentDevice && !device.IsDeleted && string.Equals(device.Platform, "Android", StringComparison.OrdinalIgnoreCase) && devices.IsPaired(device.Id)))
		{
			cancellationToken.ThrowIfCancellationRequested();
			string path = $"PharmaBill-{DateTime.UtcNow:yyyyMMdd-HHmmssfff}-{item.Id:N}-{Guid.NewGuid():N}.pbsync";
			string destinationPath = Path.Combine(outgoingDirectory, path);
			await packageService.ExportAsync(item.Id, destinationPath, pendingChangeIds, cancellationToken);
			packages++;
		}
		if (packages > 0)
		{
			HashSet<Guid> pendingIdSet = pendingChangeIds.ToHashSet();
			foreach (ChangeLog item2 in await context.ChangeLogs.Where((ChangeLog change) => pendingIdSet.Contains(change.Id) && (int)change.SyncState == 0).ToListAsync(cancellationToken))
			{
				item2.SyncState = SyncState.Synced;
			}
			await context.SaveChangesAsync(cancellationToken);
		}
		return new OutgoingResult(packages, (pendingChangeIds.Count > 0 && packages == 0) ? "Pending changes are waiting for a paired Android device." : null);
	}

	private async Task RecordErrorAsync(Exception exception, CancellationToken cancellationToken)
	{
		SyncFolderSettings syncFolderSettings = await settingsStore.LoadAsync(cancellationToken);
		await SaveSettingsAsync(syncFolderSettings with
		{
			Status = "Error",
			LastError = exception.Message
		}, cancellationToken);
	}

	private async Task SaveSettingsAsync(SyncFolderSettings settings, CancellationToken cancellationToken)
	{
		await settingsStore.SaveAsync(settings, cancellationToken);
		SettingsChanged?.Invoke(settings);
	}

	private static bool TryAcquireExclusiveRead(string path)
	{
		try
		{
			using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
			{
				return true;
			}
		}
		catch (IOException)
		{
			return false;
		}
		catch (UnauthorizedAccessException)
		{
			return false;
		}
	}

	private static string MoveToUniqueDirectory(string sourcePath, string destinationDirectory)
	{
		Directory.CreateDirectory(destinationDirectory);
		string fileName = Path.GetFileName(sourcePath);
		string text = Path.Combine(destinationDirectory, fileName);
		if (File.Exists(text))
		{
			text = Path.Combine(destinationDirectory, $"{Path.GetFileNameWithoutExtension(fileName)}-{Guid.NewGuid():N}{Path.GetExtension(fileName)}");
		}
		File.Move(sourcePath, text);
		return text;
	}

	private static bool IsPackageDataError(Exception exception)
	{
		if (exception is InvalidDataException || exception is CryptographicException || exception is JsonException || exception is FormatException || exception is EndOfStreamException)
		{
			return true;
		}
		return false;
	}
}
