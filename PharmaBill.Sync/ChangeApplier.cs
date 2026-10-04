using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PharmaBill.Core.Entities;
using PharmaBill.Core.Sync;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Sync;

public sealed class ChangeApplier(PharmaBillDbContext context, HybridLogicalClock clock, SyncPayloadSerializer serializer)
{
    private static readonly HashSet<string> ImmutableEntities = new(StringComparer.Ordinal)
    {
        "Sale", "SaleItem", "WholesaleInvoice", "WholesaleInvoiceItem",
        "PurchaseInvoice", "PurchaseItem", "PurchaseReturn", "PurchaseReturnItem",
        "Receipt", "ReturnNote", "SaleReturnItem", "WholesaleReturnItem",
        "ScheduleRegisterEntry", "AuditLog", "StockAdjustment", "StockVerificationSession",
        "StockVerificationItem", "ExpiryWriteOff"
    };

    public async Task<SyncImportResult> ApplyAsync(
        IEnumerable<SyncChangeEnvelope> input,
        IReadOnlyDictionary<(string Entity, Guid EntityId, string Property), string>? attachmentPaths = null,
        CancellationToken cancellationToken = default)
    {
        var changes = input.OrderBy(change => change, SyncChangeComparer.Instance).ToArray();
        if (changes.Select(change => change.ChangeId).Distinct().Count() != changes.Length)
        {
            throw new InvalidDataException("A sync package contains duplicate change IDs.");
        }

        foreach (var change in changes)
        {
            Validate(change);
        }

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        context.IsApplyingSync = true;
        var applied = 0;
        var duplicates = 0;
        var conflicts = 0;
        var stockConflicts = 0;
        var touchedBatches = new HashSet<Guid>();
        try
        {
            foreach (var change in changes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var prior = await context.ChangeLogs.IgnoreQueryFilters()
                    .SingleOrDefaultAsync(item => item.Id == change.ChangeId, cancellationToken);
                var wirePayload = CanonicalWirePayload(change);
                if (prior is not null)
                {
                    if (prior.EntityName != change.Entity ||
                        prior.EntityId != change.EntityId ||
                        prior.Operation != change.Operation ||
                        prior.HlcStamp != change.HlcStamp ||
                        prior.DeviceId != change.OriginDeviceId ||
                        prior.Payload != wirePayload)
                    {
                        throw new InvalidDataException("A previously seen change ID has different content.");
                    }

                    duplicates++;
                    continue;
                }

                var entityType = SyncPayloadSerializer.ResolveEntityType(change.Entity);
                var incoming = serializer.DeserializeEntity(change);
                incoming.DeviceId = change.OriginDeviceId;
                incoming.HlcStamp = change.HlcStamp;
                incoming.SyncState = SyncState.Synced;
                incoming.IsDeleted = change.Operation == "SoftDeleted" || incoming.IsDeleted;
                if (attachmentPaths is not null)
                {
                    ApplyAttachmentPaths(incoming, change, attachmentPaths);
                }
                if (incoming.Id != change.EntityId)
                {
                    throw new InvalidDataException(
                        $"The sync payload entity ID '{incoming.Id:D}' does not match its envelope ID '{change.EntityId:D}'.");
                }

                var existing = await context.FindAsync(entityType, [change.EntityId], cancellationToken);
                if (change.Entity == nameof(StockMovement))
                {
                    if (existing is not null)
                    {
                        if (serializer.SerializeEntity((EntityBase)existing) != wirePayload)
                        {
                            AddConflict(change, wirePayload, serializer.SerializeEntity((EntityBase)existing));
                            conflicts++;
                        }
                    }
                    else
                    {
                        context.Add(incoming);
                        touchedBatches.Add(((StockMovement)incoming).BatchId);
                    }
                }
                else if (existing is null)
                {
                    context.Add(incoming);
                    if (incoming is Batch addedBatch)
                    {
                        touchedBatches.Add(addedBatch.Id);
                    }
                }
                else if (ImmutableEntities.Contains(change.Entity))
                {
                    var localPayload = serializer.SerializeEntity((EntityBase)existing);
                    if (localPayload != wirePayload)
                    {
                        AddConflict(change, wirePayload, localPayload);
                        conflicts++;
                    }
                }
                else
                {
                    var local = (EntityBase)existing;
                    var localPayload = serializer.SerializeEntity(local);
                    if (localPayload != wirePayload)
                    {
                        AddConflict(change, wirePayload, localPayload);
                        conflicts++;
                        if (CompareVersion(change.HlcStamp, change.ChangedAtUtc, change.OriginDeviceId,
                                local.HlcStamp, local.UpdatedAtUtc, local.DeviceId) > 0)
                        {
                            context.Entry(existing).CurrentValues.SetValues(incoming);
                            ((EntityBase)existing).SyncState = SyncState.Synced;
                        }
                    }
                }

                if (incoming is Batch changedBatch)
                {
                    touchedBatches.Add(changedBatch.Id);
                }

                clock.Observe(change.HlcStamp);
                context.ChangeLogs.Add(new ChangeLog
                {
                    Id = change.ChangeId,
                    EntityName = change.Entity,
                    EntityId = change.EntityId,
                    Operation = change.Operation,
                    ChangedAtUtc = change.ChangedAtUtc,
                    CreatedAtUtc = change.ChangedAtUtc,
                    UpdatedAtUtc = change.ChangedAtUtc,
                    DeviceId = change.OriginDeviceId,
                    HlcStamp = change.HlcStamp,
                    SyncState = SyncState.Synced,
                    Payload = wirePayload
                });
                applied++;
            }

            await context.SaveChangesAsync(cancellationToken);
            foreach (var batchId in touchedBatches)
            {
                var batch = await context.Batches.IgnoreQueryFilters()
                    .SingleOrDefaultAsync(item => item.Id == batchId, cancellationToken);
                if (batch is null)
                {
                    throw new InvalidDataException($"A stock movement references missing batch {batchId:D}.");
                }

                var movements = await context.StockMovements.IgnoreQueryFilters()
                    .Where(item => item.BatchId == batchId)
                    .ToListAsync(cancellationToken);
                batch.Quantity = movements.Sum(item => item.QuantityChange);
                if (batch.Quantity < 0m)
                {
                    await AddStockConflictAsync(batch, movements, cancellationToken);
                    stockConflicts++;
                }
                else
                {
                    await ResolveStockConflictFromAdjustmentAsync(batch, movements, cancellationToken);
                }
            }

            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new SyncImportResult(applied, duplicates, conflicts, stockConflicts);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
        finally
        {
            context.IsApplyingSync = false;
        }
    }

    public async Task ResolveConflictAsync(
        Guid conflictId,
        bool acceptRemote,
        string reason,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        var conflict = await context.SyncConflicts.SingleOrDefaultAsync(item => item.Id == conflictId, cancellationToken)
            ?? throw new InvalidOperationException("The selected sync conflict no longer exists.");
        var isOpenStockConflict = conflict.EntityName == "StockConflict" && conflict.Resolution == "Open";
        if (conflict.ResolvedAtUtc is not null ||
            (!string.IsNullOrWhiteSpace(conflict.Resolution) && !isOpenStockConflict))
        {
            throw new InvalidOperationException("The selected sync conflict has already been resolved.");
        }

        if (conflict.EntityName == "StockConflict")
        {
            throw new InvalidOperationException("Stock conflicts can only be resolved by posting a stock adjustment movement with a reason.");
        }

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        if (acceptRemote)
        {
            var type = SyncPayloadSerializer.ResolveEntityType(conflict.EntityName);
            using var document = JsonDocument.Parse(conflict.RemotePayload);
            var envelope = new SyncChangeEnvelope(
                Guid.NewGuid(),
                conflict.EntityName,
                conflict.EntityId,
                "Upsert",
                1,
                conflict.HlcStamp,
                conflict.DeviceId,
                conflict.UpdatedAtUtc,
                document.RootElement.Clone());
            var remote = serializer.DeserializeEntity(envelope);
            var local = await context.FindAsync(type, [conflict.EntityId], cancellationToken);
            if (local is null)
            {
                context.Add(remote);
            }
            else
            {
                context.Entry(local).CurrentValues.SetValues(remote);
            }
        }

        conflict.Resolution = $"{(acceptRemote ? "Accepted remote" : "Kept local")}: {reason.Trim()}";
        conflict.ResolvedAtUtc = DateTime.UtcNow;
        context.AuditLogs.Add(new AuditLog
        {
            UserId = userId,
            ActionAtUtc = DateTime.UtcNow,
            Action = "SyncConflictResolved",
            EntityName = conflict.EntityName,
            EntityId = conflict.EntityId,
            Details = $"{(acceptRemote ? "Accepted remote" : "Kept local")} conflict {conflict.Id:D}: {reason.Trim()}"
        });
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private string CanonicalWirePayload(SyncChangeEnvelope change)
    {
        var entity = serializer.DeserializeEntity(change);
        return serializer.SerializeEntity(entity);
    }

    private static void ApplyAttachmentPaths(
        EntityBase entity,
        SyncChangeEnvelope change,
        IReadOnlyDictionary<(string Entity, Guid EntityId, string Property), string> attachmentPaths)
    {
        foreach (var propertyName in new[] { "DocumentPath", "Notes" })
        {
            if (!attachmentPaths.TryGetValue((change.Entity, change.EntityId, propertyName), out var path))
            {
                continue;
            }

            var property = entity.GetType().GetProperty(propertyName);
            if (propertyName == "Notes")
            {
                property?.SetValue(entity, $"DocumentPath={path}");
            }
            else
            {
                property?.SetValue(entity, path);
            }
        }
    }

    private static void Validate(SyncChangeEnvelope change)
    {
        if (change.ChangeId == Guid.Empty || change.EntityId == Guid.Empty ||
            change.OriginDeviceId == Guid.Empty ||
            change.SchemaVersion != 1 ||
            change.Operation is not ("Upsert" or "SoftDeleted") ||
            change.ChangedAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new InvalidDataException("A sync change envelope is invalid or unsupported.");
        }

        if (HybridLogicalClockState.Parse(change.HlcStamp).DeviceId != change.OriginDeviceId)
        {
            throw new InvalidDataException("The change origin device does not match its HLC stamp.");
        }

        _ = SyncPayloadSerializer.ResolveEntityType(change.Entity);
    }

    private void AddConflict(SyncChangeEnvelope change, string remotePayload, string localPayload)
    {
        context.SyncConflicts.Add(new SyncConflict
        {
            EntityName = change.Entity,
            EntityId = change.EntityId,
            LocalPayload = localPayload,
            RemotePayload = remotePayload,
            Resolution = null,
            DeviceId = change.OriginDeviceId,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            HlcStamp = change.HlcStamp
        });
    }

    private async Task AddStockConflictAsync(
        Batch batch,
        IReadOnlyCollection<StockMovement> movements,
        CancellationToken cancellationToken)
    {
        var existing = await context.SyncConflicts.SingleOrDefaultAsync(conflict =>
            conflict.EntityName == "StockConflict" && conflict.EntityId == batch.Id && conflict.Resolution == "Open",
            cancellationToken);
        var deviceIds = movements.Select(item => item.DeviceId).Distinct().Order().ToArray();
        var payload = JsonSerializer.Serialize(new
        {
            batchId = batch.Id,
            batchNo = batch.BatchNo,
            balance = batch.Quantity,
            movementIds = movements.Select(item => item.Id).Order().ToArray(),
            contributingDeviceIds = deviceIds,
            status = "Open"
        });
        if (existing is not null)
        {
            existing.RemotePayload = payload;
            existing.UpdatedAtUtc = DateTime.UtcNow;
            return;
        }

        context.SyncConflicts.Add(new SyncConflict
        {
            EntityName = "StockConflict",
            EntityId = batch.Id,
            LocalPayload = serializer.SerializeEntity(batch),
            RemotePayload = payload,
            Resolution = "Open",
            HlcStamp = movements.MaxBy(item => item.MovementAtUtc)?.HlcStamp ?? clock.Now(),
            DeviceId = movements.OrderBy(item => item.MovementAtUtc).Last().DeviceId
        });
    }

    private async Task ResolveStockConflictFromAdjustmentAsync(
        Batch batch,
        IReadOnlyCollection<StockMovement> movements,
        CancellationToken cancellationToken)
    {
        var conflict = await context.SyncConflicts.IgnoreQueryFilters().SingleOrDefaultAsync(item =>
            item.EntityName == "StockConflict" && item.EntityId == batch.Id && item.Resolution == "Open",
            cancellationToken);
        if (conflict is null)
        {
            return;
        }

        var adjustment = movements
            .Where(item => item.MovementType == "Adjustment" &&
                           item.ReferenceType == nameof(StockAdjustment) &&
                           item.QuantityChange > 0 &&
                           !string.IsNullOrWhiteSpace(item.Notes) &&
                           HybridLogicalClockState.Compare(item.HlcStamp, conflict.HlcStamp) > 0)
            .MaxBy(item => item.MovementAtUtc);
        if (adjustment is null)
        {
            return;
        }

        var resolvedAt = DateTime.UtcNow;
        conflict.Resolution = $"Resolved by stock adjustment: {adjustment.Notes!.Trim()}";
        conflict.ResolvedAtUtc = resolvedAt;
        conflict.UpdatedAtUtc = resolvedAt;
    }

    private static int CompareVersion(
        string firstStamp,
        DateTime firstUpdatedAtUtc,
        Guid firstDeviceId,
        string secondStamp,
        DateTime secondUpdatedAtUtc,
        Guid secondDeviceId)
    {
        var first = NormalizeStamp(firstStamp, firstUpdatedAtUtc, firstDeviceId);
        var second = NormalizeStamp(secondStamp, secondUpdatedAtUtc, secondDeviceId);
        return HybridLogicalClockState.Compare(first, second);
    }

    private static string NormalizeStamp(string stamp, DateTime updatedAtUtc, Guid deviceId)
    {
        try
        {
            _ = HybridLogicalClockState.Parse(stamp);
            return stamp;
        }
        catch (FormatException)
        {
            var milliseconds = new DateTimeOffset(
                updatedAtUtc.Kind == DateTimeKind.Utc
                    ? updatedAtUtc
                    : DateTime.SpecifyKind(updatedAtUtc, DateTimeKind.Utc)).ToUnixTimeMilliseconds();
            return new HlcValue(Math.Max(0, milliseconds), 0, deviceId).ToString();
        }
    }

    private sealed class SyncChangeComparer : IComparer<SyncChangeEnvelope>
    {
        public static readonly SyncChangeComparer Instance = new();

        public int Compare(SyncChangeEnvelope? x, SyncChangeEnvelope? y)
        {
            if (ReferenceEquals(x, y))
            {
                return 0;
            }

            if (x is null)
            {
                return -1;
            }

            if (y is null)
            {
                return 1;
            }

            var hlc = HybridLogicalClockState.Compare(x.HlcStamp, y.HlcStamp);
            return hlc != 0 ? hlc : string.Compare(
                x.ChangeId.ToString("D"), y.ChangeId.ToString("D"), StringComparison.Ordinal);
        }
    }
}
