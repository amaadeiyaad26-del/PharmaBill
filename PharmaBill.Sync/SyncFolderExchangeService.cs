using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PharmaBill.Core.Entities;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Sync;

public sealed class SyncFolderExchangeService(
    IServiceScopeFactory scopeFactory,
    SyncFolderSettingsStore settingsStore,
    ILogger<SyncFolderExchangeService> logger)
{
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan OutgoingInterval = TimeSpan.FromMinutes(10);

    private readonly SemaphoreSlim _exchangeGate = new(1, 1);
    private readonly ConcurrentDictionary<string, StableFileObservation> _observations =
        new(StringComparer.OrdinalIgnoreCase);

    public event Action<SyncFolderSettings>? SettingsChanged;

    public Task<SyncFolderSettings> GetSettingsAsync(CancellationToken cancellationToken = default) =>
        settingsStore.LoadAsync(cancellationToken);

    public async Task ConfigureAsync(
        string folderPath,
        bool autoSyncEnabled,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);
        var fullPath = Path.GetFullPath(folderPath);
        var current = await settingsStore.LoadAsync(cancellationToken);
        var updated = current with { FolderPath = fullPath, AutoSyncEnabled = autoSyncEnabled };
        await SaveSettingsAsync(updated, cancellationToken);
    }

    public async Task SetAutoSyncAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        var current = await settingsStore.LoadAsync(cancellationToken);
        await SaveSettingsAsync(current with { AutoSyncEnabled = enabled }, cancellationToken);
    }

    public async Task SyncNowAsync(CancellationToken cancellationToken = default)
    {
        await _exchangeGate.WaitAsync(cancellationToken);
        try
        {
            var settings = await settingsStore.LoadAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(settings.FolderPath))
            {
                throw new InvalidOperationException("Choose a sync folder before starting a sync.");
            }

            await SaveSettingsAsync(settings with { Status = "Syncing", LastError = null }, cancellationToken);
            var incoming = await ProcessIncomingPackagesAsync(settings.FolderPath, cancellationToken);
            var outgoing = await PublishOutgoingPackagesAsync(settings.FolderPath, cancellationToken);
            var completed = await settingsStore.LoadAsync(cancellationToken);
            var warning = incoming.Warning ?? outgoing.Warning;
            await SaveSettingsAsync(completed with
            {
                Status = warning is null ? "Idle" : "Error",
                LastSyncAtUtc = DateTime.UtcNow,
                LastOutgoingAtUtc = DateTime.UtcNow,
                LastOutgoingPackages = outgoing.Packages,
                LastIncomingPackages = incoming.Packages,
                LastAppliedChanges = incoming.Applied,
                LastError = warning
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

    public async Task PollAsync(CancellationToken cancellationToken = default)
    {
        var settings = await settingsStore.LoadAsync(cancellationToken);
        if (!settings.AutoSyncEnabled || string.IsNullOrWhiteSpace(settings.FolderPath))
        {
            return;
        }

        if (!await _exchangeGate.WaitAsync(0, cancellationToken))
        {
            return;
        }

        try
        {
            await SaveSettingsAsync(settings with { Status = "Syncing" }, cancellationToken);
            var incoming = await ProcessIncomingPackagesAsync(settings.FolderPath, cancellationToken);
            var shouldPublish = !settings.LastOutgoingAtUtc.HasValue ||
                                DateTime.UtcNow - settings.LastOutgoingAtUtc.Value >= OutgoingInterval;
            var outgoing = shouldPublish
                ? await PublishOutgoingPackagesAsync(settings.FolderPath, cancellationToken)
                : new OutgoingResult(0);
            var current = await settingsStore.LoadAsync(cancellationToken);
            var warning = incoming.Warning ?? outgoing.Warning ??
                          (incoming.Packages > 0 || outgoing.Packages > 0 ? null : current.LastError);
            await SaveSettingsAsync(current with
            {
                Status = warning is null ? "Idle" : "Error",
                LastSyncAtUtc = incoming.Packages > 0 || outgoing.Packages > 0
                    ? DateTime.UtcNow
                    : current.LastSyncAtUtc,
                LastOutgoingAtUtc = shouldPublish ? DateTime.UtcNow : current.LastOutgoingAtUtc,
                LastOutgoingPackages = outgoing.Packages,
                LastIncomingPackages = incoming.Packages,
                LastAppliedChanges = incoming.Applied,
                LastError = warning
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

    public async Task<int> ProcessIncomingPackagesOnceAsync(
        string folderPath,
        CancellationToken cancellationToken = default)
    {
        var result = await ProcessIncomingPackagesAsync(Path.GetFullPath(folderPath), cancellationToken);
        if (result.Warning is not null)
        {
            throw new InvalidDataException(result.Warning);
        }

        return result.Packages;
    }

    private async Task<IncomingResult> ProcessIncomingPackagesAsync(
        string folderPath,
        CancellationToken cancellationToken)
    {
        var incomingDirectory = Path.Combine(folderPath, "from_android");
        var archiveDirectory = Path.Combine(folderPath, "archive");
        var errorsDirectory = Path.Combine(folderPath, "errors");
        Directory.CreateDirectory(incomingDirectory);
        Directory.CreateDirectory(archiveDirectory);
        Directory.CreateDirectory(errorsDirectory);

        var packages = 0;
        var appliedChanges = 0;
        string? warning = null;
        foreach (var packagePath in Directory.EnumerateFiles(incomingDirectory, "*.pbsync", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fileInfo = new FileInfo(packagePath);
            var observation = new StableFileObservation(fileInfo.Length, fileInfo.LastWriteTimeUtc);
            var previous = _observations.AddOrUpdate(
                packagePath,
                observation with { StableObservations = 1 },
                (_, existing) => existing.Length == observation.Length &&
                                 existing.LastWriteTimeUtc == observation.LastWriteTimeUtc
                    ? existing with { StableObservations = existing.StableObservations + 1 }
                    : observation with { StableObservations = 1 });
            if (previous.StableObservations < 2 || !TryAcquireExclusiveRead(packagePath))
            {
                continue;
            }

            _observations.TryRemove(packagePath, out _);
            try
            {
                using var scope = scopeFactory.CreateScope();
                var import = await scope.ServiceProvider.GetRequiredService<SyncPackageService>()
                    .ImportAsync(packagePath, cancellationToken);
                MoveToUniqueDirectory(packagePath, archiveDirectory);
                packages++;
                appliedChanges += import.Applied;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (IsPackageDataError(exception))
            {
                var quarantinePath = MoveToUniqueDirectory(packagePath, errorsDirectory);
                warning = $"A sync package was moved to errors: {Path.GetFileName(quarantinePath)} ({exception.Message})";
                logger.LogWarning(exception, "Quarantined invalid incoming sync package {PackagePath}.", packagePath);
            }
        }

        return new IncomingResult(packages, appliedChanges, warning);
    }

    private async Task<OutgoingResult> PublishOutgoingPackagesAsync(
        string folderPath,
        CancellationToken cancellationToken)
    {
        var outgoingDirectory = Path.Combine(folderPath, "from_windows");
        Directory.CreateDirectory(outgoingDirectory);
        using var scope = scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<PharmaBillDbContext>();
        var pendingChangeIds = await context.ChangeLogs.AsNoTracking()
            .Where(change => change.SyncState == SyncState.Pending)
            .OrderBy(change => change.ChangedAtUtc)
            .Select(change => change.Id)
            .ToListAsync(cancellationToken);
        if (pendingChangeIds.Count == 0)
        {
            return new OutgoingResult(0);
        }

        var devices = scope.ServiceProvider.GetRequiredService<SyncDeviceService>();
        var peers = await devices.GetDevicesAsync(cancellationToken);
        var packageService = scope.ServiceProvider.GetRequiredService<SyncPackageService>();
        var packages = 0;
        foreach (var peer in peers.Where(device =>
                     !device.IsCurrentDevice &&
                     !device.IsDeleted &&
                     string.Equals(device.Platform, "Android", StringComparison.OrdinalIgnoreCase) &&
                     devices.IsPaired(device.Id)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = $"PharmaBill-{DateTime.UtcNow:yyyyMMdd-HHmmssfff}-{peer.Id:N}-{Guid.NewGuid():N}.pbsync";
            var destination = Path.Combine(outgoingDirectory, name);
            await packageService.ExportAsync(peer.Id, destination, pendingChangeIds, cancellationToken);
            packages++;
        }

        if (packages > 0)
        {
            var pendingIdSet = pendingChangeIds.ToHashSet();
            var exportedChanges = await context.ChangeLogs
                .Where(change => pendingIdSet.Contains(change.Id) && change.SyncState == SyncState.Pending)
                .ToListAsync(cancellationToken);
            foreach (var change in exportedChanges)
            {
                change.SyncState = SyncState.Synced;
            }

            await context.SaveChangesAsync(cancellationToken);
        }

        return new OutgoingResult(
            packages,
            pendingChangeIds.Count > 0 && packages == 0
                ? "Pending changes are waiting for a paired Android device."
                : null);
    }

    private async Task RecordErrorAsync(Exception exception, CancellationToken cancellationToken)
    {
        var current = await settingsStore.LoadAsync(cancellationToken);
        await SaveSettingsAsync(current with
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
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
            return true;
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
        var fileName = Path.GetFileName(sourcePath);
        var destination = Path.Combine(destinationDirectory, fileName);
        if (File.Exists(destination))
        {
            destination = Path.Combine(
                destinationDirectory,
                $"{Path.GetFileNameWithoutExtension(fileName)}-{Guid.NewGuid():N}{Path.GetExtension(fileName)}");
        }

        File.Move(sourcePath, destination);
        return destination;
    }

    private static bool IsPackageDataError(Exception exception) =>
        exception is InvalidDataException or CryptographicException or JsonException or FormatException or EndOfStreamException;

    private sealed record StableFileObservation(long Length, DateTime LastWriteTimeUtc, int StableObservations = 0);
    private sealed record IncomingResult(int Packages, int Applied, string? Warning);
    private sealed record OutgoingResult(int Packages, string? Warning = null);
}

internal sealed class SyncFolderPollingService(
    SyncFolderExchangeService exchangeService,
    ILogger<SyncFolderPollingService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(SyncFolderExchangeService.PollInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await exchangeService.PollAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Folder sync polling iteration failed.");
            }
        }
    }
}
