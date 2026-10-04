using Microsoft.Extensions.DependencyInjection;
using PharmaBill.App.ViewModels;
using PharmaBill.Core.Entities;
using PharmaBill.Data.Persistence;
using PharmaBill.Data.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace PharmaBill.Tests;

public sealed class DrugRecordsTests
{
    [Fact]
    public void Calculate_MergesEventsChronologicallyAndIncludesOpeningBalance()
    {
        var drugId = Guid.NewGuid();
        var batchId = Guid.NewGuid();
        var day = new DateTime(2026, 10, 1, 10, 0, 0, DateTimeKind.Utc);
        var events = new[]
        {
            Movement(drugId, batchId, day.AddHours(2), "Sale", "S-2", -3m, "Patient 1", "P1"),
            Movement(drugId, batchId, day.AddHours(1), "Purchase", "P-1", 5m),
            Movement(drugId, batchId, day.AddHours(3), "Sale return", "R-1", 1m, "Patient 1", "P1", 0m, 1m),
            Movement(drugId, batchId, day.AddHours(4), "Sale return", "R-2", 1m, "Patient 2", "P2", 0m, 1m)
        };

        var (rows, summary) = DrugRecordsCalculator.Calculate(events, 10m, 12m, "Both");

        Assert.Equal(["P-1", "S-2", "R-1", "R-2"], rows.Select(item => item.DocumentNo));
        Assert.Equal([15m, 12m, 12m, 12m], rows.Select(item => item.RunningBalance));
        Assert.Equal(10m, summary.OpeningBalance);
        Assert.Equal(5m, summary.Purchased);
        Assert.Equal(1m, summary.Sold);
        Assert.Equal(12m, summary.ClosingBalance);
        Assert.Equal(2, summary.UniquePatients);
        Assert.True(summary.ClosingMatchesCurrentStock);
    }

    [Fact]
    public void Calculate_FiltersDisplayedRecordsButKeepsFullRunningBalanceAndNetReturns()
    {
        var drugId = Guid.NewGuid();
        var batchId = Guid.NewGuid();
        var timestamp = DateTime.UtcNow;
        var events = new[]
        {
            Movement(drugId, batchId, timestamp, "Purchase", "P-1", 10m),
            Movement(drugId, batchId, timestamp.AddMinutes(1), "Sale", "S-1", -4m, "Patient", "P1"),
            Movement(drugId, batchId, timestamp.AddMinutes(2), "Sale return", "R-1", 1m, "Patient", "P1", 0m, 1m)
        };

        var (rows, summary) = DrugRecordsCalculator.Calculate(events, 0m, 6m, "Sale");

        Assert.Equal(["S-1", "R-1"], rows.Select(item => item.DocumentNo));
        Assert.Equal([6m, 6m], rows.Select(item => item.RunningBalance));
        Assert.Equal(3m, summary.Sold);
        Assert.Equal(10m, summary.Purchased);
        Assert.Equal(6m, summary.ClosingBalance);
    }

    [Fact]
    public void PeriodRange_UsesInclusiveLocalCalendarDatesAndExclusiveNextBoundary()
    {
        var now = new DateTime(2026, 1, 15, 18, 45, 0);

        var today = DrugRecordsPeriodRange.GetRange(DrugRecordsPeriod.Today, now, 1, 2026);
        Assert.Equal(new DateOnly(2026, 1, 15), DateOnly.FromDateTime(today.StartUtc.ToLocalTime()));
        Assert.Equal(new DateOnly(2026, 1, 16), DateOnly.FromDateTime(today.EndUtc.ToLocalTime()));

        var selectedMonth = DrugRecordsPeriodRange.GetRange(DrugRecordsPeriod.ThisMonth, now, 3, 2025);
        Assert.Equal(new DateOnly(2025, 3, 1), DateOnly.FromDateTime(selectedMonth.StartUtc.ToLocalTime()));
        Assert.Equal(new DateOnly(2025, 4, 1), DateOnly.FromDateTime(selectedMonth.EndUtc.ToLocalTime()));

        var lastMonth = DrugRecordsPeriodRange.GetRange(DrugRecordsPeriod.LastMonth, now, 1, 2026);
        Assert.Equal(new DateOnly(2025, 12, 1), DateOnly.FromDateTime(lastMonth.StartUtc.ToLocalTime()));
        Assert.Equal(new DateOnly(2026, 1, 1), DateOnly.FromDateTime(lastMonth.EndUtc.ToLocalTime()));
    }

    [Fact]
    public async Task DrugRecordsService_SearchesSaltAndCreditsReturnsWithoutRestocking()
    {
        await using var database = await DatabaseTestContext.CreateAsync();
        var context = database.Context;
        var user = new AppUser { UserName = "records-owner", DisplayName = "Owner", PasswordHash = "hash" };
        var medicine = new CatalogMedicine
        {
            Name = "Brand Alpha",
            BrandName = "Brand Alpha",
            ShortComposition1 = "Alprazolam 0.5 mg",
            CompositionKey = "alprazolam:0.5"
        };
        var drug = new Drug
        {
            Name = "Brand Alpha 0.5",
            BrandName = "Brand Alpha",
            GenericName = "Alprazolam",
            CatalogMedicineId = medicine.Id
        };
        var batch = new Batch { DrugId = drug.Id, BatchNo = "ALP-1", Quantity = 6m };
        var patient = new Patient { Name = "Test Patient", Phone = "9876500000", Address = "Test Road" };
        var saleAt = new DateTime(2026, 9, 12, 10, 0, 0, DateTimeKind.Utc);
        var sale = new Sale
        {
            PatientId = patient.Id,
            InvoiceNo = "WIN1/2026-27/1",
            SaleAtUtc = saleAt
        };
        var saleItem = new SaleItem
        {
            SaleId = sale.Id,
            DrugId = drug.Id,
            BatchId = batch.Id,
            Quantity = 4m,
            UnitPrice = 100m,
            LineTotal = 400m
        };
        var returnNote = new ReturnNote
        {
            SourceType = nameof(Sale),
            SourceId = sale.Id,
            ReturnNo = "WIN1-CN/2026-27/1",
            ReturnAtUtc = saleAt.AddDays(1),
            TotalAmount = 100m,
            Reason = "Credit-only return"
        };
        context.AddRange(user, medicine, drug, batch, patient, sale, saleItem, returnNote,
            new SaleReturnItem
            {
                ReturnNoteId = returnNote.Id,
                SaleItemId = saleItem.Id,
                BatchId = batch.Id,
                DrugId = drug.Id,
                Quantity = 1m,
                CreditAmount = 100m,
                Restocked = false
            },
            new StockMovement
            {
                BatchId = batch.Id,
                DrugId = drug.Id,
                QuantityChange = 10m,
                MovementType = "PurchaseReceipt",
                MovementAtUtc = saleAt.AddDays(-2)
            },
            new StockMovement
            {
                BatchId = batch.Id,
                DrugId = drug.Id,
                QuantityChange = -4m,
                MovementType = "RetailSale",
                ReferenceType = nameof(Sale),
                ReferenceId = sale.Id,
                MovementAtUtc = saleAt
            });
        await context.SaveChangesAsync();

        using var provider = new ServiceCollection()
            .AddSingleton<IUnitOfWork>(new UnitOfWork(context))
            .BuildServiceProvider();
        var service = new DrugRecordsService(provider.GetRequiredService<IServiceScopeFactory>());
        var selectedDrugs = await service.SearchDrugsAsync("alprazolam", user.Id);
        var report = await service.SearchAsync(
            new DrugRecordsQuery(
                [drug.Id],
                saleAt.AddDays(-1),
                saleAt.AddDays(2),
                "Both"),
            user.Id);

        Assert.Contains(selectedDrugs, item => item.DrugId == drug.Id);
        Assert.Equal(10m, report.Summary.OpeningBalance);
        Assert.Equal(3m, report.Summary.Sold);
        Assert.Equal(6m, report.Summary.ClosingBalance);
        Assert.True(report.Summary.ClosingMatchesCurrentStock);
        Assert.Contains(report.Rows, row => row.RecordType == "Sale return" && row.Quantity == 1m);
        Assert.Equal(2, await context.AuditLogs.CountAsync(item =>
            new[] { "DrugRecordsDrugSearch", "DrugRecordsSearched" }.Contains(item.Action)));
    }

    [Fact]
    public async Task StatutoryRegisterService_CorrectionAppendsInsteadOfChangingOriginal()
    {
        await using var database = await DatabaseTestContext.CreateAsync();
        var context = database.Context;
        var user = new AppUser { UserName = "register-owner", DisplayName = "Owner", PasswordHash = "hash" };
        var drug = new Drug { Name = "Reference medicine" };
        var batch = new Batch { DrugId = drug.Id, BatchNo = "REG-1", Quantity = 3m };
        var entry = new ScheduleRegisterEntry
        {
            RegisterType = "HABIT",
            DrugId = drug.Id,
            BatchId = batch.Id,
            BatchNo = batch.BatchNo,
            Quantity = 1m,
            PatientName = "Test Patient",
            Notes = "Original statutory entry"
        };
        context.AddRange(user, drug, batch, entry, new StockMovement
        {
            BatchId = batch.Id,
            DrugId = drug.Id,
            QuantityChange = 3m,
            MovementType = "PurchaseReceipt",
            MovementAtUtc = entry.EntryAtUtc.AddMinutes(-1)
        });
        await context.SaveChangesAsync();

        using var provider = new ServiceCollection()
            .AddSingleton<IUnitOfWork>(new UnitOfWork(context))
            .BuildServiceProvider();
        var service = new StatutoryRegisterService(provider.GetRequiredService<IServiceScopeFactory>());
        var habitRows = await service.SearchAsync("Habit-forming", user.Id);
        await service.AppendCorrectionAsync(entry.Id, "Correct patient address", user.Id);

        Assert.Single(habitRows);
        Assert.Equal(3m, habitRows[0].Balance);
        Assert.Equal("REG-1", habitRows[0].BatchNo);
        Assert.Equal(2, await context.ScheduleRegisterEntries.CountAsync());
        Assert.Equal("Original statutory entry",
            (await context.ScheduleRegisterEntries.SingleAsync(item => item.Id == entry.Id)).Notes);
        var correction = await context.ScheduleRegisterEntries.SingleAsync(item => item.Id != entry.Id);
        Assert.Contains(entry.Id.ToString("D"), correction.Notes);
        Assert.Contains("Correct patient address", correction.Notes);
        Assert.Equal(2, await context.AuditLogs.CountAsync());
    }

    private static DrugRecordsMovement Movement(
        Guid drugId,
        Guid batchId,
        DateTime at,
        string recordType,
        string documentNo,
        decimal quantity,
        string patient = "",
        string phone = "",
        decimal? balanceChange = null,
        decimal? salesQuantityChange = null) =>
        new(
            drugId,
            batchId,
            at,
            recordType,
            documentNo,
            patient,
            string.Empty,
            phone,
            string.Empty,
            string.Empty,
            "BATCH",
            quantity,
            balanceChange,
            salesQuantityChange,
            null);
}
