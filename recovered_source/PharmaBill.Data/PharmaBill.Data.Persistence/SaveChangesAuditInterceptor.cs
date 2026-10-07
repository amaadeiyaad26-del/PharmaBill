using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using PharmaBill.Core.Entities;
using PharmaBill.Core.Sync;
using PharmaBill.Data.Services;

namespace PharmaBill.Data.Persistence;

public sealed class SaveChangesAuditInterceptor : SaveChangesInterceptor
{
	private readonly DatabaseDeviceId _deviceId;

	private readonly HybridLogicalClockState _clock;

	private readonly BranchSettingsStore? _branchSettings;

	public SaveChangesAuditInterceptor(DatabaseDeviceId deviceId, HybridLogicalClockState? clock = null, BranchSettingsStore? branchSettings = null)
	{
		_deviceId = deviceId;
		_clock = clock ?? new HybridLogicalClockState();
		_branchSettings = branchSettings;
	}

	public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
	{
		StampAndRecordChanges(eventData.Context);
		return base.SavingChanges(eventData, result);
	}

	public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default(CancellationToken))
	{
		StampAndRecordChanges(eventData.Context);
		return base.SavingChangesAsync(eventData, result, cancellationToken);
	}

	private void StampAndRecordChanges(DbContext? context)
	{
		if (context == null || context is PharmaBillDbContext { IsApplyingSync: not false })
		{
			return;
		}
		EntityEntry<EntityBase>[] array = context.ChangeTracker.Entries<EntityBase>().Where((EntityEntry<EntityBase> entry) =>
		{
			EntityState state = entry.State;
			return (uint)(state - 2) <= 2u;
		}).ToArray();
		if (array.Length == 0)
		{
			return;
		}
		DateTime utcNow = DateTime.UtcNow;
		string hlcStamp = _clock.Now(_deviceId.Value, utcNow);
		List<ChangeLog> list = new List<ChangeLog>(array.Length);
		EntityEntry<EntityBase>[] array2 = array;
		foreach (EntityEntry<EntityBase> entityEntry in array2)
		{
			if (entityEntry.Entity is ChangeLog)
			{
				continue;
			}
			bool flag = entityEntry.State == EntityState.Deleted;
			if (flag)
			{
				entityEntry.State = EntityState.Modified;
				entityEntry.Entity.IsDeleted = true;
			}
			entityEntry.Entity.DeviceId = _deviceId.Value;
			entityEntry.Entity.UpdatedAtUtc = utcNow;
			entityEntry.Entity.HlcStamp = hlcStamp;
			entityEntry.Entity.SyncState = SyncState.Pending;
			if (entityEntry.State == EntityState.Added && entityEntry.Entity is AuditLog { BranchId: null } auditLog)
			{
				Guid? guid = _branchSettings?.Load().CurrentBranchId;
				if (guid.HasValue)
				{
					Guid valueOrDefault = guid.GetValueOrDefault();
					auditLog.BranchId = valueOrDefault;
				}
			}
			EntityBase entity = entityEntry.Entity;
			if ((entity is DeviceInfo || entity is SyncConflict) ? true : false)
			{
				entityEntry.Entity.SyncState = SyncState.Synced;
				continue;
			}
			entity = entityEntry.Entity;
			if ((entity is CatalogImportState || entity is CatalogMedicine || entity is CatalogInfo) ? true : false)
			{
				continue;
			}
			if (entityEntry.Entity is Batch && entityEntry.State == EntityState.Modified)
			{
				PropertyEntry[] array3 = entityEntry.Properties.Where((PropertyEntry property) => property.IsModified).ToArray();
				if (array3.Length != 0 && array3.All((PropertyEntry property) => property.Metadata.Name == "Quantity"))
				{
					continue;
				}
			}
			list.Add(new ChangeLog
			{
				DeviceId = _deviceId.Value,
				CreatedAtUtc = utcNow,
				UpdatedAtUtc = utcNow,
				ChangedAtUtc = utcNow,
				HlcStamp = hlcStamp,
				EntityName = entityEntry.Metadata.ClrType.Name,
				EntityId = entityEntry.Entity.Id,
				Operation = (flag ? "SoftDeleted" : ((entityEntry.State == EntityState.Added) ? "Added" : "Modified")),
				Payload = JsonSerializer.Serialize(entityEntry.Entity, entityEntry.Metadata.ClrType)
			});
		}
		context.Set<ChangeLog>().AddRange(list);
	}
}
