using Microsoft.EntityFrameworkCore;
using System.Threading;
using System.Threading.Tasks;
using PharmaBill.Core;
using PharmaBill.Core.Compliance;
using PharmaBill.Core.Entities;
using PharmaBill.Core.Security;
using PharmaBill.Data.Services;
using Xunit;

namespace PharmaBill.Tests;

public sealed class RetailConsumerTests
{
	private sealed class HealthEntitlements : IEntitlementService
	{
		public Task<EntitlementStatus> GetStatusAsync(CancellationToken cancellationToken = default) =>
			Task.FromResult(EntitlementStatus.TRIAL);

		public Task<bool> IsReadOnlyAsync(CancellationToken cancellationToken = default) =>
			Task.FromResult(false);

		public Task<bool> CanPerformAsync(ProtectedOperation operation, CancellationToken cancellationToken = default) =>
			Task.FromResult(true);
	}

	[Fact]
	public async Task RetailBill_AddToCustomers_CreatesOneConsumerPerPhone()
	{
		await using var database = await DatabaseTestContext.CreateAsync();
		var catalog = await E2EHealthSeed.SeedAsync(database.Context);
		var retail = database.CreateRetailBillingService(new HealthEntitlements());
		const string phone = "9111122233";

		for (var i = 0; i < 2; i++)
		{
			await retail.SaveSaleAsync(
				new SaveRetailSaleInput(
					"Consumer Test",
					phone,
					"Ward 2",
					null,
					null,
					null,
					[new RetailSaleLineInput(catalog.Paracetamol650.Id, catalog.ParacetamolBatch.Id, 1m, 0m, 30m)],
					[new RetailPaymentInput("Cash", 30m)],
					false,
					AddToCustomers: true),
				catalog.Admin.Id,
				UserRole.Pharmacist);
		}

		Customer consumer = Assert.Single(await database.Context.Customers.Where(customer => customer.Phone == phone).ToListAsync());
		Assert.Equal("Consumer Test", consumer.Name);
		Assert.Equal("Walk-in", consumer.BuyerType);
		Assert.Equal("Ward 2", consumer.Address);
	}

	[Fact]
	public async Task RetailBill_WithoutAddToCustomers_CreatesNoConsumer()
	{
		await using var database = await DatabaseTestContext.CreateAsync();
		var catalog = await E2EHealthSeed.SeedAsync(database.Context);
		var retail = database.CreateRetailBillingService(new HealthEntitlements());
		const string phone = "9111122244";

		await retail.SaveSaleAsync(
			new SaveRetailSaleInput(
				"Not Added",
				phone,
				null,
				null,
				null,
				null,
				[new RetailSaleLineInput(catalog.Paracetamol650.Id, catalog.ParacetamolBatch.Id, 1m, 0m, 30m)],
				[new RetailPaymentInput("Cash", 30m)],
				false,
				AddToCustomers: false),
			catalog.Admin.Id,
			UserRole.Pharmacist);

		Assert.Empty(await database.Context.Customers.Where(customer => customer.Phone == phone).ToListAsync());
	}
}
