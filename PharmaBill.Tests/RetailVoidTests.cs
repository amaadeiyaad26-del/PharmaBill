using Microsoft.EntityFrameworkCore;
using PharmaBill.Core;
using PharmaBill.Core.Entities;
using PharmaBill.Core.Security;
using PharmaBill.Data.Persistence;
using PharmaBill.Data.Services;
using Xunit;

namespace PharmaBill.Tests;

/// <summary>A voided retail bill must reverse stock, ledger and cash, leaving nothing orphaned.</summary>
public sealed class RetailVoidTests
{
	[Fact]
	public async Task VoidCashSale_RestocksItems_RefundsCash_AndLeavesNoDanglingReceipt()
	{
		await using var database = await DatabaseTestContext.CreateAsync();
		(RetailBillingService service, Drug drug, Batch batch) = await CreateServiceAsync(database);
		var input = NewSaleInput(drug, batch) with { Payments = [new RetailPaymentInput("Cash", 100m, 100m)] };
		var sale = await service.SaveSaleAsync(input, Guid.NewGuid(), UserRole.Owner);

		await service.VoidSaleAsync(sale.Sale.Id, Guid.NewGuid(), UserRole.Owner, "Entered on wrong patient");

		Assert.Equal(5m, await StockOnHandAsync(database, batch.Id));
		Assert.Empty(await database.Context.Sales.ToListAsync());
		Sale hidden = await database.Context.Sales.IgnoreQueryFilters().SingleAsync();
		Assert.True(hidden.IsDeleted);

		ReturnNote credit = await database.Context.ReturnNotes.SingleAsync();
		Assert.Equal(100m, credit.TotalAmount);
		Assert.Equal("Sale", credit.SourceType);

		Receipt refund = await database.Context.Receipts.SingleAsync(receipt => receipt.PaymentMethod == "Refund");
		Assert.Equal(100m, refund.Amount);
		decimal cashIn = await database.Context.Receipts
			.Where(receipt => receipt.PaymentMethod == "Cash")
			.SumAsync(receipt => receipt.Amount);
		decimal refunded = await database.Context.Receipts
			.Where(receipt => receipt.PaymentMethod == "Refund")
			.SumAsync(receipt => receipt.Amount);
		Assert.Equal(cashIn, refunded);
	}

	[Fact]
	public async Task VoidCreditSale_ClearsCustomerReceivable()
	{
		await using var database = await DatabaseTestContext.CreateAsync();
		(RetailBillingService service, Drug drug, Batch batch) = await CreateServiceAsync(database);
		var sale = await service.SaveSaleAsync(NewSaleInput(drug, batch), Guid.NewGuid(), UserRole.Owner);
		Assert.Equal(100m, await database.Context.CustomerLedgerEntries.SumAsync(entry => entry.Debit - entry.Credit));

		await service.VoidSaleAsync(sale.Sale.Id, Guid.NewGuid(), UserRole.Owner, "Patient did not take the medicine");

		Assert.Equal(0m, await database.Context.CustomerLedgerEntries.SumAsync(entry => entry.Debit - entry.Credit));
		Assert.Equal(5m, await StockOnHandAsync(database, batch.Id));
		Assert.Empty(await database.Context.Receipts.Where(receipt => receipt.PaymentMethod == "Refund").ToListAsync());
	}

	[Fact]
	public async Task VoidedBill_CannotBeVoidedAgain()
	{
		await using var database = await DatabaseTestContext.CreateAsync();
		(RetailBillingService service, Drug drug, Batch batch) = await CreateServiceAsync(database);
		var sale = await service.SaveSaleAsync(NewSaleInput(drug, batch), Guid.NewGuid(), UserRole.Owner);
		await service.VoidSaleAsync(sale.Sale.Id, Guid.NewGuid(), UserRole.Owner, "First void");

		await Assert.ThrowsAsync<InvalidOperationException>(() =>
			service.VoidSaleAsync(sale.Sale.Id, Guid.NewGuid(), UserRole.Owner, "Second void"));
		Assert.Equal(5m, await StockOnHandAsync(database, batch.Id));
	}

	private static async Task<decimal> StockOnHandAsync(DatabaseTestContext database, Guid batchId) =>
		await database.Context.StockMovements.Where(movement => movement.BatchId == batchId).SumAsync(movement => movement.QuantityChange);

	private static SaveRetailSaleInput NewSaleInput(Drug drug, Batch batch) =>
		new(
			"Test Patient",
			"9876543210",
			null,
			null,
			null,
			null,
			[new RetailSaleLineInput(drug.Id, batch.Id, 1m, 0m, 100m)],
			[],
			false);

	private static async Task<(RetailBillingService Service, Drug Drug, Batch Batch)> CreateServiceAsync(DatabaseTestContext database)
	{
		var profile = new PharmacyProfile
		{
			Name = "Test Pharmacy",
			BusinessMode = BusinessMode.Retail,
			InvoicePrefix = "WIN1"
		};
		var drug = new Drug { Name = "Void test medicine", GstRate = 12m, HsnCode = "3004" };
		var batch = new Batch
		{
			DrugId = drug.Id,
			BatchNo = "VOID-1",
			ExpiryDate = DateOnly.FromDateTime(DateTime.Today.AddMonths(6)),
			Quantity = 5m,
			Mrp = 100m,
			SalePrice = 95m
		};
		database.Context.AddRange(profile, drug, batch);
		database.Context.StockMovements.Add(new StockMovement
		{
			BatchId = batch.Id,
			DrugId = drug.Id,
			QuantityChange = 5m,
			MovementType = "PurchaseReceipt"
		});
		await database.Context.SaveChangesAsync();
		return (database.CreateRetailBillingService(new TestEntitlements()), drug, batch);
	}

	private sealed class TestEntitlements : IEntitlementService
	{
		public Task<EntitlementStatus> GetStatusAsync(CancellationToken cancellationToken = default) =>
			Task.FromResult(EntitlementStatus.TRIAL);

		public Task<bool> IsReadOnlyAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);

		public Task<bool> CanPerformAsync(ProtectedOperation operation, CancellationToken cancellationToken = default) => Task.FromResult(true);
	}
}
