using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using PharmaBill.Core.Entities;
using PharmaBill.Core.Sync;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Sync;

public sealed class ChangeApplier(PharmaBillDbContext context, HybridLogicalClock clock, SyncPayloadSerializer serializer)
{
	private sealed class SyncChangeComparer : IComparer<SyncChangeEnvelope>
	{
		public static readonly SyncChangeComparer Instance = new SyncChangeComparer();

		public int Compare(SyncChangeEnvelope? x, SyncChangeEnvelope? y)
		{
			if ((object)x == y)
			{
				return 0;
			}
			if ((object)x == null)
			{
				return -1;
			}
			if ((object)y == null)
			{
				return 1;
			}
			int num = HybridLogicalClockState.Compare(x.HlcStamp, y.HlcStamp);
			if (num == 0)
			{
				return string.Compare(x.ChangeId.ToString("D"), y.ChangeId.ToString("D"), StringComparison.Ordinal);
			}
			return num;
		}
	}

	private static readonly HashSet<string> ImmutableEntities = new HashSet<string>(StringComparer.Ordinal)
	{
		"Sale", "SaleItem", "WholesaleInvoice", "WholesaleInvoiceItem", "PurchaseInvoice", "PurchaseItem", "PurchaseReturn", "PurchaseReturnItem", "Receipt", "ReturnNote",
		"SaleReturnItem", "WholesaleReturnItem", "ScheduleRegisterEntry", "AuditLog", "StockAdjustment", "StockVerificationSession", "StockVerificationItem", "ExpiryWriteOff", "StockTransfer", "StockTransferItem"
	};

	public async Task<SyncImportResult> ApplyAsync(IEnumerable<SyncChangeEnvelope> input, IReadOnlyDictionary<(string Entity, Guid EntityId, string Property), string>? attachmentPaths = null, CancellationToken cancellationToken = default(CancellationToken))
	{
		SyncChangeEnvelope[] changes = input.OrderBy((SyncChangeEnvelope result2) => result2, SyncChangeComparer.Instance).ToArray();
		if (changes.Select((SyncChangeEnvelope syncChangeEnvelope) => syncChangeEnvelope.ChangeId).Distinct().Count() != changes.Length)
		{
			throw new InvalidDataException("A sync package contains duplicate change IDs.");
		}
		SyncChangeEnvelope[] array = changes;
		for (int num = 0; num < array.Length; num++)
		{
			Validate(array[num]);
		}
		SyncImportResult result;
		await using (IDbContextTransaction transaction = await context.Database.BeginTransactionAsync(cancellationToken))
		{
			context.IsApplyingSync = true;
			int applied = 0;
			int duplicates = 0;
			int conflicts = 0;
			int stockConflicts = 0;
			HashSet<Guid> touchedBatches = new HashSet<Guid>();
			try
			{
				SyncChangeEnvelope[] array2 = changes;
				foreach (SyncChangeEnvelope change in array2)
				{
					cancellationToken.ThrowIfCancellationRequested();
					ChangeLog changeLog = await context.ChangeLogs.IgnoreQueryFilters().SingleOrDefaultAsync((ChangeLog item) => item.Id == change.ChangeId, cancellationToken);
					string wirePayload = CanonicalWirePayload(change);
					if (changeLog != null)
					{
						if (changeLog.EntityName != change.Entity || changeLog.EntityId != change.EntityId || changeLog.Operation != change.Operation || changeLog.HlcStamp != change.HlcStamp || changeLog.DeviceId != change.OriginDeviceId || changeLog.Payload != wirePayload)
						{
							throw new InvalidDataException("A previously seen change ID has different content.");
						}
						duplicates++;
						continue;
					}
					Type entityType = SyncPayloadSerializer.ResolveEntityType(change.Entity);
					EntityBase incoming = serializer.DeserializeEntity(change);
					incoming.DeviceId = change.OriginDeviceId;
					incoming.HlcStamp = change.HlcStamp;
					incoming.SyncState = SyncState.Synced;
					incoming.IsDeleted = change.Operation == "SoftDeleted" || incoming.IsDeleted;
					if (attachmentPaths != null)
					{
						ApplyAttachmentPaths(incoming, change, attachmentPaths);
					}
					if (incoming.Id != change.EntityId)
					{
						throw new InvalidDataException($"The sync payload entity ID '{incoming.Id:D}' does not match its envelope ID '{change.EntityId:D}'.");
					}
					object obj = await context.FindAsync(entityType, new object[1] { change.EntityId }, cancellationToken);
					if (change.Entity == "StockMovement")
					{
						if (obj != null)
						{
							if (serializer.SerializeEntity((EntityBase)obj) != wirePayload)
							{
								AddConflict(change, wirePayload, serializer.SerializeEntity((EntityBase)obj));
								conflicts++;
							}
						}
						else
						{
							context.Add(incoming);
							touchedBatches.Add(((StockMovement)incoming).BatchId);
						}
					}
					else if (obj == null)
					{
						context.Add(incoming);
						if (incoming is Batch batch)
						{
							touchedBatches.Add(batch.Id);
						}
					}
					else if (ImmutableEntities.Contains(change.Entity))
					{
						string text = serializer.SerializeEntity((EntityBase)obj);
						if (text != wirePayload)
						{
							AddConflict(change, wirePayload, text);
							conflicts++;
						}
					}
					else
					{
						EntityBase entityBase = (EntityBase)obj;
						string text2 = serializer.SerializeEntity(entityBase);
						if (text2 != wirePayload)
						{
							AddConflict(change, wirePayload, text2);
							conflicts++;
							if (CompareVersion(change.HlcStamp, change.ChangedAtUtc, change.OriginDeviceId, entityBase.HlcStamp, entityBase.UpdatedAtUtc, entityBase.DeviceId) > 0)
							{
								context.Entry(obj).CurrentValues.SetValues(incoming);
								((EntityBase)obj).SyncState = SyncState.Synced;
							}
						}
					}
					if (incoming is Batch batch2)
					{
						touchedBatches.Add(batch2.Id);
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
				foreach (Guid batchId in touchedBatches)
				{
					Batch batch3 = await context.Batches.IgnoreQueryFilters().SingleOrDefaultAsync((Batch item) => item.Id == batchId, cancellationToken);
					if (batch3 == null)
					{
						throw new InvalidDataException($"A stock movement references missing batch {batchId:D}.");
					}
					List<StockMovement> list = await (from item in context.StockMovements.IgnoreQueryFilters()
						where item.BatchId == batchId
						select item).ToListAsync(cancellationToken);
					batch3.Quantity = list.Sum((StockMovement item) => item.QuantityChange);
					if (batch3.Quantity < 0m)
					{
						await AddStockConflictAsync(batch3, list, cancellationToken);
						stockConflicts++;
					}
					else
					{
						await ResolveStockConflictFromAdjustmentAsync(batch3, list, cancellationToken);
					}
				}
				await context.SaveChangesAsync(cancellationToken);
				await transaction.CommitAsync(cancellationToken);
				result = new SyncImportResult(applied, duplicates, conflicts, stockConflicts);
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
		return result;
	}

	public async Task ResolveConflictAsync(Guid conflictId, bool acceptRemote, string reason, Guid userId, CancellationToken cancellationToken = default(CancellationToken))
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(reason, "reason");
		SyncConflict conflict = (await context.SyncConflicts.SingleOrDefaultAsync((SyncConflict item) => item.Id == conflictId, cancellationToken)) ?? throw new InvalidOperationException("The selected sync conflict no longer exists.");
		bool flag = conflict.EntityName == "StockConflict" && conflict.Resolution == "Open";
		if (conflict.ResolvedAtUtc.HasValue || (!string.IsNullOrWhiteSpace(conflict.Resolution) && !flag))
		{
			throw new InvalidOperationException("The selected sync conflict has already been resolved.");
		}
		if (conflict.EntityName == "StockConflict")
		{
			throw new InvalidOperationException("Stock conflicts can only be resolved by posting a stock adjustment movement with a reason.");
		}
		await using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync(cancellationToken);
		if (acceptRemote)
		{
			Type entityType = SyncPayloadSerializer.ResolveEntityType(conflict.EntityName);
			using JsonDocument document = JsonDocument.Parse(conflict.RemotePayload);
			SyncChangeEnvelope envelope = new SyncChangeEnvelope(Guid.NewGuid(), conflict.EntityName, conflict.EntityId, "Upsert", 1, conflict.HlcStamp, conflict.DeviceId, conflict.UpdatedAtUtc, document.RootElement.Clone());
			EntityBase remote = serializer.DeserializeEntity(envelope);
			object obj = await context.FindAsync(entityType, new object[1] { conflict.EntityId }, cancellationToken);
			if (obj == null)
			{
				context.Add(remote);
			}
			else
			{
				context.Entry(obj).CurrentValues.SetValues(remote);
			}
		}
		conflict.Resolution = (acceptRemote ? "Accepted remote" : "Kept local") + ": " + reason.Trim();
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
		EntityBase entity = serializer.DeserializeEntity(change);
		return serializer.SerializeEntity(entity);
	}

	private static void ApplyAttachmentPaths(EntityBase entity, SyncChangeEnvelope change, IReadOnlyDictionary<(string Entity, Guid EntityId, string Property), string> attachmentPaths)
	{
		string[] array = new string[2] { "DocumentPath", "Notes" };
		foreach (string text in array)
		{
			if (attachmentPaths.TryGetValue((change.Entity, change.EntityId, text), out string value))
			{
				PropertyInfo property = entity.GetType().GetProperty(text);
				if (text == "Notes")
				{
					property?.SetValue(entity, "DocumentPath=" + value);
				}
				else
				{
					property?.SetValue(entity, value);
				}
			}
		}
	}

	private static void Validate(SyncChangeEnvelope change)
	{
		bool flag = change.ChangeId == Guid.Empty || change.EntityId == Guid.Empty || change.OriginDeviceId == Guid.Empty || change.SchemaVersion != 1;
		if (!flag)
		{
			string operation = change.Operation;
			bool flag2 = ((operation == "Upsert" || operation == "SoftDeleted") ? true : false);
			flag = !flag2;
		}
		if (flag || change.ChangedAtUtc.Kind != DateTimeKind.Utc)
		{
			throw new InvalidDataException("A sync change envelope is invalid or unsupported.");
		}
		if (HybridLogicalClockState.Parse(change.HlcStamp).DeviceId != change.OriginDeviceId)
		{
			throw new InvalidDataException("The change origin device does not match its HLC stamp.");
		}
		SyncPayloadSerializer.ResolveEntityType(change.Entity);
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

	private async Task AddStockConflictAsync(Batch batch, IReadOnlyCollection<StockMovement> movements, CancellationToken cancellationToken)
	{
		SyncConflict syncConflict = await context.SyncConflicts.SingleOrDefaultAsync((SyncConflict conflict) => conflict.EntityName == "StockConflict" && conflict.EntityId == batch.Id && conflict.Resolution == "Open", cancellationToken);
		Guid[] contributingDeviceIds = movements.Select((StockMovement item) => item.DeviceId).Distinct().Order()
			.ToArray();
		string remotePayload = JsonSerializer.Serialize(new
		{
			batchId = batch.Id,
			batchNo = batch.BatchNo,
			balance = batch.Quantity,
			movementIds = movements.Select((StockMovement item) => item.Id).Order().ToArray(),
			contributingDeviceIds = contributingDeviceIds,
			status = "Open"
		});
		if (syncConflict != null)
		{
			syncConflict.RemotePayload = remotePayload;
			syncConflict.UpdatedAtUtc = DateTime.UtcNow;
			return;
		}
		context.SyncConflicts.Add(new SyncConflict
		{
			EntityName = "StockConflict",
			EntityId = batch.Id,
			LocalPayload = serializer.SerializeEntity(batch),
			RemotePayload = remotePayload,
			Resolution = "Open",
			HlcStamp = (movements.MaxBy((StockMovement item) => item.MovementAtUtc)?.HlcStamp ?? clock.Now()),
			DeviceId = movements.OrderBy((StockMovement item) => item.MovementAtUtc).Last().DeviceId
		});
	}

	private async Task ResolveStockConflictFromAdjustmentAsync(Batch batch, IReadOnlyCollection<StockMovement> movements, CancellationToken cancellationToken)
	{
		SyncConflict conflict = await context.SyncConflicts.IgnoreQueryFilters().SingleOrDefaultAsync((SyncConflict item) => item.EntityName == "StockConflict" && item.EntityId == batch.Id && item.Resolution == "Open", cancellationToken);
		if (conflict != null)
		{
			StockMovement stockMovement = movements.Where((StockMovement item) => item.MovementType == "Adjustment" && item.ReferenceType == "StockAdjustment" && item.QuantityChange > 0m && !string.IsNullOrWhiteSpace(item.Notes) && HybridLogicalClockState.Compare(item.HlcStamp, conflict.HlcStamp) > 0).MaxBy((StockMovement item) => item.MovementAtUtc);
			if (stockMovement != null)
			{
				DateTime utcNow = DateTime.UtcNow;
				conflict.Resolution = "Resolved by stock adjustment: " + stockMovement.Notes.Trim();
				conflict.ResolvedAtUtc = utcNow;
				conflict.UpdatedAtUtc = utcNow;
			}
		}
	}

	private static int CompareVersion(string firstStamp, DateTime firstUpdatedAtUtc, Guid firstDeviceId, string secondStamp, DateTime secondUpdatedAtUtc, Guid secondDeviceId)
	{
		string first = NormalizeStamp(firstStamp, firstUpdatedAtUtc, firstDeviceId);
		string second = NormalizeStamp(secondStamp, secondUpdatedAtUtc, secondDeviceId);
		return HybridLogicalClockState.Compare(first, second);
	}

	private static string NormalizeStamp(string stamp, DateTime updatedAtUtc, Guid deviceId)
	{
		try
		{
			HybridLogicalClockState.Parse(stamp);
			return stamp;
		}
		catch (FormatException)
		{
			long val = new DateTimeOffset((updatedAtUtc.Kind == DateTimeKind.Utc) ? updatedAtUtc : DateTime.SpecifyKind(updatedAtUtc, DateTimeKind.Utc)).ToUnixTimeMilliseconds();
			return new HlcValue(Math.Max(0L, val), 0L, deviceId).ToString();
		}
	}
}
