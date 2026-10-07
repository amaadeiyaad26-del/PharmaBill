using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PharmaBill.Core.Entities;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Sync;

public sealed class CloudSyncService(IServiceScopeFactory scopeFactory, CloudSyncSettingsStore settingsStore, ICloudHttpClientProvider httpClients, DatabaseDeviceId deviceId, ILogger<CloudSyncService> logger)
{
	public static readonly TimeSpan Interval = TimeSpan.FromMinutes(5L);

	private const int PushBatchSize = 500;

	private const int MaxPullPages = 200;

	private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);

	public event Action<CloudSyncSettings>? SettingsChanged;

	public Task<CloudSyncSettings> GetSettingsAsync(CancellationToken ct = default(CancellationToken))
	{
		return settingsStore.LoadAsync(ct);
	}

	public async Task ConfigureAsync(string serverUrl, string apiKey, bool enabled, CancellationToken ct = default(CancellationToken))
	{
		CloudSyncSettings cloudSyncSettings = await settingsStore.LoadAsync(ct);
		CloudSyncSettings cloudSyncSettings2 = cloudSyncSettings with
		{
			ServerUrl = serverUrl.Trim(),
			ApiKey = apiKey.Trim(),
			Enabled = enabled
		};
		if (enabled && !cloudSyncSettings2.IsConfigured)
		{
			throw new InvalidOperationException("Enter an https:// server URL (http only for localhost) and a valid branch API key.");
		}
		if (cloudSyncSettings2.ServerUrl != cloudSyncSettings.ServerUrl || cloudSyncSettings2.ApiKey != cloudSyncSettings.ApiKey)
		{
			cloudSyncSettings2 = cloudSyncSettings2 with
			{
				PullCursor = string.Empty,
				PushCursorUtc = DateTime.MinValue
			};
		}
		await SaveAsync(cloudSyncSettings2, ct);
	}

	public async Task<CloudSyncRunResult> SyncNowAsync(CancellationToken ct = default(CancellationToken))
	{
		await _gate.WaitAsync(ct);
		try
		{
			return await RunAsync(ct);
		}
		finally
		{
			_gate.Release();
		}
	}

	public async Task PollAsync(CancellationToken ct = default(CancellationToken))
	{
		CloudSyncSettings cloudSyncSettings = await settingsStore.LoadAsync(ct);
		bool flag = !cloudSyncSettings.Enabled || !cloudSyncSettings.IsConfigured;
		if (!flag)
		{
			flag = !(await _gate.WaitAsync(0, ct));
		}
		if (flag)
		{
			return;
		}
		try
		{
			await RunAsync(ct);
		}
		catch (OperationCanceledException) when (ct.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception exception)
		{
			logger.LogWarning(exception, "Background cloud sync failed.");
		}
		finally
		{
			_gate.Release();
		}
	}

	private async Task<CloudSyncRunResult> RunAsync(CancellationToken ct)
	{
		CloudSyncSettings settings = await settingsStore.LoadAsync(ct);
		if (!settings.IsConfigured)
		{
			throw new InvalidOperationException("Cloud sync is not configured.");
		}
		settings.TryReadBranchId(out var branchId);
		try
		{
			await SaveAsync(settings with
			{
				Status = "Syncing",
				LastError = null
			}, ct);
			CloudSyncClient client = new CloudSyncClient(httpClients.Create(settings), settings.ApiKey, branchId, deviceId.Value);
			int pushed = await PushPendingAsync(client, ct);
			(int, int) tuple = await PullAndApplyAsync(client, ct);
			int pulled = tuple.Item1;
			int applied = tuple.Item2;
			CloudSyncSettings cloudSyncSettings = await settingsStore.LoadAsync(ct);
			await SaveAsync(cloudSyncSettings with
			{
				Status = "Idle",
				LastError = null,
				LastPushAtUtc = DateTime.UtcNow,
				LastPullAtUtc = DateTime.UtcNow,
				LastPushedChanges = pushed,
				LastPulledChanges = pulled
			}, ct);
			return new CloudSyncRunResult(pushed, pulled, applied);
		}
		catch (OperationCanceledException) when (ct.IsCancellationRequested)
		{
			await SaveAsync(await settingsStore.LoadAsync(CancellationToken.None)with
			{
				Status = "Idle"
			}, CancellationToken.None);
			throw;
		}
		catch (Exception ex2)
		{
			Exception e = ex2;
			await SaveAsync(await settingsStore.LoadAsync(CancellationToken.None)with
			{
				Status = "Error",
				LastError = e.Message
			}, CancellationToken.None);
			ExceptionDispatchInfo.Capture((ex2 as Exception) ?? throw ex2).Throw();
		}
		throw null;
	}

	private async Task<int> PushPendingAsync(CloudSyncClient client, CancellationToken ct)
	{
		int total = 0;
		while (true)
		{
			DateTime cursor = (await settingsStore.LoadAsync(ct)).PushCursorUtc;
			using IServiceScope scope = scopeFactory.CreateScope();
			PharmaBillDbContext context = scope.ServiceProvider.GetRequiredService<PharmaBillDbContext>();
			SyncPayloadSerializer serializer = scope.ServiceProvider.GetRequiredService<SyncPayloadSerializer>();
			int limit = 500;
			List<ChangeLog> list;
			while (true)
			{
				list = await (from c in context.ChangeLogs.AsNoTracking()
					where c.ChangedAtUtc >= cursor
					orderby c.ChangedAtUtc, c.Id
					select c).Take(limit).ToListAsync(ct);
				if (list.Count < limit)
				{
					break;
				}
				if (list[list.Count - 1].ChangedAtUtc > cursor || limit >= 1000000)
				{
					break;
				}
				limit *= 2;
			}
			List<ChangeLog> fresh = list;
			if (fresh.Count == 0)
			{
				return total;
			}
			foreach (ChangeLog[] item in fresh.Chunk(500))
			{
				List<CloudChangeWire> changes = item.Select((ChangeLog change) =>
				{
					SyncChangeEnvelope syncChangeEnvelope = serializer.FromChangeLog(change);
					return new CloudChangeWire(syncChangeEnvelope.ChangeId, syncChangeEnvelope.Entity, syncChangeEnvelope.EntityId, syncChangeEnvelope.Operation, syncChangeEnvelope.SchemaVersion, syncChangeEnvelope.HlcStamp, syncChangeEnvelope.OriginDeviceId, syncChangeEnvelope.ChangedAtUtc, syncChangeEnvelope.Payload);
				}).ToList();
				total += (await client.PushAsync(changes, ct)).Accepted;
			}
			DateTime newCursor = fresh[fresh.Count - 1].ChangedAtUtc;
			CloudSyncSettings cloudSyncSettings = await settingsStore.LoadAsync(ct);
			await settingsStore.SaveAsync(cloudSyncSettings with
			{
				PushCursorUtc = newCursor
			}, ct);
			if (newCursor == cursor)
			{
				return total;
			}
		}
	}

	private async Task<(int Pulled, int Applied)> PullAndApplyAsync(CloudSyncClient client, CancellationToken ct)
	{
		int pulled = 0;
		int applied = 0;
		for (int page = 0; page < 200; page++)
		{
			string since = (await settingsStore.LoadAsync(ct)).PullCursor;
			CloudPullPage result = await client.PullAsync(since, 200, ct);
			if (result.Changes.Count > 0)
			{
				using IServiceScope scope = scopeFactory.CreateScope();
				ChangeApplier requiredService = scope.ServiceProvider.GetRequiredService<ChangeApplier>();
				List<SyncChangeEnvelope> input = result.Changes.Select((CloudChangeWire c) => new SyncChangeEnvelope(c.ChangeId, c.Entity, c.EntityId, c.Operation, c.SchemaVersion, c.HlcStamp, c.OriginDeviceId, c.ChangedAtUtc, c.Payload)).ToList();
				applied += (await requiredService.ApplyAsync(input, null, ct)).Applied;
				pulled += result.Changes.Count;
			}
			if (!string.IsNullOrEmpty(result.NextSince) && result.NextSince != since)
			{
				CloudSyncSettings cloudSyncSettings = await settingsStore.LoadAsync(ct);
				await settingsStore.SaveAsync(cloudSyncSettings with
				{
					PullCursor = result.NextSince
				}, ct);
			}
			if (!result.HasMore)
			{
				break;
			}
		}
		return (Pulled: pulled, Applied: applied);
	}

	private async Task SaveAsync(CloudSyncSettings settings, CancellationToken ct)
	{
		await settingsStore.SaveAsync(settings, ct);
		SettingsChanged?.Invoke(settings);
	}
}
