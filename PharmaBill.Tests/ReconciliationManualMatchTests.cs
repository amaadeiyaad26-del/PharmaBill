using PharmaBill.Core;
using PharmaBill.Core.Entities;
using PharmaBill.Data.Persistence;
using PharmaBill.Data.Services.Reconciliation;
using Xunit;

namespace PharmaBill.Tests;

public sealed class ReconciliationManualMatchTests
{
    [Fact]
    public async Task MatchToCustomer_AllocatesOldestOpenInvoicesUpToTheDeposit()
    {
        await using var database = await DatabaseTestContext.CreateAsync();
        var chemist = new Customer { Name = "Chemist A", BuyerType = "Retailer", IsActive = true };
        var older = new WholesaleInvoice
        {
            CustomerId = chemist.Id,
            InvoiceNo = "WS-1",
            InvoiceAtUtc = BusinessClock.UtcNow.AddDays(-20),
            TotalAmount = 300m,
            PaidAmount = 0m,
            Status = "Posted"
        };
        var newer = new WholesaleInvoice
        {
            CustomerId = chemist.Id,
            InvoiceNo = "WS-2",
            InvoiceAtUtc = BusinessClock.UtcNow.AddDays(-5),
            TotalAmount = 500m,
            PaidAmount = 0m,
            Status = "Posted"
        };
        database.Context.AddRange(chemist, older, newer);
        await database.Context.SaveChangesAsync();

        var service = new ReconciliationService(new UnitOfWork(database.Context));
        var line = new BankStatementEntry(BusinessClock.Today, "UTR-1", "NEFT chemist A", 400m);

        var match = await service.MatchToCustomerAsync(line, chemist.Id);

        Assert.Equal(chemist.Id, match.CustomerId);
        Assert.Equal(new[] { "WS-1", "WS-2" }, match.MatchedDocuments.Select(doc => doc.DocumentNo));
        Assert.Equal(ReconciliationConfidence.High, match.Confidence);
        Assert.False(match.IsConfirmed);
    }

    [Fact]
    public async Task MatchToCustomer_WithNoOpenInvoices_LeavesDocumentsEmpty()
    {
        await using var database = await DatabaseTestContext.CreateAsync();
        var chemist = new Customer { Name = "Chemist B", BuyerType = "Retailer", IsActive = true };
        database.Context.Add(chemist);
        await database.Context.SaveChangesAsync();

        var service = new ReconciliationService(new UnitOfWork(database.Context));
        var line = new BankStatementEntry(BusinessClock.Today, null, "Cash deposit", 250m);

        var match = await service.MatchToCustomerAsync(line, chemist.Id);

        Assert.Empty(match.MatchedDocuments);
        Assert.Equal("No open invoices for this chemist.", match.MatchReason);
    }
}
