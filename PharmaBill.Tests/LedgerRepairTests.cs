using Microsoft.EntityFrameworkCore;
using PharmaBill.Core.Accounting;
using PharmaBill.Core.Entities;
using PharmaBill.Data.Services;
using Xunit;

namespace PharmaBill.Tests;

public sealed class LedgerRepairTests
{
	[Fact]
	public async Task SyncOrphanedInvoicesToLedger_PostsMissingPurchaseBillCredits()
	{
		await using var database = await DatabaseTestContext.CreateAsync();
		var supplier = new Supplier { Name = "General Supplier" };
		database.Context.Suppliers.Add(supplier);
		await database.Context.SaveChangesAsync();

		var invoice = new PurchaseInvoice
		{
			SupplierId = supplier.Id,
			InvoiceNo = "GS-31815",
			InvoiceDate = DateOnly.FromDateTime(DateTime.Today),
			Subtotal = 30000m,
			TaxAmount = 1815m,
			TotalAmount = 31815m,
			Status = PurchaseInvoice.CommittedStatus
		};
		database.Context.PurchaseInvoices.Add(invoice);
		database.Context.SupplierLedgerEntries.Add(new SupplierLedgerEntry
		{
			SupplierId = supplier.Id,
			EntryAtUtc = DateTime.UtcNow,
			EntryType = LedgerEntryTypes.SupplierPayment,
			ReferenceNo = "PAY-1",
			Debit = 5000m,
			Credit = 0m,
			Notes = "Partial payment"
		});
		await database.Context.SaveChangesAsync();

		int added = await new LedgerRepairService(database.Context).SyncOrphanedInvoicesToLedgerAsync();
		Assert.Equal(1, added);

		SupplierLedgerEntry bill = Assert.Single(
			await database.Context.SupplierLedgerEntries
				.Where(entry => entry.EntryType == LedgerEntryTypes.PurchaseBill)
				.ToListAsync());
		Assert.Equal(31815m, bill.Credit);
		Assert.Equal(0m, bill.Debit);
		Assert.Equal("GS-31815", bill.ReferenceNo);
		Assert.Equal(invoice.Id, bill.ReferenceId);

		int secondPass = await new LedgerRepairService(database.Context).SyncOrphanedInvoicesToLedgerAsync();
		Assert.Equal(0, secondPass);

		decimal credit = await database.Context.SupplierLedgerEntries
			.Where(entry => entry.SupplierId == supplier.Id)
			.SumAsync(entry => entry.Credit);
		decimal debit = await database.Context.SupplierLedgerEntries
			.Where(entry => entry.SupplierId == supplier.Id)
			.SumAsync(entry => entry.Debit);
		Assert.Equal(31815m - 5000m, credit - debit);
	}
}
