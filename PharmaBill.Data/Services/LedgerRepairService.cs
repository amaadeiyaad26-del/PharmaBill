using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PharmaBill.Core.Accounting;
using PharmaBill.Core.Entities;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Data.Services;

/// <summary>
/// One-time / startup repair that posts missing SupplierLedger credits for committed purchase bills.
/// </summary>
public sealed class LedgerRepairService(PharmaBillDbContext context)
{
	/// <summary>
	/// For every PurchaseInvoice that has no matching SupplierLedger row (by ReferenceId or ReferenceNo),
	/// insert a PurchaseBill credit so payables balance against existing supplier payments.
	/// Soft-delete filter applies; never hard-deletes. Idempotent.
	/// </summary>
	public async Task<int> SyncOrphanedInvoicesToLedgerAsync(CancellationToken cancellationToken = default)
	{
		var invoices = await context.PurchaseInvoices.AsNoTracking()
			.Select(invoice => new { invoice.Id, invoice.SupplierId, invoice.InvoiceNo, invoice.InvoiceDate, invoice.TotalAmount })
			.ToListAsync(cancellationToken);
		if (invoices.Count == 0)
		{
			return 0;
		}

		var existing = await context.SupplierLedgerEntries.AsNoTracking()
			.Where(entry => entry.EntryType == LedgerEntryTypes.PurchaseBill
				|| entry.EntryType == LedgerEntryTypes.PurchaseInvoiceLegacy)
			.Select(entry => new { entry.SupplierId, entry.ReferenceId, entry.ReferenceNo })
			.ToListAsync(cancellationToken);

		HashSet<(Guid SupplierId, Guid InvoiceId)> byId = existing
			.Where(entry => entry.ReferenceId.HasValue)
			.Select(entry => (entry.SupplierId, entry.ReferenceId!.Value))
			.ToHashSet();
		HashSet<(Guid SupplierId, string InvoiceNo)> byNo = existing
			.Where(entry => !string.IsNullOrWhiteSpace(entry.ReferenceNo))
			.Select(entry => (entry.SupplierId, entry.ReferenceNo!.Trim()))
			.ToHashSet();

		int added = 0;
		foreach (var invoice in invoices)
		{
			string invoiceNo = invoice.InvoiceNo.Trim();
			if (byId.Contains((invoice.SupplierId, invoice.Id))
				|| byNo.Contains((invoice.SupplierId, invoiceNo)))
			{
				continue;
			}

			decimal credit = decimal.Round(invoice.TotalAmount, 2, MidpointRounding.AwayFromZero);
			context.SupplierLedgerEntries.Add(new SupplierLedgerEntry
			{
				SupplierId = invoice.SupplierId,
				EntryAtUtc = invoice.InvoiceDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
				EntryType = LedgerEntryTypes.PurchaseBill,
				ReferenceId = invoice.Id,
				ReferenceNo = invoice.InvoiceNo,
				Debit = 0m,
				Credit = credit,
				Notes = "Backfilled purchase bill " + invoiceNo
			});
			byId.Add((invoice.SupplierId, invoice.Id));
			byNo.Add((invoice.SupplierId, invoiceNo));
			added++;
		}

		if (added > 0)
		{
			await context.SaveChangesAsync(cancellationToken);
		}

		return added;
	}
}
