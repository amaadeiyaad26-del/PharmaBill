using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using PharmaBill.Core.Entities;
using PharmaBill.Core.Sync;

namespace PharmaBill.Data.Persistence;

public sealed class SaveChangesAuditInterceptor : SaveChangesInterceptor
{
    private readonly DatabaseDeviceId _deviceId;
    private readonly HybridLogicalClockState _clock;

    public SaveChangesAuditInterceptor(DatabaseDeviceId deviceId, HybridLogicalClockState? clock = null)
    {
        _deviceId = deviceId;
        _clock = clock ?? new HybridLogicalClockState();
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        StampAndRecordChanges(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        StampAndRecordChanges(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void StampAndRecordChanges(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        if (context is PharmaBillDbContext { IsApplyingSync: true })
        {
            return;
        }

        var changedEntries = context.ChangeTracker.Entries<EntityBase>()
            .Where(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .ToArray();
        if (changedEntries.Length == 0)
        {
            return;
        }

        var changedAt = DateTime.UtcNow;
        var stamp = _clock.Now(_deviceId.Value, changedAt);
        var logs = new List<ChangeLog>(changedEntries.Length);
        foreach (var entry in changedEntries)
        {
            if (entry.Entity is ChangeLog)
            {
                continue;
            }

            var wasDeleted = entry.State == EntityState.Deleted;
            if (wasDeleted)
            {
                entry.State = EntityState.Modified;
                entry.Entity.IsDeleted = true;
            }

            entry.Entity.DeviceId = _deviceId.Value;
            entry.Entity.UpdatedAtUtc = changedAt;
            entry.Entity.HlcStamp = stamp;
            entry.Entity.SyncState = SyncState.Pending;

            if (entry.Entity is DeviceInfo or SyncConflict)
            {
                entry.Entity.SyncState = SyncState.Synced;
                continue;
            }

            if (entry.Entity is CatalogImportState or CatalogMedicine or CatalogInfo)
            {
                continue;
            }

            if (entry.Entity is Batch && entry.State == EntityState.Modified)
            {
                var modifiedProperties = entry.Properties.Where(property => property.IsModified).ToArray();
                if (modifiedProperties.Length > 0 &&
                    modifiedProperties.All(property => property.Metadata.Name == nameof(Batch.Quantity)))
                {
                    continue;
                }
            }

            logs.Add(new ChangeLog
            {
                DeviceId = _deviceId.Value,
                CreatedAtUtc = changedAt,
                UpdatedAtUtc = changedAt,
                ChangedAtUtc = changedAt,
                HlcStamp = stamp,
                EntityName = entry.Metadata.ClrType.Name,
                EntityId = entry.Entity.Id,
                Operation = wasDeleted ? "SoftDeleted" :
                    entry.State == EntityState.Added ? "Added" : "Modified",
                Payload = JsonSerializer.Serialize(entry.Entity, entry.Metadata.ClrType)
            });
        }

        context.Set<ChangeLog>().AddRange(logs);
    }
}
