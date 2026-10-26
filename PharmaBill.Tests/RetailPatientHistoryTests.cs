using PharmaBill.Core.Entities;
using PharmaBill.Core.Security;
using PharmaBill.Data.Services;
using Xunit;

namespace PharmaBill.Tests;

public sealed class RetailPatientHistoryTests
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
	public async Task SavedPatient_IsFoundByPhone_AndHistoryListsEveryBill()
	{
		await using var database = await DatabaseTestContext.CreateAsync();
		var catalog = await E2EHealthSeed.SeedAsync(database.Context);
		var retail = database.CreateRetailBillingService(new HealthEntitlements());
		const string phone = "9111122255";

		for (var i = 0; i < 2; i++)
		{
			await retail.SaveSaleAsync(
				new SaveRetailSaleInput(
					"History Test",
					phone,
					null,
					null,
					null,
					null,
					[new RetailSaleLineInput(catalog.Paracetamol650.Id, catalog.ParacetamolBatch.Id, 1m, 0m, 30m)],
					[new RetailPaymentInput("Cash", 30m)],
					false),
				catalog.Admin.Id,
				UserRole.Pharmacist);
		}

		RetailPatientMatch match = Assert.Single(await retail.SearchPatientsByPhoneAsync("91111"));
		Assert.Equal("History Test", match.Name);
		Assert.Equal(match.PatientId, await retail.FindPatientIdByPhoneAsync(phone));

		var history = await retail.GetPatientHistoryAsync(match.PatientId);
		Assert.Equal(2, history.Count);
		Assert.All(history, bill => Assert.Single(bill.Lines));
		Assert.Equal(catalog.Paracetamol650.Id, history[0].Lines[0].DrugId);
		Assert.Equal(1m, history[0].Lines[0].Quantity);
	}

	[Fact]
	public async Task PhoneSearch_NeedsFourDigits()
	{
		await using var database = await DatabaseTestContext.CreateAsync();
		await E2EHealthSeed.SeedAsync(database.Context);
		var retail = database.CreateRetailBillingService(new HealthEntitlements());

		Assert.Empty(await retail.SearchPatientsByPhoneAsync("911"));
	}
}
