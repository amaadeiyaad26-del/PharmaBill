using PharmaBill.Core;
using PharmaBill.Data.Services;
using Xunit;

namespace PharmaBill.Tests;

public sealed class BranchCacheTests
{
    [Fact]
    public async Task PeerStockHint_ShownWithItsTime_OnlyWhileFresh()
    {
        await using var database = await DatabaseTestContext.CreateAsync();
        BranchService branches = database.CreateBranchService();
        DateTime now = BusinessClock.UtcNow;
        branches.SavePeerStockIndex(Index("BR02", "Lal Chowk branch", now.AddHours(-2), "Paracetamol 650", 40m));
        branches.SavePeerStockIndex(Index("BR03", "Rajbagh branch", now.AddHours(-25), "Paracetamol 650", 90m));

        IReadOnlyList<PeerBranchStockHint> hints = branches.FindPeerAvailability("Paracetamol 650", null);

        PeerBranchStockHint hint = Assert.Single(hints);
        Assert.Equal("BR02", hint.BranchCode);
        string text = BranchService.FormatPeerAvailabilityHint(hints);
        Assert.Equal($"Lal Chowk branch had 40 in stock at {now.AddHours(-2).ToBusinessLocal():h:mm tt, d MMM}", text);
    }

    [Fact]
    public async Task PeerStockHint_Hidden_WhenTheCacheIsOld()
    {
        await using var database = await DatabaseTestContext.CreateAsync();
        BranchService branches = database.CreateBranchService();
        branches.SavePeerStockIndex(Index("BR02", "Lal Chowk branch", BusinessClock.UtcNow.AddDays(-12), "Paracetamol 650", 40m));

        Assert.Empty(branches.FindPeerAvailability("Paracetamol 650", null));
        Assert.Equal(string.Empty, BranchService.FormatPeerAvailabilityHint(branches.FindPeerAvailability("Paracetamol 650", null)));
    }

    [Fact]
    public void Freshness_RejectsOldAndFutureDatedFigures()
    {
        DateTime now = new(2026, 10, 21, 6, 0, 0, DateTimeKind.Utc);
        Assert.True(BranchService.IsFresh(now.AddHours(-23), now));
        Assert.False(BranchService.IsFresh(now.AddHours(-25), now));
        Assert.False(BranchService.IsFresh(now.AddHours(3), now));
    }

    [Fact]
    public async Task PurchaseReport_NetworkScope_HasNoBranchSalesRows()
    {
        await using var database = await DatabaseTestContext.CreateAsync();
        BranchService branches = database.CreateBranchService();
        branches.SavePeerSalesSummary(new BranchSalesSummaryDocument
        {
            BranchCode = "BR02",
            BranchName = "Lal Chowk branch",
            GeneratedAtUtc = BusinessClock.UtcNow,
            SalesTotal = 125000m,
            InvoiceCount = 300
        });
        var reports = new ReportsDashboardService(database.Context, branches, new SalesAnalysisService(database.Context));
        DateOnly today = BusinessClock.Today;

        ReportTable purchases = await reports.GetReportAsync(ReportKind.Purchases, today.AddMonths(-1), today, ReportBranchScope.ConsolidatedNetwork);

        Assert.Empty(purchases.Rows);
    }

    private static BranchStockIndexDocument Index(string code, string name, DateTime generatedAtUtc, string medicine, decimal quantity) => new()
    {
        BranchId = Guid.NewGuid(),
        BranchCode = code,
        BranchName = name,
        GeneratedAtUtc = generatedAtUtc,
        Items = [new BranchStockIndexItem { MedicineName = medicine, Quantity = quantity }]
    };
}
