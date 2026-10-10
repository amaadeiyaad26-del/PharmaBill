using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PharmaBill.Core.Accounting;
using PharmaBill.Core.Compliance;
using PharmaBill.Core.Entities;
using PharmaBill.Core.Security;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Data.Services;

public sealed class PurchaseService(IUnitOfWork unitOfWork, IEntitlementService entitlementService, StorageLocationService storageLocations, BranchService branchService, ILicenseRuntimeGuard licenseRuntimeGuard)
{
	private sealed record ValidatedLine(PurchaseLineInput Input, decimal BaseAmount, decimal TaxAmount, decimal LineTotal);

	public async Task<bool> IsDuplicateInvoiceAsync(Guid supplierId, string invoiceNo, CancellationToken cancellationToken = default(CancellationToken))
	{
		string key = NormalizeInvoiceNo(invoiceNo);
		if (key.Length == 0)
		{
			return false;
		}

		return await unitOfWork.Context.PurchaseInvoices.IgnoreQueryFilters()
			.AnyAsync(
				(PurchaseInvoice invoice) => invoice.SupplierId == supplierId
					&& invoice.InvoiceNo.ToLower() == key,
				cancellationToken);
	}

	/// <summary>
	/// True when this spreadsheet/file content was already transferred into stock (any supplier).
	/// Prevents re-importing the same CSV/Excel from doubling quantities under a new auto invoice no.
	/// </summary>
	public async Task<bool> IsDuplicateSourceFingerprintAsync(string? sourceFingerprint, CancellationToken cancellationToken = default)
	{
		string marker = SourceFingerprintMarker(sourceFingerprint);
		if (marker.Length == 0)
		{
			return false;
		}

		return await unitOfWork.Context.PurchaseInvoices.IgnoreQueryFilters()
			.AnyAsync(
				(PurchaseInvoice invoice) => invoice.Notes != null && invoice.Notes.Contains(marker),
				cancellationToken);
	}

	public static string SourceFingerprintMarker(string? sourceFingerprint)
	{
		if (string.IsNullOrWhiteSpace(sourceFingerprint))
		{
			return string.Empty;
		}

		return "[src:" + sourceFingerprint.Trim().ToLowerInvariant() + "]";
	}

	public static string NormalizeInvoiceNo(string? invoiceNo) =>
		string.IsNullOrWhiteSpace(invoiceNo) ? string.Empty : invoiceNo.Trim().ToLowerInvariant();

	public static string AppendSourceFingerprint(string? notes, string? sourceFingerprint)
	{
		string marker = SourceFingerprintMarker(sourceFingerprint);
		if (marker.Length == 0)
		{
			return notes?.Trim() ?? string.Empty;
		}

		string existing = notes?.Trim() ?? string.Empty;
		if (existing.Contains(marker, StringComparison.OrdinalIgnoreCase))
		{
			return existing;
		}

		return string.IsNullOrWhiteSpace(existing) ? marker : existing + " " + marker;
	}

	public static string? ExtractSourceFingerprint(string? notes)
	{
		if (string.IsNullOrWhiteSpace(notes))
		{
			return null;
		}

		int start = notes.IndexOf("[src:", StringComparison.OrdinalIgnoreCase);
		if (start < 0)
		{
			return null;
		}

		int valueStart = start + 5;
		int end = notes.IndexOf(']', valueStart);
		if (end <= valueStart)
		{
			return null;
		}

		string value = notes[valueStart..end].Trim();
		return string.IsNullOrWhiteSpace(value) ? null : value.ToLowerInvariant();
	}

	public async Task<PurchaseInvoice> SavePurchaseAsync(SavePurchaseInput input, Guid actingUserId, UserRole role, CancellationToken cancellationToken = default(CancellationToken))
	{
		ValidateHeader(input);
		if (!PermissionMatrix.Allows(role, AppPermission.ManagePurchases))
		{
			throw new UnauthorizedAccessException("Your role cannot enter purchases.");
		}
		if (!(await entitlementService.CanPerformAsync(ProtectedOperation.EnterStock, cancellationToken)))
		{
			throw new InvalidOperationException("Purchase entry is unavailable in read-only mode.");
		}
		if (await IsDuplicateInvoiceAsync(input.SupplierId, input.InvoiceNo, cancellationToken))
		{
			throw new InvalidOperationException(
				"Invoice '" + input.InvoiceNo.Trim() + "' was already transferred to stock for this supplier. Stock was not added again.");
		}

		string? sourceFingerprint = ExtractSourceFingerprint(input.Notes);
		if (await IsDuplicateSourceFingerprintAsync(sourceFingerprint, cancellationToken))
		{
			throw new InvalidOperationException(
				"This spreadsheet/invoice file was already transferred to stock. Stock was not added again.");
		}
		if (!(await unitOfWork.Context.Suppliers.AnyAsync((Supplier supplier) => supplier.Id == input.SupplierId && supplier.IsActive, cancellationToken)))
		{
			throw new InvalidOperationException("Select an active supplier.");
		}
		Guid[] drugIds = input.Items.Select((PurchaseLineInput item) => item.DrugId).Distinct().ToArray();
		Dictionary<Guid, Drug> drugs = await unitOfWork.Context.Drugs.Where((Drug drug2) => drugIds.Contains(drug2.Id) && drug2.IsActive).ToDictionaryAsync((Drug drug2) => drug2.Id, cancellationToken);
		if (drugs.Count != drugIds.Length)
		{
			throw new InvalidOperationException("One or more purchase items refer to an unavailable medicine.");
		}
		ValidatedLine[] validatedItems = input.Items.Select((PurchaseLineInput item) => ValidateLine(item, drugs[item.DrugId])).ToArray();
		decimal expectedSubtotal = validatedItems.Sum((ValidatedLine item) => item.BaseAmount);
		decimal expectedTax = validatedItems.Sum((ValidatedLine item) => item.TaxAmount);
		decimal expectedTotal = expectedSubtotal + expectedTax;
		if (RoundMoney(input.Subtotal) != RoundMoney(expectedSubtotal) || RoundMoney(input.TaxAmount) != RoundMoney(expectedTax) || RoundMoney(input.GrandTotal) != RoundMoney(expectedTotal))
		{
			throw new InvalidOperationException("Purchase totals do not equal the validated item totals.");
		}
		IUnitOfWorkTransaction unitOfWorkTransaction = ((unitOfWork.Context.Database.CurrentTransaction != null) ? null : (await unitOfWork.BeginTransactionAsync(cancellationToken)));
		IUnitOfWorkTransaction ownedTransaction = unitOfWorkTransaction;
		PurchaseInvoice result;
		await using (ownedTransaction)
		{
			Guid? storageLocationId = input.StorageLocationId;
			StorageLocation storageLocation;
			if (storageLocationId.HasValue)
			{
				Guid locationId = storageLocationId.GetValueOrDefault();
				storageLocation = await unitOfWork.Context.StorageLocations.SingleAsync((StorageLocation item) => item.Id == locationId && item.IsActive, cancellationToken);
			}
			else
			{
				storageLocation = await storageLocations.EnsureDefaultRetailLocationAsync(cancellationToken);
			}
			StorageLocation receiveLocation = storageLocation;
			Branch branch = await branchService.EnsureCurrentBranchAsync(cancellationToken);
			decimal num = RoundMoney(expectedTotal - input.DiscountAmount);
			PurchaseInvoice invoice = new PurchaseInvoice
			{
				SupplierId = input.SupplierId,
				InvoiceNo = input.InvoiceNo.Trim(),
				InvoiceDate = input.InvoiceDate,
				Subtotal = RoundMoney(expectedSubtotal),
				TaxAmount = RoundMoney(expectedTax),
				DiscountAmount = RoundMoney(input.DiscountAmount),
				TotalAmount = num,
				Status = PurchaseInvoice.CommittedStatus,
				Notes = NullIfWhiteSpace(AppendSourceFingerprint(input.Notes, ExtractSourceFingerprint(input.Notes))),
				StorageLocationId = receiveLocation.Id,
				BranchId = branch.Id,
				AttachedInvoicePath = NullIfWhiteSpace(input.AttachedInvoicePath),
				OriginalFileName = NullIfWhiteSpace(input.OriginalFileName)
			};
			unitOfWork.Context.PurchaseInvoices.Add(invoice);
			// Same EF transaction: payable increases (Credit); payments later reduce it via Debit.
			unitOfWork.Context.SupplierLedgerEntries.Add(new SupplierLedgerEntry
			{
				SupplierId = input.SupplierId,
				EntryAtUtc = invoice.InvoiceDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
				EntryType = LedgerEntryTypes.PurchaseBill,
				ReferenceId = invoice.Id,
				ReferenceNo = invoice.InvoiceNo,
				Debit = 0m,
				Credit = num,
				Notes = "Purchase bill " + invoice.InvoiceNo.Trim()
			});
			Dictionary<(Guid DrugId, string BatchNo), Batch> batchesByKey = new Dictionary<(Guid, string), Batch>();
			Dictionary<Guid, decimal> resultingQuantities = new Dictionary<Guid, decimal>();
			ValidatedLine[] array = validatedItems;
			foreach (ValidatedLine line in array)
			{
				PurchaseLineInput inputLine = line.Input;
				string batchNo = inputLine.BatchNo.Trim();
				(Guid DrugId, string BatchNo) batchKey = (inputLine.DrugId, batchNo.ToUpperInvariant());
				if (!batchesByKey.TryGetValue(batchKey, out Batch batch))
				{
					List<Batch> existing = await unitOfWork.Context.Batches.Where((Batch item) => item.DrugId == inputLine.DrugId && item.BatchNo.ToLower() == batchNo.ToLower()).ToListAsync(cancellationToken);
					batch = existing.FirstOrDefault((Batch item) => item.ExpiryDate == inputLine.ExpiryDate) ?? existing.OrderByDescending((Batch item) => item.Quantity).FirstOrDefault();
				}
				if (batch == null)
				{
					batch = new Batch
					{
						DrugId = inputLine.DrugId,
						SupplierId = input.SupplierId,
						BatchNo = batchNo,
						ExpiryDate = inputLine.ExpiryDate,
						Quantity = 0m,
						Mrp = inputLine.Mrp,
						Ptr = inputLine.Rate,
						PurchasePrice = inputLine.Rate,
						SalePrice = inputLine.Mrp > 0m ? inputLine.Mrp : inputLine.Rate,
						Rack = NullIfWhiteSpace(inputLine.Rack),
						BranchId = branch.Id
					};
					unitOfWork.Context.Batches.Add(batch);
				}
				else
				{
					batch.SupplierId = input.SupplierId;
					batch.ExpiryDate = inputLine.ExpiryDate;
					batch.Mrp = inputLine.Mrp;
					batch.Ptr = inputLine.Rate;
					batch.PurchasePrice = inputLine.Rate;
					batch.SalePrice = inputLine.Mrp > 0m ? inputLine.Mrp : batch.SalePrice;
					if (!string.IsNullOrWhiteSpace(inputLine.Rack))
					{
						batch.Rack = inputLine.Rack.Trim();
					}
				}
				batchesByKey[batchKey] = batch;
				if (!resultingQuantities.TryGetValue(batch.Id, out var value))
				{
					value = (await unitOfWork.Context.StockMovements.Where((StockMovement movement) => movement.BatchId == batch.Id).SumAsync((Expression<Func<StockMovement, decimal?>>)((StockMovement movement) => movement.QuantityChange), cancellationToken)).GetValueOrDefault();
				}
				decimal receivedQuantity = inputLine.Quantity + inputLine.FreeQuantity;
				batch.Quantity = value + receivedQuantity;
				resultingQuantities[batch.Id] = batch.Quantity;
				unitOfWork.Context.PurchaseItems.Add(new PurchaseItem
				{
					PurchaseInvoiceId = invoice.Id,
					DrugId = inputLine.DrugId,
					BatchId = batch.Id,
					Quantity = inputLine.Quantity,
					FreeQuantity = inputLine.FreeQuantity,
					BatchNo = batchNo,
					ExpiryDate = inputLine.ExpiryDate,
					Mrp = inputLine.Mrp,
					Ptr = inputLine.Rate,
					UnitPrice = inputLine.Rate,
					DiscountAmount = inputLine.DiscountAmount,
					TaxRate = inputLine.GstRate,
					LineTotal = line.LineTotal
				});
				unitOfWork.Context.StockMovements.Add(new StockMovement
				{
					BatchId = batch.Id,
					DrugId = inputLine.DrugId,
					LocationId = receiveLocation.Id,
					QuantityChange = receivedQuantity,
					MovementType = "PurchaseInward",
					ReferenceType = "PurchaseInvoice",
					ReferenceId = invoice.Id,
					MovementAtUtc = DateTime.UtcNow,
					Notes = input.InvoiceNo.Trim()
				});
				await StorageLocationService.ApplyBalanceDeltaAsync(unitOfWork.Context, receiveLocation.Id, batch.Id, inputLine.DrugId, receivedQuantity, cancellationToken);
				Drug drug = drugs[inputLine.DrugId];
				CatalogInfo info = await GetCatalogInfoAsync(drug, cancellationToken);
				string controlledRegisterType = GetControlledRegisterType(drug.Schedule, info);
				if (controlledRegisterType != null)
				{
					unitOfWork.Context.ScheduleRegisterEntries.Add(new ScheduleRegisterEntry
					{
						RegisterType = controlledRegisterType,
						DrugId = drug.Id,
						SupplierId = input.SupplierId,
						BatchId = batch.Id,
						BatchNo = batch.BatchNo,
						Quantity = receivedQuantity,
						EntryAtUtc = DateTime.UtcNow,
						Notes = "Purchase invoice " + input.InvoiceNo.Trim()
					});
				}
			}
			await RecalculateDrugStockAsync(drugs, cancellationToken);
			unitOfWork.Context.AuditLogs.Add(new AuditLog
			{
				UserId = actingUserId,
				Action = "PurchaseInvoicePosted",
				EntityName = "PurchaseInvoice",
				EntityId = invoice.Id,
				BranchId = branch.Id,
				Details = $"Invoice {invoice.InvoiceNo}; total {invoice.TotalAmount}; branch {branch.Code}"
			});
			await unitOfWork.SaveChangesAsync(cancellationToken);
			if (ownedTransaction != null)
			{
				await ownedTransaction.CommitAsync(cancellationToken);
			}
			if (!licenseRuntimeGuard.OnTransactionCommitted())
			{
				throw new InvalidOperationException("System clock manipulation detected. Please set your system time correctly to resume.");
			}
			result = invoice;
		}
		return result;
	}

	public async Task<PurchaseReturn> SavePurchaseReturnAsync(Guid supplierId, string returnNo, DateOnly returnDate, IReadOnlyList<PurchaseReturnLineInput> lines, string reason, Guid actingUserId, UserRole role, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (!PermissionMatrix.Allows(role, AppPermission.ProcessReturn))
		{
			throw new UnauthorizedAccessException("Your role cannot process purchase returns.");
		}
		if (!(await entitlementService.CanPerformAsync(ProtectedOperation.ProcessReturn, cancellationToken)))
		{
			throw new InvalidOperationException("Purchase returns are unavailable in read-only mode.");
		}
		ArgumentException.ThrowIfNullOrWhiteSpace(returnNo, "returnNo");
		ArgumentException.ThrowIfNullOrWhiteSpace(reason, "reason");
		if (returnDate > DateOnly.FromDateTime(DateTime.Today))
		{
			throw new InvalidOperationException("Purchase return date cannot be in the future.");
		}
		if (lines.Count == 0 || lines.Any((PurchaseReturnLineInput purchaseReturnLineInput) => purchaseReturnLineInput.Quantity <= 0m))
		{
			throw new InvalidOperationException("Add at least one purchase-return line with a positive quantity.");
		}
		lines = (from purchaseReturnLineInput in lines
			group purchaseReturnLineInput by purchaseReturnLineInput.BatchId into @group
			select new PurchaseReturnLineInput(@group.Key, @group.Sum((PurchaseReturnLineInput purchaseReturnLineInput) => purchaseReturnLineInput.Quantity))).ToArray();
		if (await unitOfWork.Context.PurchaseReturns.IgnoreQueryFilters().AnyAsync((PurchaseReturn item) => item.ReturnNo == returnNo.Trim(), cancellationToken))
		{
			throw new InvalidOperationException("A purchase return with this number already exists.");
		}
		Supplier supplier = (await unitOfWork.Context.Suppliers.SingleOrDefaultAsync((Supplier item) => item.Id == supplierId && item.IsActive, cancellationToken)) ?? throw new InvalidOperationException("Select an active supplier.");
		PurchaseReturn result;
		await using (IUnitOfWorkTransaction transaction = await unitOfWork.BeginTransactionAsync(cancellationToken))
		{
			Dictionary<Guid, Batch> batches = await unitOfWork.Context.Batches.Where((Batch batch3) => lines.Select((PurchaseReturnLineInput purchaseReturnLineInput) => purchaseReturnLineInput.BatchId).Contains(batch3.Id) && batch3.SupplierId == supplier.Id).ToDictionaryAsync((Batch batch3) => batch3.Id, cancellationToken);
			if (batches.Count != lines.Select((PurchaseReturnLineInput purchaseReturnLineInput) => purchaseReturnLineInput.BatchId).Distinct().Count())
			{
				throw new InvalidOperationException("Every return batch must belong to the selected supplier.");
			}
			Dictionary<Guid, decimal> onHandByBatch = new Dictionary<Guid, decimal>();
			foreach (PurchaseReturnLineInput line in lines)
			{
				Batch batch = batches[line.BatchId];
				decimal valueOrDefault = (await unitOfWork.Context.StockMovements.Where((StockMovement movement) => movement.BatchId == batch.Id).SumAsync((Expression<Func<StockMovement, decimal?>>)((StockMovement movement) => movement.QuantityChange), cancellationToken)).GetValueOrDefault();
				if (line.Quantity > valueOrDefault)
				{
					throw new InvalidOperationException("Return quantity exceeds stock for batch " + batch.BatchNo + ".");
				}
				onHandByBatch[batch.Id] = valueOrDefault;
			}
			foreach (Batch lockedBatch in batches.Values)
			{
				RecordLockGuard.Demand(lockedBatch.CreatedAtUtc, "BatchStock", lockedBatch.Id, "MODIFIED_AFTER_LOCK");
			}
			PurchaseReturn purchaseReturn = new PurchaseReturn
			{
				SupplierId = supplierId,
				ReturnNo = returnNo.Trim(),
				ReturnDate = returnDate,
				Reason = reason.Trim()
			};
			unitOfWork.Context.PurchaseReturns.Add(purchaseReturn);
			foreach (PurchaseReturnLineInput line in lines)
			{
				Batch batch2 = batches[line.BatchId];
				decimal purchasePrice = batch2.PurchasePrice;
				decimal num = RoundMoney(purchasePrice * line.Quantity);
				purchaseReturn.TotalAmount += num;
				batch2.Quantity = onHandByBatch[batch2.Id] - line.Quantity;
				onHandByBatch[batch2.Id] = batch2.Quantity;
				unitOfWork.Context.PurchaseReturnItems.Add(new PurchaseReturnItem
				{
					PurchaseReturnId = purchaseReturn.Id,
					BatchId = batch2.Id,
					DrugId = batch2.DrugId,
					Quantity = line.Quantity,
					UnitPrice = purchasePrice,
					LineTotal = num
				});
				unitOfWork.Context.StockMovements.Add(new StockMovement
				{
					BatchId = batch2.Id,
					DrugId = batch2.DrugId,
					QuantityChange = -line.Quantity,
					MovementType = "PurchaseReturn",
					ReferenceType = "PurchaseReturn",
					ReferenceId = purchaseReturn.Id,
					Notes = purchaseReturn.ReturnNo
				});
				Drug drug = await unitOfWork.Context.Drugs.SingleAsync((Drug item) => item.Id == batch2.DrugId, cancellationToken);
				CatalogInfo info = await GetCatalogInfoAsync(drug, cancellationToken);
				string controlledRegisterType = GetControlledRegisterType(drug.Schedule, info);
				unitOfWork.Context.ScheduleRegisterEntries.Add(new ScheduleRegisterEntry
				{
					RegisterType = (controlledRegisterType ?? "PurchaseReturn"),
					DrugId = drug.Id,
					SupplierId = supplierId,
					BatchId = batch2.Id,
					BatchNo = batch2.BatchNo,
					Quantity = line.Quantity,
					EntryAtUtc = DateTime.UtcNow,
					Notes = "Outgoing purchase return " + purchaseReturn.ReturnNo + "; " + reason.Trim()
				});
			}
			unitOfWork.Context.SupplierLedgerEntries.Add(new SupplierLedgerEntry
			{
				SupplierId = supplier.Id,
				EntryAtUtc = DateTime.UtcNow,
				EntryType = LedgerEntryTypes.PurchaseReturn,
				ReferenceId = purchaseReturn.Id,
				ReferenceNo = purchaseReturn.ReturnNo,
				Debit = purchaseReturn.TotalAmount,
				Credit = 0m,
				Notes = reason.Trim()
			});
			unitOfWork.Context.AuditLogs.Add(new AuditLog
			{
				UserId = actingUserId,
				Action = "PurchaseReturnPosted",
				EntityName = "PurchaseReturn",
				EntityId = purchaseReturn.Id,
				Details = "Return " + purchaseReturn.ReturnNo + "; reason: " + reason.Trim()
			});
			foreach (Batch lockedBatch in batches.Values.Where(item => RecordLockPolicy.IsLocked(item.CreatedAtUtc)))
			{
				await ComplianceAuditWriter.AppendIfUnlockedAsync(unitOfWork.Context, lockedBatch.CreatedAtUtc, new ComplianceAuditEntry
				{
					EntityType = "BatchStock",
					EntityId = lockedBatch.Id,
					Action = "MODIFIED_AFTER_LOCK",
					AuthorizedBy = string.Empty,
					Reason = reason.Trim(),
					OldSnapshotJson = ComplianceAuditWriter.Snapshot(new { lockedBatch.BatchNo, purchaseReturn.ReturnNo }),
					NewSnapshotJson = ComplianceAuditWriter.Snapshot(new { lockedBatch.BatchNo, lockedBatch.Quantity, purchaseReturn.ReturnNo, purchaseReturn.TotalAmount }),
					UserId = actingUserId
				}, cancellationToken);
			}
			await unitOfWork.SaveChangesAsync(cancellationToken);
			await transaction.CommitAsync(cancellationToken);
			result = purchaseReturn;
		}
		return result;
	}

	private async Task RecalculateDrugStockAsync(Dictionary<Guid, Drug> drugs, CancellationToken cancellationToken)
	{
		Guid[] drugIds = drugs.Keys.ToArray();
		DateOnly today = DateOnly.FromDateTime(DateTime.Today);
		List<Batch> batches = await unitOfWork.Context.Batches.Where((Batch batch) => drugIds.Contains(batch.DrugId)).ToListAsync(cancellationToken);
		foreach (Guid drugId in drugIds)
		{
			drugs[drugId].StockQuantity = batches
				.Where((Batch batch) => batch.DrugId == drugId && batch.Quantity > 0m && (!batch.ExpiryDate.HasValue || batch.ExpiryDate.Value >= today))
				.Sum((Batch batch) => batch.Quantity);
		}
	}

	private static void ValidateHeader(SavePurchaseInput input)
	{
		if (input.SupplierId == Guid.Empty)
		{
			throw new InvalidOperationException("Select a supplier.");
		}
		ArgumentException.ThrowIfNullOrWhiteSpace(input.InvoiceNo, "input.InvoiceNo");
		if (input.InvoiceDate > DateOnly.FromDateTime(DateTime.Today))
		{
			throw new InvalidOperationException("Purchase invoice date cannot be in the future.");
		}
		if (input.Items.Count == 0)
		{
			throw new InvalidOperationException("Add at least one purchase item.");
		}
		if (input.DiscountAmount < 0m)
		{
			throw new InvalidOperationException("Invoice discount cannot be negative.");
		}
		if (input.DiscountAmount > input.Subtotal)
		{
			throw new InvalidOperationException("Invoice discount cannot exceed the subtotal.");
		}
	}

	private static ValidatedLine ValidateLine(PurchaseLineInput item, Drug drug)
	{
		if (string.IsNullOrWhiteSpace(item.BatchNo) || !item.ExpiryDate.HasValue || item.Quantity <= 0m || item.FreeQuantity < 0m || item.Mrp < 0m || item.Rate < 0m || item.GstRate < 0m || item.DiscountAmount < 0m || item.DiscountAmount > item.Quantity * item.Rate)
		{
			throw new InvalidOperationException("Purchase line for '" + drug.Name + "' has invalid batch, expiry, quantity, rate, GST, MRP or discount.");
		}
		if (item.ExpiryDate.Value < DateOnly.FromDateTime(DateTime.Today))
		{
			throw new InvalidOperationException("Expired batch " + item.BatchNo + " cannot be received into saleable stock.");
		}
		decimal num = RoundMoney(item.Quantity * item.Rate - item.DiscountAmount);
		decimal num2 = RoundMoney(num * item.GstRate / 100m);
		decimal lineTotal = RoundMoney(num + num2);
		if (RoundMoney(item.Amount) != num)
		{
			throw new InvalidOperationException("Purchase amount for '" + drug.Name + "' must equal quantity × rate less line discount.");
		}
		return new ValidatedLine(item, num, num2, lineTotal);
	}

	private async Task<CatalogInfo?> GetCatalogInfoAsync(Drug drug, CancellationToken cancellationToken)
	{
		if (drug.CatalogMedicineId.HasValue)
		{
			CatalogInfo catalogInfo = await unitOfWork.Context.CatalogInfos.FirstOrDefaultAsync((CatalogInfo info) => info.CatalogMedicineId == drug.CatalogMedicineId, cancellationToken);
			if (catalogInfo != null)
			{
				return catalogInfo;
			}
		}
		string nameKey = drug.Name.Trim().ToLowerInvariant();
		return await unitOfWork.Context.CatalogInfos.FirstOrDefaultAsync((CatalogInfo info) => info.NameKey == nameKey, cancellationToken);
	}

	private static string? GetControlledRegisterType(string? schedule, CatalogInfo? info)
	{
		string text = schedule?.Trim().ToUpperInvariant();
		bool flag;
		switch (text)
		{
		case "H1":
		case "X":
		case "NDPS":
			flag = true;
			break;
		default:
			flag = false;
			break;
		}
		if (flag)
		{
			return text;
		}
		if (info == null || !info.IsHabitForming)
		{
			return null;
		}
		return "HabitForming";
	}

	private static decimal RoundMoney(decimal amount)
	{
		return decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
	}

	private static string? NullIfWhiteSpace(string? value)
	{
		if (!string.IsNullOrWhiteSpace(value))
		{
			return value.Trim();
		}
		return null;
	}
}
