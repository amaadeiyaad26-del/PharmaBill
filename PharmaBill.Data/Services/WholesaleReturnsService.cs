using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PharmaBill.Core.Accounting;
using PharmaBill.Core.Compliance;
using PharmaBill.Core.Entities;
using PharmaBill.Core.Security;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Data.Services;

public sealed class WholesaleReturnsService(IUnitOfWork unitOfWork, IEntitlementService entitlementService)
{
	public async Task<ReturnNote> CreateCreditNoteAsync(Guid invoiceId, string returnNo, DateOnly returnDate, IReadOnlyList<WholesaleCreditLineInput> lines, string reason, Guid userId, UserRole role, CancellationToken cancellationToken = default(CancellationToken))
	{
		await DemandReturnAllowed(role, cancellationToken);
		ArgumentException.ThrowIfNullOrWhiteSpace(returnNo, "returnNo");
		ArgumentException.ThrowIfNullOrWhiteSpace(reason, "reason");
		if (returnDate > DateOnly.FromDateTime(DateTime.Today) || lines.Count == 0 || lines.Any((WholesaleCreditLineInput wholesaleCreditLineInput) => wholesaleCreditLineInput.Quantity <= 0m))
		{
			throw new InvalidOperationException("Credit note needs a non-future date, reason, and positive item quantities.");
		}
		PharmaBillDbContext context = unitOfWork.Context;
		WholesaleInvoice invoice = (await context.WholesaleInvoices.SingleOrDefaultAsync((WholesaleInvoice item) => item.Id == invoiceId && item.Status == "Posted", cancellationToken)) ?? throw new InvalidOperationException("Select a posted wholesale invoice.");
		string oldSnapshot = ComplianceAuditWriter.Snapshot(new { invoice.InvoiceNo, invoice.TotalAmount, invoice.PaidAmount, invoice.Status });
		RecordLockGuard.Demand(invoice.InvoiceAtUtc, "WholesaleInvoice", invoice.Id, "MODIFIED_AFTER_LOCK");
		if (await context.ReturnNotes.IgnoreQueryFilters().AnyAsync((ReturnNote item) => item.ReturnNo == returnNo.Trim(), cancellationToken))
		{
			throw new InvalidOperationException("That return number has already been used.");
		}
		WholesaleCreditLineInput[] grouped = (from wholesaleCreditLineInput in lines
			group wholesaleCreditLineInput by new { wholesaleCreditLineInput.InvoiceItemId, wholesaleCreditLineInput.Restock } into @group
			select new WholesaleCreditLineInput(@group.Key.InvoiceItemId, @group.Sum((WholesaleCreditLineInput wholesaleCreditLineInput) => wholesaleCreditLineInput.Quantity), @group.Key.Restock)).ToArray();
		Dictionary<Guid, WholesaleInvoiceItem> invoiceLines = await context.WholesaleInvoiceItems.Where((WholesaleInvoiceItem item) => item.WholesaleInvoiceId == invoice.Id).ToDictionaryAsync((WholesaleInvoiceItem item) => item.Id, cancellationToken);
		List<WholesaleReturnItem> source = await context.WholesaleReturnItems.Where((WholesaleReturnItem item) => grouped.Select((WholesaleCreditLineInput wholesaleCreditLineInput) => wholesaleCreditLineInput.InvoiceItemId).Contains(item.WholesaleInvoiceItemId)).ToListAsync(cancellationToken);
		WholesaleCreditLineInput[] array = grouped;
		foreach (WholesaleCreditLineInput line in array)
		{
			if (!invoiceLines.TryGetValue(line.InvoiceItemId, out var value))
			{
				throw new InvalidOperationException("A credit-note item does not belong to the selected invoice.");
			}
			decimal num2 = source.Where((WholesaleReturnItem item) => item.WholesaleInvoiceItemId == line.InvoiceItemId).Sum((WholesaleReturnItem item) => item.Quantity);
			if (line.Quantity + num2 > value.Quantity)
			{
				throw new InvalidOperationException("Credit-note quantity exceeds the sold paid quantity for its original batch.");
			}
		}
		decimal creditTotal = grouped.Sum((WholesaleCreditLineInput wholesaleCreditLineInput) =>
		{
			WholesaleInvoiceItem wholesaleInvoiceItem = invoiceLines[wholesaleCreditLineInput.InvoiceItemId];
			return RoundMoney(((wholesaleInvoiceItem.Quantity == 0m) ? 0m : (wholesaleInvoiceItem.LineTotal / wholesaleInvoiceItem.Quantity)) * wholesaleCreditLineInput.Quantity);
		});
		ReturnNote result;
		await using (IUnitOfWorkTransaction transaction = await unitOfWork.BeginTransactionAsync(cancellationToken))
		{
			ReturnNote note = new ReturnNote
			{
				ReturnNo = returnNo.Trim(),
				SourceType = "WholesaleCreditNote",
				SourceId = invoice.Id,
				ReturnAtUtc = DateTime.UtcNow,
				TotalAmount = creditTotal,
				Reason = reason.Trim(),
				Notes = "Against invoice " + invoice.InvoiceNo + "; quarantined returns remain outside saleable stock."
			};
			context.ReturnNotes.Add(note);
			WholesaleCreditLineInput[] array2 = grouped;
			foreach (WholesaleCreditLineInput line2 in array2)
			{
				WholesaleInvoiceItem invoiceLine = invoiceLines[line2.InvoiceItemId];
				Batch batch = await context.Batches.SingleAsync((Batch item) => item.Id == invoiceLine.BatchId, cancellationToken);
				decimal creditAmount = RoundMoney(invoiceLine.LineTotal / invoiceLine.Quantity * line2.Quantity);
				WholesaleReturnItem entity = new WholesaleReturnItem
				{
					ReturnNoteId = note.Id,
					WholesaleInvoiceItemId = invoiceLine.Id,
					BatchId = invoiceLine.BatchId,
					DrugId = invoiceLine.DrugId,
					Quantity = line2.Quantity,
					CreditAmount = creditAmount,
					Restocked = line2.Restock,
					Quarantined = !line2.Restock
				};
				context.WholesaleReturnItems.Add(entity);
				if (line2.Restock)
				{
					batch.Quantity += line2.Quantity;
					context.StockMovements.Add(new StockMovement
					{
						BatchId = batch.Id,
						DrugId = invoiceLine.DrugId,
						QuantityChange = line2.Quantity,
						MovementType = "WholesaleCreditReturnRestocked",
						ReferenceType = "ReturnNote",
						ReferenceId = note.Id,
						Notes = reason.Trim()
					});
				}
			}
			context.CustomerLedgerEntries.Add(new CustomerLedgerEntry
			{
				CustomerId = invoice.CustomerId,
				EntryAtUtc = note.ReturnAtUtc,
				EntryType = LedgerEntryTypes.WholesaleCreditNote,
				ReferenceId = invoice.Id,
				ReferenceNo = note.ReturnNo,
				Debit = 0m,
				Credit = creditTotal,
				Notes = "Credit note " + note.ReturnNo + " against " + invoice.InvoiceNo
			});
			context.AuditLogs.Add(new AuditLog
			{
				UserId = userId,
				ActionAtUtc = note.ReturnAtUtc,
				Action = "WholesaleCreditNoteCreated",
				EntityName = "ReturnNote",
				EntityId = note.Id,
				Details = $"{note.ReturnNo}; invoice {invoice.InvoiceNo}; amount {creditTotal}; {grouped.Length} lines."
			});
			await ComplianceAuditWriter.AppendIfUnlockedAsync(context, invoice.InvoiceAtUtc, new ComplianceAuditEntry
			{
				EntityType = "WholesaleInvoice",
				EntityId = invoice.Id,
				Action = "MODIFIED_AFTER_LOCK",
				AuthorizedBy = string.Empty,
				Reason = reason.Trim(),
				OldSnapshotJson = oldSnapshot,
				NewSnapshotJson = ComplianceAuditWriter.Snapshot(new { invoice.InvoiceNo, note.ReturnNo, creditTotal, note.Reason }),
				UserId = userId
			}, cancellationToken);
			await unitOfWork.SaveChangesAsync(cancellationToken);
			await transaction.CommitAsync(cancellationToken);
			result = note;
		}
		return result;
	}

	public async Task<ReturnNote> CreateDebitNoteAsync(Guid customerId, string debitNoteNo, DateOnly date, decimal amount, string reason, Guid userId, UserRole role, CancellationToken cancellationToken = default(CancellationToken))
	{
		await DemandReturnAllowed(role, cancellationToken);
		ArgumentException.ThrowIfNullOrWhiteSpace(debitNoteNo, "debitNoteNo");
		ArgumentException.ThrowIfNullOrWhiteSpace(reason, "reason");
		if (amount <= 0m || date > DateOnly.FromDateTime(DateTime.Today))
		{
			throw new InvalidOperationException("Debit note requires a positive amount and a non-future date.");
		}
		PharmaBillDbContext context = unitOfWork.Context;
		Customer customer = (await context.Customers.SingleOrDefaultAsync((Customer item) => item.Id == customerId, cancellationToken)) ?? throw new InvalidOperationException("Customer not found.");
		if (await context.ReturnNotes.IgnoreQueryFilters().AnyAsync((ReturnNote item) => item.ReturnNo == debitNoteNo.Trim(), cancellationToken))
		{
			throw new InvalidOperationException("That debit note number has already been used.");
		}
		ReturnNote result;
		await using (IUnitOfWorkTransaction transaction = await unitOfWork.BeginTransactionAsync(cancellationToken))
		{
			ReturnNote note = new ReturnNote
			{
				ReturnNo = debitNoteNo.Trim(),
				SourceType = "WholesaleDebitNote",
				SourceId = customer.Id,
				ReturnAtUtc = DateTime.UtcNow,
				TotalAmount = RoundMoney(amount),
				Reason = reason.Trim()
			};
			context.ReturnNotes.Add(note);
			context.CustomerLedgerEntries.Add(new CustomerLedgerEntry
			{
				CustomerId = customer.Id,
				EntryAtUtc = note.ReturnAtUtc,
				EntryType = LedgerEntryTypes.WholesaleDebitNote,
				ReferenceId = note.Id,
				ReferenceNo = note.ReturnNo,
				Debit = note.TotalAmount,
				Credit = 0m,
				Notes = note.Reason
			});
			context.AuditLogs.Add(new AuditLog
			{
				UserId = userId,
				ActionAtUtc = note.ReturnAtUtc,
				Action = "WholesaleDebitNoteCreated",
				EntityName = "ReturnNote",
				EntityId = note.Id,
				Details = $"{note.ReturnNo}; customer {customer.Name}; amount {note.TotalAmount}."
			});
			await unitOfWork.SaveChangesAsync(cancellationToken);
			await transaction.CommitAsync(cancellationToken);
			result = note;
		}
		return result;
	}

	public async Task<IReadOnlyList<ExpiryReturnTrackerRow>> GetExpiryReturnTrackerAsync(DateOnly through, CancellationToken cancellationToken = default(CancellationToken))
	{
		List<Batch> batches = await (from batch in unitOfWork.Context.Batches.AsNoTracking()
			where batch.ExpiryDate.HasValue && batch.ExpiryDate.Value <= through
			select batch).ToListAsync(cancellationToken);
		Guid[] drugIds = batches.Select((Batch batch) => batch.DrugId).Distinct().ToArray();
		Guid[] supplierIds = (from batch in batches
			where batch.SupplierId.HasValue
			select batch.SupplierId.Value).Distinct().ToArray();
		Dictionary<Guid, Drug> drugs = await (from drug in unitOfWork.Context.Drugs.AsNoTracking()
			where drugIds.Contains(drug.Id)
			select drug).ToDictionaryAsync((Drug drug) => drug.Id, cancellationToken);
		Dictionary<Guid, string> suppliers = await (from supplier in unitOfWork.Context.Suppliers.AsNoTracking()
			where supplierIds.Contains(supplier.Id)
			select supplier).ToDictionaryAsync((Supplier supplier) => supplier.Id, (Supplier supplier) => supplier.Name, cancellationToken);
		Guid[] batchIds = batches.Select((Batch batch) => batch.Id).ToArray();
		Dictionary<Guid, decimal> stock = await (from movement in unitOfWork.Context.StockMovements.AsNoTracking()
			where batchIds.Contains(movement.BatchId)
			group movement by movement.BatchId into @group
			select new
			{
				BatchId = @group.Key,
				Quantity = @group.Sum((StockMovement movement) => movement.QuantityChange)
			}).ToDictionaryAsync(item => item.BatchId, item => item.Quantity, cancellationToken);
		return (from batch in batches
			select new ExpiryReturnTrackerRow(batch.Id, drugs[batch.DrugId].Name, batch.BatchNo, batch.ExpiryDate, stock.GetValueOrDefault(batch.Id), batch.PurchasePrice, batch.SupplierId.HasValue ? suppliers.GetValueOrDefault(batch.SupplierId.Value, string.Empty) : string.Empty) into row
			where row.OnHand > 0m
			orderby row.ExpiryDate
			select row).ToArray();
	}

	private async Task DemandReturnAllowed(UserRole role, CancellationToken cancellationToken)
	{
		if (!PermissionMatrix.Allows(role, AppPermission.ProcessReturn))
		{
			throw new UnauthorizedAccessException("Your role cannot process wholesale returns.");
		}
		if (!(await entitlementService.CanPerformAsync(ProtectedOperation.ProcessReturn, cancellationToken)))
		{
			throw new InvalidOperationException("Returns are unavailable in read-only mode.");
		}
	}

	private static decimal RoundMoney(decimal amount)
	{
		return decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
	}
}
