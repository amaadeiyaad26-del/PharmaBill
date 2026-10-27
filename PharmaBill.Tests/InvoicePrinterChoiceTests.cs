using System.Printing;
using PharmaBill.App.Services;
using Xunit;

namespace PharmaBill.Tests;

// Which printers put the invoice in a file rather than on paper: silent F10 printing opens the invoice for viewing
// instead of sending it there, so Windows never stops the counter at a Save As box.
public sealed class InvoicePrinterChoiceTests
{
    [Theory]
    [InlineData("Microsoft Print to PDF", "PORTPROMPT:", "Microsoft Print To PDF", true)]
    [InlineData("Microsoft XPS Document Writer", "PORTPROMPT:", "Microsoft XPS Document Writer v4", true)]
    [InlineData("OneNote (Desktop)", "nul:", "Send to Microsoft OneNote 16 Driver", true)]
    [InlineData("Fax", "SHRFAX:", "Microsoft Shared Fax Driver", true)]
    [InlineData("Adobe PDF", "Documents\\*.pdf", "Adobe PDF Converter", true)]
    [InlineData("Save to file", "FILE:", "Generic / Text Only", true)]
    [InlineData("HP LaserJet M1005", "USB001", "HP LaserJet M1005", false)]
    [InlineData("TVS RP3160 Gold", "USB002", "TVS-E RP 3160 Gold", false)]
    [InlineData("EPSON L3250 Series", "WSD-6c1e9a2f", "EPSON L3250 Series", false)]
    public void WritesToFile_TellsFilePrintersFromPaperPrinters(string queue, string port, string driver, bool writesToFile) =>
        Assert.Equal(writesToFile, InvoicePrintService.WritesToFile(queue, port, driver));

    // This PC's own printers, as Windows reports them: Microsoft Print to PDF (on every Windows 10/11 PC) counts as a file.
    [Fact]
    public void ThisPcsMicrosoftPrintToPdf_IsAFilePrinter()
    {
        using var server = new LocalPrintServer();
        PrintQueue? pdf = server.GetPrintQueues().FirstOrDefault(q => q.Name.Contains("Microsoft Print to PDF", StringComparison.OrdinalIgnoreCase));
        if (pdf == null)
        {
            return;
        }
        Assert.True(InvoicePrintService.WritesToFile(pdf.Name, pdf.QueuePort?.Name, pdf.QueueDriver?.Name), $"{pdf.Name} on {pdf.QueuePort?.Name}");
    }

    [Fact]
    public void OpenedForViewing_IsTheInvoicePdfPath()
    {
        Assert.True(InvoicePrintService.OpenedForViewing(@"C:\Shop\Invoices\INV-000123.pdf"));
        Assert.False(InvoicePrintService.OpenedForViewing("HP LaserJet M1005"));
        Assert.False(InvoicePrintService.OpenedForViewing("cancelled"));
        Assert.False(InvoicePrintService.OpenedForViewing(null));
    }
}
