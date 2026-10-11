using System;
using System.Linq;
using PharmaBill.Core.Labels;
using Xunit;

namespace PharmaBill.Tests;

public sealed class BarcodeLabelPlannerTests
{
    [Fact]
    public void Plan_RepeatsEachItemByCopies()
    {
        BarcodeLabelItem item = new("Pantoprazole", "40mg", "8901234567890", 120m, "B1", new DateOnly(2027, 3, 1), 3);

        BarcodeLabelPlan plan = BarcodeLabelPlanner.Plan(new[] { item });

        Assert.Equal(3, plan.Cells.Count);
        Assert.All(plan.Cells, c => Assert.Equal("8901234567890", c.Code));
        Assert.Equal(0, plan.SkippedWithoutBarcode);
    }

    [Fact]
    public void Plan_SkipsItemsWithoutPrintableBarcode()
    {
        BarcodeLabelItem missing = new("Paracetamol", null, "   ", 10m, null, null, 2);
        BarcodeLabelItem nonAscii = new("Syrup", null, "कोड-1", 10m, null, null, 1);
        BarcodeLabelItem ok = new("Azithromycin", "500mg", "ABC-123", 45m, null, null, 1);

        BarcodeLabelPlan plan = BarcodeLabelPlanner.Plan(new[] { missing, nonAscii, ok });

        Assert.Single(plan.Cells);
        Assert.Equal("ABC-123", plan.Cells[0].Code);
        Assert.Equal(2, plan.SkippedWithoutBarcode);
    }

    [Fact]
    public void Plan_IgnoresZeroOrNegativeCopies()
    {
        BarcodeLabelItem zero = new("Cough syrup", null, "CODE1", 50m, null, null, 0);
        BarcodeLabelItem negative = new("Cough syrup", null, "CODE1", 50m, null, null, -2);

        BarcodeLabelPlan plan = BarcodeLabelPlanner.Plan(new[] { zero, negative });

        Assert.Empty(plan.Cells);
        Assert.Equal(0, plan.SkippedWithoutBarcode);
    }

    [Fact]
    public void Cell_FormatsPriceBatchAndExpiry()
    {
        BarcodeLabelItem item = new("Metformin", "500mg", "MET500", 35.5m, "BX9", new DateOnly(2026, 11, 15), 1);

        BarcodeLabelCell cell = BarcodeLabelPlanner.Plan(new[] { item }).Cells.Single();

        Assert.Equal("Metformin", cell.Title);
        Assert.Equal("500mg", cell.Subtitle);
        Assert.Equal("MRP ₹35.50", cell.Price);
        Assert.Equal("B: BX9", cell.Batch);
        Assert.Equal("Exp 11/2026", cell.Expiry);
    }

    [Fact]
    public void Cell_LeavesMissingOptionalFieldsEmpty()
    {
        BarcodeLabelItem item = new("Gauze", null, "G1", null, null, null, 1);

        BarcodeLabelCell cell = BarcodeLabelPlanner.Plan(new[] { item }).Cells.Single();

        Assert.Equal(string.Empty, cell.Subtitle);
        Assert.Equal(string.Empty, cell.Price);
        Assert.Equal(string.Empty, cell.Batch);
        Assert.Equal(string.Empty, cell.Expiry);
    }

    [Theory]
    [InlineData(" 12345 ", "12345")]
    [InlineData("A B", "A B")]
    [InlineData("", null)]
    [InlineData(null, null)]
    [InlineData("tab\there", null)]
    public void NormaliseCode_TrimsAndRejectsUnprintable(string? input, string? expected) =>
        Assert.Equal(expected, BarcodeLabelPlanner.NormaliseCode(input));

    [Fact]
    public void Paginate_SplitsCellsIntoPagesOfColumnsTimesRows()
    {
        BarcodeLabelCell[] cells = Enumerable.Range(0, 7)
            .Select(i => new BarcodeLabelCell("T" + i, string.Empty, string.Empty, string.Empty, string.Empty, "C" + i))
            .ToArray();

        var pages = BarcodeLabelPlanner.Paginate(cells, columns: 3, rowsPerPage: 2);

        Assert.Equal(2, pages.Count);
        Assert.Equal(6, pages[0].Count);
        Assert.Single(pages[1]);
        Assert.Equal("C6", pages[1][0].Code);
    }

    [Fact]
    public void Paginate_RejectsNonPositiveLayout() =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            BarcodeLabelPlanner.Paginate(Array.Empty<BarcodeLabelCell>(), columns: 0, rowsPerPage: 2));
}
