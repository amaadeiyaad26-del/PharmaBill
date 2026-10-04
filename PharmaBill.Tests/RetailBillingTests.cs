using Microsoft.EntityFrameworkCore;
using PharmaBill.Core;
using PharmaBill.Core.Entities;
using PharmaBill.Core.Security;
using PharmaBill.Data.Persistence;
using PharmaBill.Data.Services;
using Xunit;

namespace PharmaBill.Tests;

public sealed class RetailBillingTests
{
    [Fact]
    public void InclusiveGst_SplitsMprInclusivePriceAndRoundsPerLine()
    {
        var split = RetailTaxCalculator.SplitInclusive(1m, 112m, 12m, 0m);
        Assert.Equal(112m, split.GrossAmount);
        Assert.Equal(12m, split.TaxAmount);
        Assert.Equal(100m, split.NetAmount);

        var rounded = RetailTaxCalculator.SplitInclusive(1m, 0.05m, 5m, 0m);
        Assert.Equal(0.05m, rounded.GrossAmount);
        Assert.Equal(0m, rounded.TaxAmount);
        Assert.Equal(0.05m, rounded.NetAmount);
    }

    [Fact]
    public async Task SaveSale_RequiresPatientNameAndPhone()
    {
        await using var database = await DatabaseTestContext.CreateAsync();
        var (service, drug, batch) = await CreateServiceAsync(database);
        var input = NewSaleInput(drug, batch) with { PatientName = " ", PatientPhone = "1234567890" };

        var exception = await Assert.ThrowsAnyAsync<ArgumentException>(() =>
            service.SaveSaleAsync(input, Guid.NewGuid(), UserRole.Owner));

        Assert.Contains("PatientName", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await database.Context.Sales.ToListAsync());
    }

    [Fact]
    public async Task SaveSale_H1RequiresAddressPrescriberRegistrationAndPrescriptionDocument()
    {
        await using var database = await DatabaseTestContext.CreateAsync();
        var (service, drug, batch) = await CreateServiceAsync(database, schedule: "H1");

        var exception = await Assert.ThrowsAnyAsync<ArgumentException>(() =>
            service.SaveSaleAsync(NewSaleInput(drug, batch), Guid.NewGuid(), UserRole.Owner));

        Assert.Contains("PatientAddress", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await database.Context.Sales.ToListAsync());
    }

    [Fact]
    public async Task SaveSale_H1RequiresPrescriberAndPrescriptionAttachment()
    {
        await using var database = await DatabaseTestContext.CreateAsync();
        var (service, drug, batch) = await CreateServiceAsync(database, schedule: "H1");
        var input = NewSaleInput(drug, batch) with
        {
            PatientAddress = "1 Pharmacy Road",
            PrescriberName = "Dr Test"
        };

        var prescriberException = await Assert.ThrowsAnyAsync<ArgumentException>(() =>
            service.SaveSaleAsync(input, Guid.NewGuid(), UserRole.Owner));
        Assert.Contains("PrescriberRegistrationNumber", prescriberException.Message, StringComparison.OrdinalIgnoreCase);

        input = input with
        {
            PrescriberName = "Dr Test",
            PrescriberRegistrationNumber = "REG-TEST"
        };
        var attachmentException = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SaveSaleAsync(input, Guid.NewGuid(), UserRole.Owner));
        Assert.Contains("Attach a prescription", attachmentException.Message);
    }

    [Fact]
    public async Task SaveSale_H1PersistsPrescriptionDocumentAndScheduleRegisterEntry()
    {
        await using var database = await DatabaseTestContext.CreateAsync();
        var storageDirectory = Path.Combine(Path.GetTempPath(), $"PharmaBill.Prescriptions.{Guid.NewGuid():N}");
        Directory.CreateDirectory(storageDirectory);
        var sourcePath = Path.Combine(storageDirectory, "source.jpg");
        await File.WriteAllBytesAsync(sourcePath, [1, 2, 3, 4]);
        var (service, drug, batch) = await CreateServiceAsync(
            database,
            schedule: "H1",
            prescriptionStorageDirectory: Path.Combine(storageDirectory, "stored"));

        try
        {
            var result = await service.SaveSaleAsync(
                NewSaleInput(drug, batch) with
                {
                    PatientAddress = "1 Pharmacy Road",
                    PrescriberName = "Dr Test",
                    PrescriberRegistrationNumber = "REG-TEST",
                    PrescriptionDocumentPath = sourcePath
                },
                Guid.NewGuid(),
                UserRole.Pharmacist);

            var prescription = await database.Context.Prescriptions.SingleAsync();
            Assert.Contains("DocumentPath=", prescription.Notes);
            var storedPath = prescription.Notes!["DocumentPath=".Length..];
            Assert.True(File.Exists(storedPath));
            Assert.Equal(result.Sale.Id, (await database.Context.ScheduleRegisterEntries.SingleAsync()).SaleId);
        }
        finally
        {
            Directory.Delete(storageDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task SaveSale_BlocksExpiredBatch()
    {
        await using var database = await DatabaseTestContext.CreateAsync();
        var (service, drug, batch) = await CreateServiceAsync(
            database,
            expiry: DateOnly.FromDateTime(DateTime.Today.AddDays(-1)));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SaveSaleAsync(NewSaleInput(drug, batch), Guid.NewGuid(), UserRole.Owner));

        Assert.Contains("expired", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await database.Context.Sales.ToListAsync());
    }

    [Fact]
    public async Task SaveSale_BlocksQuantityAboveMovementBasedStock()
    {
        await using var database = await DatabaseTestContext.CreateAsync();
        var (service, drug, batch) = await CreateServiceAsync(database, stock: 2m);
        var input = NewSaleInput(drug, batch) with
        {
            Items = [new RetailSaleLineInput(drug.Id, batch.Id, 3m, 0m, 100m)]
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SaveSaleAsync(input, Guid.NewGuid(), UserRole.Owner));

        Assert.Contains("exceeds available stock", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SaveSale_BlocksPriceAboveMrp()
    {
        await using var database = await DatabaseTestContext.CreateAsync();
        var (service, drug, batch) = await CreateServiceAsync(database);
        var input = NewSaleInput(drug, batch) with
        {
            Items = [new RetailSaleLineInput(drug.Id, batch.Id, 1m, 0m, 100.01m)]
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SaveSaleAsync(input, Guid.NewGuid(), UserRole.Owner));

        Assert.Contains("MRP", exception.Message);
    }

    [Fact]
    public async Task SaveSale_RequiresConfirmationForUpiPayment()
    {
        await using var database = await DatabaseTestContext.CreateAsync();
        var (service, drug, batch) = await CreateServiceAsync(database);
        var input = NewSaleInput(drug, batch) with
        {
            Payments = [new RetailPaymentInput("UPI", 50m)],
            UpiPaymentReceived = false
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SaveSaleAsync(input, Guid.NewGuid(), UserRole.Owner));

        Assert.Contains("Confirm that the UPI payment", exception.Message);
    }

    [Fact]
    public async Task SaveSale_StoresInclusiveGstSplitAndConfirmedUpiReceipt()
    {
        await using var database = await DatabaseTestContext.CreateAsync();
        var (service, drug, batch) = await CreateServiceAsync(database);

        var result = await service.SaveSaleAsync(
            NewSaleInput(drug, batch) with
            {
                Payments = [new RetailPaymentInput("UPI", 50m)],
                UpiPaymentReceived = true
            },
            Guid.NewGuid(),
            UserRole.Owner);
        var item = await database.Context.SaleItems.SingleAsync();

        Assert.Equal(10.71m, result.TaxAmount);
        Assert.Equal(89.29m, result.Subtotal);
        Assert.Equal(100m, result.TotalAmount);
        Assert.Equal(50m, result.PaidAmount);
        Assert.Equal("Partial", result.Sale.PaymentStatus);
        Assert.Equal(100m, item.LineTotal);
        Assert.Equal("UPI", (await database.Context.Receipts.SingleAsync()).PaymentMethod);
    }

    [Fact]
    public async Task SaveSale_ReusesPatientAndCreatesHabitRegisterAndMovement()
    {
        await using var database = await DatabaseTestContext.CreateAsync();
        var (service, drug, batch) = await CreateServiceAsync(database, habitForming: true);
        var patient = new Patient
        {
            Name = "Existing Patient",
            Phone = "9876543210",
            Address = "Old address"
        };
        database.Context.Patients.Add(patient);
        await database.Context.SaveChangesAsync();

        var result = await service.SaveSaleAsync(
            NewSaleInput(drug, batch) with
            {
                PatientName = "Updated Patient",
                PatientPhone = patient.Phone!,
                Payments = [new RetailPaymentInput("Cash", 40m, 50m)]
            },
            Guid.NewGuid(),
            UserRole.BillingClerk);

        Assert.Equal("Partial", result.Sale.PaymentStatus);
        Assert.Equal(10m, result.ChangeDue);
        Assert.Equal(1, await database.Context.Patients.CountAsync());
        Assert.Equal("Updated Patient", (await database.Context.Patients.SingleAsync()).Name);
        Assert.Single(await database.Context.Sales.ToListAsync());
        Assert.Equal(4m, await database.Context.StockMovements
            .Where(movement => movement.BatchId == batch.Id)
            .SumAsync(movement => movement.QuantityChange));
        Assert.Contains(await database.Context.ScheduleRegisterEntries.ToListAsync(),
            entry => entry.RegisterType == "HABIT" && entry.SaleId == result.Sale.Id);
    }

    [Fact]
    public async Task SaveSale_RejectsReadOnlyEntitlement()
    {
        await using var database = await DatabaseTestContext.CreateAsync();
        var (service, drug, batch) = await CreateServiceAsync(database, canCreateBills: false);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SaveSaleAsync(NewSaleInput(drug, batch), Guid.NewGuid(), UserRole.Owner));

        Assert.Contains("read-only", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SearchStock_ReturnsEarliestUnexpiredBatchFirst()
    {
        await using var database = await DatabaseTestContext.CreateAsync();
        var (service, drug, firstBatch) = await CreateServiceAsync(database);
        var secondBatch = new Batch
        {
            DrugId = drug.Id,
            BatchNo = "LATER",
            ExpiryDate = DateOnly.FromDateTime(DateTime.Today.AddMonths(8)),
            Mrp = 100m,
            Quantity = 3m
        };
        database.Context.Batches.Add(secondBatch);
        database.Context.StockMovements.Add(new StockMovement
        {
            BatchId = secondBatch.Id,
            DrugId = drug.Id,
            QuantityChange = 3m,
            MovementType = "PurchaseReceipt"
        });
        await database.Context.SaveChangesAsync();

        var matches = await service.SearchStockAsync("Test medicine");

        Assert.Equal(firstBatch.Id, matches[0].BatchId);
        Assert.Equal(secondBatch.Id, matches[1].BatchId);
    }

    [Fact]
    public async Task IssueSalesReturn_PartialReturnsTrackQuantityAndRequireRestockConfirmation()
    {
        await using var database = await DatabaseTestContext.CreateAsync();
        var (service, drug, batch) = await CreateServiceAsync(database);
        var sale = await service.SaveSaleAsync(
            NewSaleInput(drug, batch),
            Guid.NewGuid(),
            UserRole.Owner);
        var returnLine = Assert.Single(await service.GetSaleReturnLinesAsync(sale.Sale.Id));
        var creditOnly = await service.IssueSalesReturnAsync(
            sale.Sale.Id,
            [new RetailSaleReturnLineInput(returnLine.SaleItemId, 0.5m)],
            "Customer returned sealed goods",
            false,
            Guid.NewGuid(),
            UserRole.Owner);

        Assert.Equal(50m, creditOnly.CreditAmount);
        Assert.Equal(0m, creditOnly.RestockedQuantity);
        Assert.Equal(4m, await database.Context.StockMovements
            .Where(movement => movement.BatchId == batch.Id)
            .SumAsync(movement => movement.QuantityChange));
        var recordedReturn = await database.Context.SaleReturnItems.SingleAsync();
        Assert.False(recordedReturn.Restocked);
        Assert.Equal(0.5m, recordedReturn.Quantity);

        var restocked = await service.IssueSalesReturnAsync(
            sale.Sale.Id,
            [new RetailSaleReturnLineInput(returnLine.SaleItemId, 0.5m)],
            "Remaining sealed unit returned after inspection",
            true,
            Guid.NewGuid(),
            UserRole.Owner);

        Assert.Equal(50m, restocked.CreditAmount);
        Assert.Equal(0.5m, restocked.RestockedQuantity);
        Assert.StartsWith("WIN1-CN/", creditOnly.ReturnNo);
        Assert.StartsWith("WIN1-CN/", restocked.ReturnNo);
        Assert.Equal(4.5m, await database.Context.StockMovements
            .Where(movement => movement.BatchId == batch.Id)
            .SumAsync(movement => movement.QuantityChange));
        Assert.Equal(0m, (await service.GetSaleReturnLinesAsync(sale.Sale.Id)).Single().RemainingQuantity);
        Assert.Equal(0.5m, await database.Context.StockMovements
            .Where(movement => movement.BatchId == batch.Id && movement.MovementType == "RetailSaleReturn")
            .SumAsync(movement => movement.QuantityChange));

        var duplicate = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.IssueSalesReturnAsync(
                sale.Sale.Id,
                [new RetailSaleReturnLineInput(returnLine.SaleItemId, 0.1m)],
                "Duplicate request",
                false,
                Guid.NewGuid(),
                UserRole.Owner));

        Assert.Contains("exceeds the remaining", duplicate.Message);
        Assert.Equal(2, await database.Context.ReturnNotes.CountAsync());
        Assert.Equal(100m, await database.Context.ReturnNotes.SumAsync(note => note.TotalAmount));
    }

    [Fact]
    public async Task IssueSalesReturn_ExpiredBatchCannotBeRestocked()
    {
        await using var database = await DatabaseTestContext.CreateAsync();
        var (service, drug, batch) = await CreateServiceAsync(database);
        var sale = await service.SaveSaleAsync(
            NewSaleInput(drug, batch),
            Guid.NewGuid(),
            UserRole.Owner);
        var returnLine = Assert.Single(await service.GetSaleReturnLinesAsync(sale.Sale.Id));
        batch.ExpiryDate = DateOnly.FromDateTime(DateTime.Today.AddDays(-1));
        await database.Context.SaveChangesAsync();

        var expiredRestock = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.IssueSalesReturnAsync(
                sale.Sale.Id,
                [new RetailSaleReturnLineInput(returnLine.SaleItemId, 1m)],
                "Expired item returned",
                true,
                Guid.NewGuid(),
                UserRole.Owner));

        Assert.Contains("Expired batch", expiredRestock.Message);
        var creditOnly = await service.IssueSalesReturnAsync(
            sale.Sale.Id,
            [new RetailSaleReturnLineInput(returnLine.SaleItemId, 1m)],
            "Expired item returned; not restocked",
            false,
            Guid.NewGuid(),
            UserRole.Owner);
        Assert.Equal(100m, creditOnly.CreditAmount);
        Assert.Equal(0m, creditOnly.RestockedQuantity);
        Assert.Equal(4m, await database.Context.StockMovements
            .Where(movement => movement.BatchId == batch.Id)
            .SumAsync(movement => movement.QuantityChange));
    }

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

    private static async Task<(RetailBillingService Service, Drug Drug, Batch Batch)> CreateServiceAsync(
        DatabaseTestContext database,
        string? schedule = null,
        bool habitForming = false,
        DateOnly? expiry = null,
        decimal stock = 5m,
        bool canCreateBills = true,
        string? prescriptionStorageDirectory = null)
    {
        var profile = new PharmacyProfile
        {
            Name = "Test Pharmacy",
            BusinessMode = BusinessMode.Retail,
            InvoicePrefix = "WIN1"
        };
        var drug = new Drug { Name = "Test medicine", Schedule = schedule, GstRate = 12m };
        var batch = new Batch
        {
            DrugId = drug.Id,
            BatchNo = "TEST-1",
            ExpiryDate = expiry ?? DateOnly.FromDateTime(DateTime.Today.AddMonths(6)),
            Quantity = stock,
            Mrp = 100m,
            SalePrice = 95m
        };
        database.Context.AddRange(profile, drug, batch);
        if (habitForming)
        {
            database.Context.CatalogInfos.Add(new CatalogInfo
            {
                NameKey = drug.Name.ToLowerInvariant(),
                IsHabitForming = true,
                RegisterType = "HABIT"
            });
        }

        database.Context.StockMovements.Add(new StockMovement
        {
            BatchId = batch.Id,
            DrugId = drug.Id,
            QuantityChange = stock,
            MovementType = "PurchaseReceipt"
        });
        await database.Context.SaveChangesAsync();
        var unitOfWork = new UnitOfWork(database.Context);
        var service = new RetailBillingService(
            unitOfWork,
            new NumberSeriesService(unitOfWork),
            new TestEntitlements(canCreateBills),
            new CatalogSearchService(database.Context),
            prescriptionStorageDirectory);
        return (service, drug, batch);
    }

    private sealed class TestEntitlements(bool canCreateBills) : IEntitlementService
    {
        public Task<EntitlementStatus> GetStatusAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(EntitlementStatus.TRIAL);

        public Task<bool> IsReadOnlyAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(!canCreateBills);

        public Task<bool> CanPerformAsync(
            ProtectedOperation operation,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(canCreateBills || operation is ProtectedOperation.View or ProtectedOperation.Search);
    }
}
