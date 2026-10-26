using System.Text;
using PharmaBill.App.Services;
using PharmaBill.Core.Labels;
using QuestPDF.Infrastructure;
using Xunit;

namespace PharmaBill.Tests;

public sealed class BarcodeLabelPdfServiceTests
{
    [Fact]
    public void Build_ProducesPdfForManyLabelsAcrossPages()
    {
        QuestPDF.Settings.License = LicenseType.Community;
        PdfFonts.Register();
        var items = Enumerable.Range(0, 40)
            .Select(i => new BarcodeLabelItem("Medicine " + i, "500mg", "CODE-" + i, 25.5m, "B" + i, new DateOnly(2027, 1, 1), 1))
            .ToList();
        var plan = BarcodeLabelPlanner.Plan(items);

        byte[] pdf = new BarcodeLabelPdfService().Build(plan, "Shelf labels", columns: 3);

        Assert.True(pdf.Length > 1000);
        Assert.Equal("%PDF", Encoding.ASCII.GetString(pdf, 0, 4));
    }

    [Fact]
    public void Build_HandlesEmptyPlan()
    {
        QuestPDF.Settings.License = LicenseType.Community;
        PdfFonts.Register();

        byte[] pdf = new BarcodeLabelPdfService().Build(BarcodeLabelPlanner.Plan(Array.Empty<BarcodeLabelItem>()), "Empty", columns: 2);

        Assert.Equal("%PDF", Encoding.ASCII.GetString(pdf, 0, 4));
    }
}
