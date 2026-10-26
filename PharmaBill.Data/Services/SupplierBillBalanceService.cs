using Microsoft.EntityFrameworkCore;
using PharmaBill.Core.Accounting;
using PharmaBill.Core.Entities;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Data.Services;

/// <summary>A purchase bill that still has something owing on it.</summary>
public sealed record OpenSupplierBill(Guid PurchaseId, string BillNo, DateOnly BillDate, decimal Total, decimal Due);

/// <summary>
/// What is still owed on each purchase bill, from the supplier ledger. Payments the pharmacist pointed at particular bills
/// settle those bills; every other payment, return or debit settles the supplier's oldest dues first (first in, first out).
/// Used by Reports, Purchase history and the supplier payment screen, so they always agree.
/// </summary>
public sealed class SupplierBillBalanceService(PharmaBillDbContext context)
{
	public const string Paid = "Paid";
	public const string PartiallyPaid = "Partially paid";
	public const string Unpaid = "Unpaid";
	public const string NotInLedger = "Not in ledger";

	/// <summary>Paid, Partially paid or Unpaid from what is due; a bill missing from the ledger has no due.</summary>
	public static string StatusOf(decimal? due, decimal total)
	{
		if (!due.HasValue)
		{
			return NotInLedger;
		}
		if (due.Value <= 0.005m)
		{
			return Paid;
		}
		return due.Value >= total - 0.005m ? Unpaid : PartiallyPaid;
	}

	/// <summary>Amount still due on each purchase bill of these suppliers that is in the supplier ledger.</summary>
	public async Task<Dictionary<Guid, decimal>> GetBalancesAsync(IReadOnlyCollection<Guid> supplierIds, CancellationToken cancellationToken = default)
	{
		List<SupplierLedgerEntry> entries = await context.SupplierLedgerEntries.AsNoTracking()
			.Where(entry => supplierIds.Contains(entry.SupplierId))
			.ToListAsync(cancellationToken);
		Guid[] debitIds = entries.Where(entry => entry.Debit > 0m).Select(entry => entry.Id).ToArray();
		List<SupplierPaymentAllocation> allocations = await context.SupplierPaymentAllocations.AsNoTracking()
			.Where(item => debitIds.Contains(item.PaymentEntryId))
			.ToListAsync(cancellationToken);
		return Settle(entries, allocations);
	}

	/// <summary>The supplier's bills with something still due, oldest first: what the payment screen offers to pay.</summary>
	public async Task<IReadOnlyList<OpenSupplierBill>> GetOpenBillsAsync(Guid supplierId, CancellationToken cancellationToken = default)
	{
		Dictionary<Guid, decimal> due = await GetBalancesAsync([supplierId], cancellationToken);
		Guid[] open = due.Where(pair => pair.Value > 0.005m).Select(pair => pair.Key).ToArray();
		List<PurchaseInvoice> bills = await context.PurchaseInvoices.AsNoTracking()
			.Where(invoice => open.Contains(invoice.Id))
			.ToListAsync(cancellationToken);
		return bills
			.OrderBy(invoice => invoice.InvoiceDate).ThenBy(invoice => invoice.CreatedAtUtc)
			.Select(invoice => new OpenSupplierBill(invoice.Id, invoice.InvoiceNo, invoice.InvoiceDate, invoice.TotalAmount, due[invoice.Id]))
			.ToArray();
	}

	internal static Dictionary<Guid, decimal> Settle(IEnumerable<SupplierLedgerEntry> entries, IReadOnlyCollection<SupplierPaymentAllocation> allocations)
	{
		var balances = new Dictionary<Guid, decimal>();
		foreach (IGrouping<Guid, SupplierLedgerEntry> supplier in entries.GroupBy(entry => entry.SupplierId))
		{
			// Every amount the supplier is owed, oldest first; a purchase bill is keyed by its purchase.
			var dues = supplier
				.Where(entry => entry.Credit > 0m)
				.OrderBy(entry => entry.EntryAtUtc).ThenBy(entry => entry.CreatedAtUtc)
				.Select(entry => new Due(entry, IsBill(entry) ? entry.ReferenceId : null, entry.Credit))
				.ToList();
			Dictionary<Guid, Due> byBill = dues.Where(due => due.PurchaseId.HasValue).GroupBy(due => due.PurchaseId!.Value).ToDictionary(group => group.Key, group => group.First());
			decimal free = 0m;
			foreach (SupplierLedgerEntry debit in supplier.Where(entry => entry.Debit > 0m).OrderBy(entry => entry.EntryAtUtc).ThenBy(entry => entry.CreatedAtUtc))
			{
				decimal unallocated = debit.Debit;
				// The pharmacist's choice first: the bills this payment was pointed at.
				foreach (SupplierPaymentAllocation allocation in allocations.Where(item => item.PaymentEntryId == debit.Id))
				{
					if (byBill.TryGetValue(allocation.PurchaseInvoiceId, out Due? target))
					{
						decimal applied = Math.Min(Math.Min(allocation.Amount, unallocated), target.Remaining);
						target.Remaining -= applied;
						unallocated -= applied;
					}
				}
				free += unallocated;
			}
			// Everything else settles the oldest dues first.
			foreach (Due due in dues)
			{
				decimal applied = Math.Min(free, due.Remaining);
				due.Remaining -= applied;
				free -= applied;
			}
			foreach (Due due in dues.Where(due => due.PurchaseId.HasValue))
			{
				balances[due.PurchaseId!.Value] = balances.GetValueOrDefault(due.PurchaseId.Value) + due.Remaining;
			}
		}
		return balances;
	}

	private static bool IsBill(SupplierLedgerEntry entry) =>
		entry.EntryType is LedgerEntryTypes.PurchaseBill or LedgerEntryTypes.PurchaseInvoiceLegacy && entry.ReferenceId.HasValue;

	private sealed class Due(SupplierLedgerEntry entry, Guid? purchaseId, decimal remaining)
	{
		public SupplierLedgerEntry Entry { get; } = entry;
		public Guid? PurchaseId { get; } = purchaseId;
		public decimal Remaining { get; set; } = remaining;
	}
}
