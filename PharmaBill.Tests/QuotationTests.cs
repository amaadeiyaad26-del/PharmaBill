using Microsoft.EntityFrameworkCore;
using PharmaBill.Core;
using PharmaBill.Core.Entities;
using PharmaBill.Core.Security;
using PharmaBill.Data.Persistence;
using PharmaBill.Data.Services;
using Xunit;

namespace PharmaBill.Tests;

public sealed class QuotationTests
{
	[Fact]
	public async Task RetailQuotation_IsSavedWithEstimate_AndTouchesNoStock()
	{
		await using var database = await DatabaseTestContext.CreateAsync();
		(Drug drug, Batch batch) = await SeedStockAsync(database);
		QuotationService service = CreateService(database);

		Quotation quotation = await service.CreateAsync(
			QuotationService.RetailMode,
			null,
			"Walk-in customer",
			null,
			null,
			new[] { new QuotationLineInput(drug.Id, null, drug.Name, 2m, 0m, 100m, 10m) },
			null,
			Guid.NewGuid(),
			UserRole.Owner);

		Assert.Equal(Quotation.OpenStatus, quotation.Status);
		Assert.StartsWith("WIN1-QT", quotation.QuoteNo);
		Assert.Equal(190m, quotation.EstimatedTotal);
		Assert.Single(await database.Context.QuotationItems.Where(item => item.QuotationId == quotation.Id).ToListAsync());

		Batch stored = await database.Context.Batches.SingleAsync(item => item.Id == batch.Id);
		Assert.Equal(5m, stored.Quantity);
		Assert.Equal(1, await database.Context.StockMovements.CountAsync());
	}

	[Fact]
	public async Task WholesaleQuotation_WithoutBatch_IsRejected()
	{
		await using var database = await DatabaseTestContext.CreateAsync();
		(Drug drug, _) = await SeedStockAsync(database);
		QuotationService service = CreateService(database);

		await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(
			QuotationService.WholesaleMode,
			Guid.NewGuid(),
			"Buyer",
			null,
			null,
			new[] { new QuotationLineInput(drug.Id, null, drug.Name, 1m, 0m, 100m, 0m) },
			null,
			Guid.NewGuid(),
			UserRole.Owner));
	}

	[Fact]
	public async Task ConvertedQuotation_CannotBeConvertedAgain_OrCancelled()
	{
		await using var database = await DatabaseTestContext.CreateAsync();
		(Drug drug, Batch batch) = await SeedStockAsync(database);
		QuotationService service = CreateService(database);
		Quotation quotation = await service.CreateAsync(
			QuotationService.WholesaleMode,
			Guid.NewGuid(),
			"Buyer",
			null,
			null,
			new[] { new QuotationLineInput(drug.Id, batch.Id, drug.Name, 1m, 0m, 100m, 0m) },
			null,
			Guid.NewGuid(),
			UserRole.Owner);

		await service.MarkConvertedAsync(quotation.Id, "WIN1-0001");
		Quotation converted = await database.Context.Quotations.AsNoTracking().SingleAsync(item => item.Id == quotation.Id);
		Assert.Equal(Quotation.ConvertedStatus, converted.Status);
		Assert.Equal("WIN1-0001", converted.ConvertedInvoiceNo);

		await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetOpenAsync(quotation.Id));
		await Assert.ThrowsAsync<InvalidOperationException>(() => service.CancelAsync(quotation.Id, Guid.NewGuid(), UserRole.Owner));
	}

	[Fact]
	public async Task Quotation_ValidityDateInThePast_IsRejected()
	{
		await using var database = await DatabaseTestContext.CreateAsync();
		(Drug drug, _) = await SeedStockAsync(database);
		QuotationService service = CreateService(database);

		await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(
			QuotationService.RetailMode,
			null,
			"Walk-in customer",
			null,
			DateOnly.FromDateTime(DateTime.Today.AddDays(-1)),
			new[] { new QuotationLineInput(drug.Id, null, drug.Name, 1m, 0m, 50m, 0m) },
			null,
			Guid.NewGuid(),
			UserRole.Owner));
	}

	[Fact]
	public async Task OpenQuotation_CanBeCancelled()
	{
		await using var database = await DatabaseTestContext.CreateAsync();
		(Drug drug, _) = await SeedStockAsync(database);
		QuotationService service = CreateService(database);
		Quotation quotation = await service.CreateAsync(
			QuotationService.RetailMode,
			null,
			"Walk-in customer",
			null,
			null,
			new[] { new QuotationLineInput(drug.Id, null, drug.Name, 1m, 0m, 50m, 0m) },
			null,
			Guid.NewGuid(),
			UserRole.Owner);

		await service.CancelAsync(quotation.Id, Guid.NewGuid(), UserRole.Owner);

		Quotation cancelled = await database.Context.Quotations.AsNoTracking().SingleAsync(item => item.Id == quotation.Id);
		Assert.Equal(Quotation.CancelledStatus, cancelled.Status);
	}

	private static QuotationService CreateService(DatabaseTestContext database)
	{
		var unitOfWork = database.CreateUnitOfWork();
		return new QuotationService(unitOfWork, new NumberSeriesService(unitOfWork));
	}

	private static async Task<(Drug Drug, Batch Batch)> SeedStockAsync(DatabaseTestContext database)
	{
		var profile = new PharmacyProfile
		{
			Name = "Test Pharmacy",
			BusinessMode = BusinessMode.Retail,
			InvoicePrefix = "WIN1"
		};
		var drug = new Drug { Name = "Test medicine", GstRate = 12m };
		var batch = new Batch
		{
			DrugId = drug.Id,
			BatchNo = "QT-1",
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
		return (drug, batch);
	}
}
