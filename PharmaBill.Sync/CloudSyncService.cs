using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Sync;

public sealed record CloudSyncRunResult(int Pushed, int Pulled, int Applied);

public sealed class CloudSyncService(
    IServiceScopeFactory scopeFactory,
    CloudSyncSettingsStore settingsStore,
    ICloudHttpClientProvider httpClients,
    DatabaseDeviceId deviceId,
    ILogger<CloudSyncService> logger)
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);
    private const int PushBatchSize = 500;
    private const int MaxPullPages = 200;

    private readonly SemaphoreSlim _gate = new(1, 1);

    public event Action<CloudSyncSettings>? SettingsChanged;

    public Task<CloudSyncSettings> GetSettingsAsync(CancellationToken ct = default) => settingsStore.LoadAsync(ct);

    public async Task ConfigureAsync(string serverUrl, string apiKey, bool enabled, CancellationToken ct = default)
    {
        var current = await settingsStore.LoadAsync(ct);
        var candidate = current with { ServerUrl = serverUrl.Trim(), ApiKey = apiKey.Trim(), Enabled = enabled };
        if (enabled && !candidate.IsConfigured)
            throw new InvalidOperationException(
                "Enter an https:// server URL (http only for localhost) and a valid branch API key.");
        // A different server or branch starts from scratch.
        if (candidate.ServerUrl != current.ServerUrl || candidate.ApiKey != current.ApiKey)
            candidate = candidate with { PullCursor = string.Empty, PushCursorUtc = DateTime.MinValue };
        await SaveAsync(candidate, ct);
    }

    public async Task<CloudSyncRunResult> SyncNowAsync(CancellationToken ct = default)
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

    public async Task PollAsync(CancellationToken ct = default)
    {
        var settings = await settingsStore.LoadAsync(ct);
        if (!settings.Enabled || !settings.IsConfigured || !await _gate.WaitAsync(0, ct)) return;
        try
        {
            await RunAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Background cloud sync failed.");
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<CloudSyncRunResult> RunAsync(CancellationToken ct)
    {
        var settings = await settingsStore.LoadAsync(ct);
        if (!settings.IsConfigured)
            throw new InvalidOperationException("Cloud sync is not configured.");
        settings.TryReadBranchId(out var branchId);
        try
        {
            await SaveAsync(settings with { Status = "Syncing", LastError = null }, ct);
            var client = new CloudSyncClient(httpClients.Create(settings), settings.ApiKey, branchId, deviceId.Value);
            var pushed = await PushPendingAsync(client, ct);
            var (pulled, applied) = await PullAndApplyAsync(client, ct);
            var latest = await settingsStore.LoadAsync(ct);
            await SaveAsync(latest with
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
            await SaveAsync((await settingsStore.LoadAsync(CancellationToken.None)) with { Status = "Idle" },
                CancellationToken.None);
            throw;
        }
        catch (Exception e)
        {
            await SaveAsync((await settingsStore.LoadAsync(CancellationToken.None)) with
            {
                Status = "Error",
                LastError = e.Message
            }, CancellationToken.None);
            throw;
        }
    }

    private async Task<int> PushPendingAsync(CloudSyncClient client, CancellationToken ct)
    {
        var total = 0;
        while (true)
        {
            var cursor = (await settingsStore.LoadAsync(ct)).PushCursorUtc;
            using var scope = scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<PharmaBillDbContext>();
            var serializer = scope.ServiceProvider.GetRequiredService<SyncPayloadSerializer>();

            // Resending changes with the cursor timestamp is safe because the server is idempotent by change id.
            var limit = PushBatchSize;
            List<PharmaBill.Core.Entities.ChangeLog> rows;
            while (true)
            {
                rows = await context.ChangeLogs.AsNoTracking()
                    .Where(c => c.ChangedAtUtc >= cursor)
                    .OrderBy(c => c.ChangedAtUtc).ThenBy(c => c.Id)
                    .Take(limit).ToListAsync(ct);
                if (rows.Count < limit || rows[^1].ChangedAtUtc > cursor || limit >= 1_000_000) break;
                limit *= 2;
            }

            var fresh = rows;
            if (fresh.Count == 0) return total;

            foreach (var chunk in fresh.Chunk(PushBatchSize))
            {
                var wire = chunk.Select(change =>
                {
                    var e = serializer.FromChangeLog(change);
                    return new CloudChangeWire(e.ChangeId, e.Entity, e.EntityId, e.Operation, e.SchemaVersion,
                        e.HlcStamp, e.OriginDeviceId, e.ChangedAtUtc, e.Payload);
                }).ToList();
                var ack = await client.PushAsync(wire, ct);
                total += ack.Accepted;
            }

            var newCursor = fresh[^1].ChangedAtUtc;
            var current = await settingsStore.LoadAsync(ct);
            await settingsStore.SaveAsync(current with { PushCursorUtc = newCursor }, ct);
            if (newCursor == cursor) return total;
        }
    }

    private async Task<(int Pulled, int Applied)> PullAndApplyAsync(CloudSyncClient client, CancellationToken ct)
    {
        var pulled = 0;
        var applied = 0;
        for (var page = 0; page < MaxPullPages; page++)
        {
            var since = (await settingsStore.LoadAsync(ct)).PullCursor;
            var result = await client.PullAsync(since, 200, ct);
            if (result.Changes.Count > 0)
            {
                using var scope = scopeFactory.CreateScope();
                var applier = scope.ServiceProvider.GetRequiredService<ChangeApplier>();
                var envelopes = result.Changes.Select(c => new SyncChangeEnvelope(
                    c.ChangeId, c.Entity, c.EntityId, c.Operation, c.SchemaVersion, c.HlcStamp,
                    c.OriginDeviceId, c.ChangedAtUtc, c.Payload)).ToList();
                var import = await applier.ApplyAsync(envelopes, null, ct);
                applied += import.Applied;
                pulled += result.Changes.Count;
            }

            // The cursor only advances after the page has been applied.
            if (!string.IsNullOrEmpty(result.NextSince) && result.NextSince != since)
            {
                var current = await settingsStore.LoadAsync(ct);
                await settingsStore.SaveAsync(current with { PullCursor = result.NextSince }, ct);
            }

            if (!result.HasMore) break;
        }

        return (pulled, applied);
    }

    private async Task SaveAsync(CloudSyncSettings settings, CancellationToken ct)
    {
        await settingsStore.SaveAsync(settings, ct);
        SettingsChanged?.Invoke(settings);
    }
}

internal sealed class CloudSyncWorker(CloudSyncService service, ILogger<CloudSyncWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(CloudSyncService.Interval);
        do
        {
            try
            {
                await service.PollAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception e)
            {
                logger.LogError(e, "Cloud sync iteration failed.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}

