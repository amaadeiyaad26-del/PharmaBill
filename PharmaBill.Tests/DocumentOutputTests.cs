using PharmaBill.App.Services;
using PharmaBill.Core;
using PharmaBill.Data.Services;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using Xunit;

namespace PharmaBill.Tests;

public sealed class DocumentOutputTests
{
    [Theory]
    [InlineData(DocumentPaperSize.A4, RetailDocumentType.CashMemo)]
    [InlineData(DocumentPaperSize.A5, RetailDocumentType.CashMemo)]
    [InlineData(DocumentPaperSize.Thermal80, RetailDocumentType.CashMemo)]
    [InlineData(DocumentPaperSize.A4, RetailDocumentType.Invoice)]
    [InlineData(DocumentPaperSize.A5, RetailDocumentType.Invoice)]
    [InlineData(DocumentPaperSize.Thermal80, RetailDocumentType.Invoice)]
    public void RetailBillPdf_GeneratesEveryPaperLayout(
        DocumentPaperSize paperSize,
        RetailDocumentType documentType)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        var settings = new DocumentOutputSettings
        {
            RetailMemoPaperSize = paperSize,
            RetailInvoicePaperSize = paperSize,
            FooterText = "Thank you"
        };
        var bill = CreateBill();
        var path = Path.Combine(Path.GetTempPath(), $"PharmaBill-{Guid.NewGuid():N}.pdf");
        try
        {
            RetailBillPdfService.CreateDocument(bill, settings, documentType)
                .GeneratePdf(path);

            Assert.True(new FileInfo(path).Length > 100);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Theory]
    [InlineData(1234.56, "one thousand two hundred thirty four rupees and fifty six paise only")]
    [InlineData(100000, "one lakh rupees only")]
    [InlineData(12345678.9, "one crore twenty three lakh forty five thousand six hundred seventy eight rupees and ninety paise only")]
    [InlineData(10000000, "one crore rupees only")]
    public void IndianNumberWords_UsesIndianPlaceValues(decimal amount, string expected) =>
        Assert.Equal(expected, IndianNumberWords.Convert(amount));

    [Fact]
    public void IndianNumberWords_RoundsPaiseHalfUp()
    {
        Assert.Equal("one rupee only", IndianNumberWords.Convert(0.995m));
        Assert.Equal("one rupee and one paise only", IndianNumberWords.Convert(1.005m));
    }

    [Theory]
    [InlineData(".pdf")]
    [InlineData(".xlsx")]
    [InlineData(".csv")]
    public async Task TabularExport_CreatesRequestedShareFormat(string extension)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        var directory = Path.Combine(Path.GetTempPath(), $"PharmaBill.Export.{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"output{extension}");
        try
        {
            await new TabularExportService().ExportAsync(
                path,
                "Test report",
                ["Date", "Amount"],
                new IReadOnlyList<string>[] { new[] { "2026-10-03", "12.50" } });

            Assert.True(new FileInfo(path).Length > 0);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static RetailBillPrintData CreateBill() =>
        new(
            "Test Pharmacy",
            "1 Main Road",
            "9876543210",
            "22ABCDE1234F1Z5",
            ["20B/123", "21B/456"],
            "A. Pharmacist",
            "B.Pharm",
            "REG-1234",
            "WIN1/2026-27/000001",
            DateTime.UtcNow,
            "Test Patient",
            "9876500000",
            "2 High Street",
            100m,
            12m,
            0m,
            112m,
            112m,
            [
                new RetailBillPrintLine(
                    "Test medicine",
                    "BATCH-1",
                    new DateOnly(2027, 12, 31),
                    1m,
                    112m,
                    12m,
                    12m,
                    0m,
                    112m)
            ]);
}
