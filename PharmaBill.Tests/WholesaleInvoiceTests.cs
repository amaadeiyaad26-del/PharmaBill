using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PharmaBill.Core.Entities;
using PharmaBill.Core.Security;
using PharmaBill.Data.Persistence;
using PharmaBill.Data.Services;
using Xunit;

namespace PharmaBill.Tests;

public sealed class WholesaleInvoiceTests
{
    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task WholesaleInvoice_BlocksBlockedAndUnlicensedBuyers(
        bool blocked,
        bool hasLicence)
    {
        var test = await CreateScenarioAsync(blocked: blocked, validLicence: hasLicence);
        await using var database = test.Database;
        var service = CreateService(database.Context);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SaveAsync(test.Input(), test.Owner.Id, UserRole.Owner));
    }

    [Fact]
    public async Task WholesaleInvoice_BlocksExpiredBuyerLicence()
    {
        var test = await CreateScenarioAsync(validLicence: true, licenceExpiry: DateOnly.FromDateTime(DateTime.Today.AddDays(-1)));
        await using var database = test.Database;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateService(database.Context).SaveAsync(test.Input(), test.Owner.Id, UserRole.Owner));
    }

    [Fact]
    public async Task WholesaleInvoice_ScheduleXRequiresBuyerAuthorisation()
    {
        var test = await CreateScenarioAsync(
            validLicence: true,
            schedule: "X",
            authorisation: "General wholesale authority");
        await using var database = test.Database;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateService(database.Context).SaveAsync(test.Input(), test.Owner.Id, UserRole.Owner));
    }

    [Fact]
    public async Task WholesaleInvoice_SplitsTaxByStateAndRecordsFreeQuantityMovementAndNumber()
    {
        var test = await CreateScenarioAsync(validLicence: true, sameState: true);
        await using var database = test.Database;

        var result = await CreateService(database.Context).SaveAsync(test.Input(), test.Owner.Id, UserRole.Owner);

        Assert.Equal("WHO/2026-27/000001", result.Invoice.InvoiceNo);
        Assert.Equal(120m, result.CgstAmount + result.SgstAmount);
        Assert.Equal(0m, result.IgstAmount);
        Assert.Equal(-11m, await database.Context.StockMovements
            .Where(item => item.MovementType == "WholesaleSale")
            .SumAsync(item => item.QuantityChange));
        var invoiceItem = await database.Context.WholesaleInvoiceItems.SingleAsync();
        Assert.Equal(1m, invoiceItem.FreeQuantity);
        Assert.Equal(11m, invoiceItem.Quantity + invoiceItem.FreeQuantity);
        Assert.Equal(1120m, result.Invoice.TotalAmount);
        Assert.Single(await database.Context.CustomerLedgerEntries.ToListAsync());
        Assert.Contains(
            await database.Context.AuditLogs.ToListAsync(),
            item => item.Action == "WholesaleInvoicePosted");
    }

    [Fact]
    public async Task WholesaleInvoice_InterStateBuyerUsesIgstOnly()
    {
        var test = await CreateScenarioAsync(validLicence: true, sameState: false);
        await using var database = test.Database;

        var result = await CreateService(database.Context).SaveAsync(test.Input(), test.Owner.Id, UserRole.Owner);

        Assert.Equal(0m, result.CgstAmount);
        Assert.Equal(0m, result.SgstAmount);
        Assert.Equal(120m, result.IgstAmount);
    }

    [Fact]
    public async Task WholesaleInvoice_RequiresOwnerReasonToOverrideCreditLimit()
    {
        var test = await CreateScenarioAsync(validLicence: true, creditLimit: 500m);
        await using var database = test.Database;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateService(database.Context).SaveAsync(test.Input(), test.Owner.Id, UserRole.Owner));
        var input = test.Input() with
        {
            OverrideCreditOrOverdue = true,
            OverrideReason = "Approved by owner"
        };
        var result = await CreateService(database.Context).SaveAsync(input, test.Owner.Id, UserRole.Owner);

        Assert.Equal(1120m, result.OutstandingAfterPosting);
        Assert.Contains(
            await database.Context.CustomerLedgerEntries.ToListAsync(),
            entry => entry.Notes!.Contains("Approved by owner"));
    }

    [Fact]
    public async Task WholesaleInvoice_NearExpiryNeedsConfirmationAndExpiredBatchesAreBlocked()
    {
        var nearExpiry = await CreateScenarioAsync(
            validLicence: true,
            batchExpiry: DateOnly.FromDateTime(DateTime.Today.AddDays(30)));
        await using (nearExpiry.Database)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                CreateService(nearExpiry.Database.Context)
                    .SaveAsync(nearExpiry.Input(), nearExpiry.Owner.Id, UserRole.Owner));
        }

        var expired = await CreateScenarioAsync(
            validLicence: true,
            batchExpiry: DateOnly.FromDateTime(DateTime.Today.AddDays(-1)));
        await using (expired.Database)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                CreateService(expired.Database.Context)
                    .SaveAsync(expired.Input() with { ConfirmNearExpiry = true }, expired.Owner.Id, UserRole.Owner));
        }
    }

    [Fact]
    public async Task WholesaleInvoice_CancellationRestoresBatchAndRetainsInvoiceNumber()
    {
        var test = await CreateScenarioAsync(validLicence: true);
        await using var database = test.Database;
        var service = CreateService(database.Context);
        var result = await service.SaveAsync(test.Input(), test.Owner.Id, UserRole.Owner);

        await service.CancelAsync(result.Invoice.Id, "Duplicate order", test.Owner.Id, UserRole.Owner);

        Assert.Equal("WHO/2026-27/000001", (await database.Context.WholesaleInvoices.SingleAsync()).InvoiceNo);
        Assert.Equal("Cancelled", (await database.Context.WholesaleInvoices.SingleAsync()).Status);
        Assert.Equal(100m, await database.Context.StockMovements.SumAsync(item => item.QuantityChange));
        Assert.Contains(
            await database.Context.ReturnNotes.ToListAsync(),
            note => note.SourceId == result.Invoice.Id && note.Reason == "Duplicate order");
    }

    [Fact]
    public async Task WholesaleCreditReturn_UsesOriginalBatchAndQuarantinesUntilRestocked()
    {
        var test = await CreateScenarioAsync(validLicence: true);
        await using var database = test.Database;
        var invoice = await CreateService(database.Context)
            .SaveAsync(test.Input(), test.Owner.Id, UserRole.Owner);
        var invoiceLine = await database.Context.WholesaleInvoiceItems.SingleAsync();
        var entitlement = new AllowedEntitlement();
        var returns = new WholesaleReturnsService(new UnitOfWork(database.Context), entitlement);

        var note = await returns.CreateCreditNoteAsync(
            invoice.Invoice.Id,
            "CN-1",
            DateOnly.FromDateTime(DateTime.Today),
            [new WholesaleCreditLineInput(invoiceLine.Id, 2m, Restock: false)],
            "Damaged on delivery",
            test.Owner.Id,
            UserRole.Owner);

        Assert.Equal(224m, note.TotalAmount);
        var returned = await database.Context.WholesaleReturnItems.SingleAsync();
        Assert.Equal(test.Batch.Id, returned.BatchId);
        Assert.True(returned.Quarantined);
        Assert.False(returned.Restocked);
        Assert.Equal(89m, await database.Context.StockMovements.SumAsync(item => item.QuantityChange));
        await Assert.ThrowsAsync<InvalidOperationException>(() => returns.CreateCreditNoteAsync(
            invoice.Invoice.Id,
            "CN-2",
            DateOnly.FromDateTime(DateTime.Today),
            [new WholesaleCreditLineInput(invoiceLine.Id, 9m, Restock: true)],
            "Return excess",
            test.Owner.Id,
            UserRole.Owner));
    }

    [Fact]
    public async Task WholesaleAccounts_RecordsInvoiceReceiptAndAgeing()
    {
        var test = await CreateScenarioAsync(validLicence: true);
        await using var database = test.Database;
        var invoice = await CreateService(database.Context)
            .SaveAsync(test.Input(), test.Owner.Id, UserRole.Owner);
        var accounts = new WholesaleAccountsService(new UnitOfWork(database.Context));

        var receipt = await accounts.RecordReceiptAsync(
            test.Customer.Id,
            invoice.Invoice.Id,
            500m,
            "Cheque",
            "REF-1",
            "CHQ-1",
            DateOnly.FromDateTime(DateTime.Today),
            "Local bank",
            test.Owner.Id);
        var ledger = await accounts.GetCustomerLedgerAsync(test.Customer.Id);
        var ageing = await accounts.GetAgeingAsync(DateOnly.FromDateTime(DateTime.Today));

        Assert.Equal("CHQ-1", receipt.ChequeNumber);
        Assert.Equal(620m, Assert.Single(ageing).Current0To30);
        Assert.Equal(620m, ledger.Last().Balance);
        var book = await accounts.GetCashBankBookAsync(
            DateTime.UtcNow.AddDays(-1),
            DateTime.UtcNow.AddDays(1));
        Assert.Contains(book, row => row.ReceiptNo == receipt.ReceiptNo && row.Direction == "In");
    }

    [Fact]
    public async Task WholesaleAccounts_AppliesOnAccountReceiptsAndRecordsSupplierPayments()
    {
        var test = await CreateScenarioAsync(validLicence: true);
        await using var database = test.Database;
        var invoice = await CreateService(database.Context)
            .SaveAsync(test.Input(), test.Owner.Id, UserRole.Owner);
        var supplier = new Supplier { Name = "Ledger supplier" };
        database.Context.Suppliers.Add(supplier);
        var accounts = new WholesaleAccountsService(new UnitOfWork(database.Context));
        await accounts.RecordReceiptAsync(
            test.Customer.Id, null, 100m, "UPI", "ON-ACCOUNT", null, null, null, test.Owner.Id);
        var ageing = Assert.Single(await accounts.GetAgeingAsync(DateOnly.FromDateTime(DateTime.Today)));

        Assert.Equal(1020m, ageing.Current0To30);
        database.Context.SupplierLedgerEntries.Add(new SupplierLedgerEntry
        {
            SupplierId = supplier.Id,
            EntryAtUtc = DateTime.UtcNow,
            EntryType = "PurchaseInvoice",
            Credit = 500m
        });
        await database.Context.SaveChangesAsync();
        var payment = await accounts.RecordSupplierPaymentAsync(
            supplier.Id, 200m, "Cheque", "PAYREF", "CHQ-2",
            DateOnly.FromDateTime(DateTime.Today), "Test bank", test.Owner.Id);

        Assert.Equal(300m, Assert.Single(await accounts.GetSupplierPayablesAsync()).Payable);
        Assert.Contains(await accounts.GetSupplierLedgerAsync(supplier.Id), row => row.ReferenceNo == payment.ReceiptNo);
        Assert.Contains(await accounts.GetCashBankBookAsync(
            DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1)),
            row => row.ReceiptNo == payment.ReceiptNo && row.Direction == "Out");
        Assert.Equal(1120m, invoice.Invoice.TotalAmount);
    }

    [Fact]
    public async Task StockInHand_UsesMovementAsOfWindowAndSuggestsReorder()
    {
        await using var database = await DatabaseTestContext.CreateAsync();
        var drug = new Drug { Name = "As-of stock", ReorderLevel = 10m, HsnCode = "3004" };
        var batch = new Batch
        {
            DrugId = drug.Id,
            BatchNo = "ASOF",
            PurchasePrice = 4m,
            Ptr = 5m,
            Mrp = 8m,
            ExpiryDate = new DateOnly(2027, 12, 31)
        };
        database.Context.AddRange(drug, batch);
        database.Context.StockMovements.AddRange(
            new StockMovement
            {
                DrugId = drug.Id,
                BatchId = batch.Id,
                QuantityChange = 10m,
                MovementType = "PurchaseReceipt",
                MovementAtUtc = new DateTime(2026, 10, 1, 10, 0, 0, DateTimeKind.Utc)
            },
            new StockMovement
            {
                DrugId = drug.Id,
                BatchId = batch.Id,
                QuantityChange = -3m,
                MovementType = "Sale",
                MovementAtUtc = new DateTime(2026, 10, 3, 10, 0, 0, DateTimeKind.Utc)
            });
        await database.Context.SaveChangesAsync();

        var service = new StockInHandService(database.Context);
        var rows = await service.GetAsOfAsync(
            new DateTime(2026, 10, 4, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 10, 3, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 10, 4, 0, 0, 0, DateTimeKind.Utc));
        var row = Assert.Single(rows);
        Assert.Equal(10m, row.Opening);
        Assert.Equal(3m, row.OutQuantity);
        Assert.Equal(7m, row.Closing);
        Assert.Equal(28m, row.StockValue);
        Assert.Equal(3m, Assert.Single(await service.GetReorderSuggestionsAsync()).SuggestedQuantity);
    }

    [Fact]
    public async Task WholesaleGstReports_ExportReferenceGstr1AndGroupByHsnAndRate()
    {
        var test = await CreateScenarioAsync(validLicence: true, sameState: false);
        await using var database = test.Database;
        await CreateService(database.Context).SaveAsync(test.Input(), test.Owner.Id, UserRole.Owner);
        var reports = new WholesaleGstReportsService(database.Context);
        var today = DateOnly.FromDateTime(DateTime.Now);

        var hsn = await reports.GetHsnSummaryAsync(today, today);
        var csv = await reports.ExportGstr1CsvAsync(today, today);
        var json = await reports.ExportGstr1JsonAsync(today, today);
        var reference = await reports.GetGstr3bReferenceSummaryAsync(today, today);

        Assert.Equal("3004", Assert.Single(hsn).Hsn);
        Assert.Contains("WHO/2026-27/000001", csv);
        Assert.Contains("GSTR-1-style reference export", json);
        Assert.Equal("For reference only; verify with your CA.", reference.Notice);
        Assert.Equal(120m, reference.Igst);
    }

    [Fact]
    public async Task WholesaleGstReports_IncludesSupplierPurchaseRegisterRows()
    {
        var test = await CreateScenarioAsync(validLicence: true);
        await using var database = test.Database;
        var supplier = new Supplier { Name = "Purchase register supplier", Gstin = "29ABCDE1234F1Z5" };
        database.Context.Suppliers.Add(supplier);
        await database.Context.SaveChangesAsync();
        var invoice = new PurchaseInvoice
        {
            SupplierId = supplier.Id,
            InvoiceNo = "SUP-INV-1",
            InvoiceDate = DateOnly.FromDateTime(DateTime.Today),
            TotalAmount = 224m,
            Status = "Posted"
        };
        database.Context.PurchaseInvoices.Add(invoice);
        database.Context.PurchaseItems.Add(new PurchaseItem
        {
            PurchaseInvoiceId = invoice.Id,
            DrugId = test.Drug.Id,
            BatchId = test.Batch.Id,
            Quantity = 10m,
            UnitPrice = 20m,
            TaxRate = 12m,
            LineTotal = 224m
        });
        await database.Context.SaveChangesAsync();

        var row = Assert.Single(await new WholesaleGstReportsService(database.Context)
            .GetPurchaseRegisterAsync(DateOnly.FromDateTime(DateTime.Today), DateOnly.FromDateTime(DateTime.Today)));

        Assert.Equal("SUP-INV-1", row.InvoiceNo);
        Assert.Equal("3004", row.Hsn);
        Assert.Equal(200m, row.TaxableAmount);
        Assert.Equal(24m, row.TaxAmount);
    }

    private static WholesaleInvoiceService CreateService(PharmaBillDbContext context)
    {
        var unitOfWork = new UnitOfWork(context);
        return new WholesaleInvoiceService(unitOfWork, new NumberSeriesService(unitOfWork), new AllowedEntitlement());
    }

    private static async Task<Scenario> CreateScenarioAsync(
        bool blocked = false,
        bool validLicence = false,
        bool sameState = false,
        string? schedule = null,
        string? authorisation = null,
        decimal creditLimit = 0m,
        DateOnly? licenceExpiry = null,
        DateOnly? batchExpiry = null)
    {
        var database = await DatabaseTestContext.CreateAsync();
        var profile = new PharmacyProfile
        {
            Name = "Test Pharmacy",
            State = "Karnataka",
            BusinessMode = BusinessMode.Wholesaler,
            InvoicePrefix = "WHO",
            WholesaleBuyerLicenceRulesJson = JsonSerializer.Serialize(
                new Dictionary<string, string[]> { ["Distributor"] = ["W1"] })
        };
        var owner = new AppUser
        {
            DisplayName = "Owner",
            UserName = $"wholesale-owner-{Guid.NewGuid():N}",
            PasswordHash = "test",
            Role = UserRole.Owner
        };
        var customer = new Customer
        {
            Name = "Buyer",
            BuyerType = "Distributor",
            State = sameState ? "Karnataka" : "Tamil Nadu",
            IsActive = !blocked,
            CreditLimit = creditLimit,
            CreditDays = 30
        };
        var drug = new Drug { Name = "Test tablet", Schedule = schedule, GstRate = 12m, Mrp = 100m, HsnCode = "3004" };
        var batch = new Batch
        {
            DrugId = drug.Id,
            BatchNo = "B-1",
            ExpiryDate = batchExpiry ?? DateOnly.FromDateTime(DateTime.Today.AddYears(1)),
            Mrp = 100m,
            PurchasePrice = 50m
        };
        database.Context.AddRange(profile, owner, customer, drug, batch);
        database.Context.StockMovements.Add(new StockMovement
        {
            DrugId = drug.Id,
            BatchId = batch.Id,
            QuantityChange = 100m,
            MovementType = "PurchaseReceipt"
        });
        if (validLicence)
        {
            database.Context.CustomerLicences.Add(new CustomerLicence
            {
                CustomerId = customer.Id,
                LicenceType = "W1",
                LicenceNumber = "DL-W1",
                IssuedOn = DateOnly.FromDateTime(DateTime.Today.AddYears(-1)),
                ExpiresOn = licenceExpiry ?? DateOnly.FromDateTime(DateTime.Today.AddYears(1)),
                Authorisation = authorisation
            });
        }

        await database.Context.SaveChangesAsync();
        return new Scenario(database, owner, customer, drug, batch);
    }

    private sealed record Scenario(
        DatabaseTestContext Database,
        AppUser Owner,
        Customer Customer,
        Drug Drug,
        Batch Batch)
    {
        public SaveWholesaleInvoiceInput Input() => new(
            Customer.Id,
            [new WholesaleInvoiceLineInput(Drug.Id, Batch.Id, 10m, 1m, 100m)],
            0m,
            "Cash",
            null,
            false,
            90,
            false,
            null,
            "Transporter",
            "KA01AB1234",
            null,
            null,
            null,
            new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, DateTime.UtcNow.Day, 10, 0, 0, DateTimeKind.Utc));
    }

    private sealed class AllowedEntitlement : IEntitlementService
    {
        public Task<EntitlementStatus> GetStatusAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(EntitlementStatus.TRIAL);

        public Task<bool> IsReadOnlyAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<bool> CanPerformAsync(
            ProtectedOperation operation,
            CancellationToken cancellationToken = default) => Task.FromResult(true);
    }
}
