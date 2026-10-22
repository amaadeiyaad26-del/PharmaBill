using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.DependencyInjection;
using PharmaBill.App;
using PharmaBill.App.Services;
using PharmaBill.Core.Entities;
using PharmaBill.Core.Security;
using PharmaBill.Data.Services;
using QuestPDF.Infrastructure;
using Xunit;

namespace PharmaBill.Tests;

// The "Invoice details" window opened from Reports & Daily MIS: Print, Save as PDF and Export pressed in the real window,
// for a retail bill and a wholesale invoice.
public sealed class InvoiceDetailWindowTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"PharmaBill.InvoiceDetail.{Guid.NewGuid():N}");

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void Print_SaveAsPdf_AndExport_ProduceTheInvoice()
    {
        QuestPDF.Settings.License = LicenseType.Community;
        Ui.Run(async () =>
        {
            await using var database = await DatabaseTestContext.CreateAsync();
            var catalog = await E2EHealthSeed.SeedAsync(database.Context);
            var allowed = new Allowed();
            RetailSaleResult sale = await database.CreateRetailBillingService(allowed).SaveSaleAsync(
                new SaveRetailSaleInput(catalog.RetailPatients[1].Name, catalog.RetailPatients[1].Phone!, catalog.RetailPatients[1].Address, null, null, null,
                    [new RetailSaleLineInput(catalog.Paracetamol650.Id, catalog.ParacetamolBatch.Id, 2m, 0m, 30m)], [new RetailPaymentInput("Cash", 60m)], false),
                catalog.Admin.Id, UserRole.Pharmacist);
            WholesaleInvoiceResult wholesale = await database.CreateWholesaleInvoiceService(allowed).SaveAsync(
                new SaveWholesaleInvoiceInput(catalog.BurhanMedicalHall.Id, [new WholesaleInvoiceLineInput(catalog.Paracetamol650.Id, catalog.ParacetamolBatch.Id, 5m, 0m, 28m)],
                    0m, "Credit", null, false, 90, false, null, null, null, null, null, "Reports test"),
                catalog.Admin.Id, UserRole.Owner);

            ServiceProvider services = new ServiceCollection()
                .AddSingleton(database.Context)
                .AddScoped(_ => database.CreateRetailBillingService(allowed))
                .BuildServiceProvider();
            IServiceScopeFactory scopes = services.GetRequiredService<IServiceScopeFactory>();
            string downloads = Path.Combine(_root, "Downloads");
            var documents = new InvoiceDocumentService(
                new RetailBillPdfService(scopes, new DocumentOutputSettingsStore(Path.Combine(_root, "settings"))),
                new WholesaleInvoiceDocumentService(scopes, Path.Combine(_root, "wholesale-documents")),
                downloads);
            var reports = new ReportsDashboardService(database.Context, database.CreateBranchService(), new SalesAnalysisService(database.Context));

            foreach (string key in new[] { "R|" + sale.Sale.Id, "W|" + wholesale.Invoice.Id })
            {
                InvoiceDetail detail = (await reports.GetInvoiceDetailAsync(key))!;
                var window = new InvoiceDetailWindow(detail, documents);
                var printed = new List<BitmapSource>();
                string? saveTo = null;
                window.PrintPages = (pages, _) =>
                {
                    printed.AddRange(pages);
                    return true;
                };
                window.PickPdfDestination = _ => saveTo;
                Ui.Prepare(window);
                window.Show();
                await Ui.SettleAsync();
                var status = (TextBlock)window.FindName("ActionStatus");
                Assert.True(Ui.Button(window, "Close").IsVisible);

                // Print: the invoice's pages go to the print dialog.
                await PressAndWaitAsync(window, "Print");
                Assert.Equal("Sent to the printer.", status.Text);
                Assert.NotEmpty(printed);
                Assert.True(printed[0].PixelWidth > 1000, "pages are rendered at print resolution");

                // Save as PDF: cancelling saves nothing; choosing a file saves the invoice there.
                await PressAndWaitAsync(window, "Save as PDF");
                Assert.Equal(string.Empty, status.Text);
                saveTo = Path.Combine(_root, "chosen", InvoiceDocumentService.FileName(detail));
                await PressAndWaitAsync(window, "Save as PDF");
                Assert.Equal("Saved: " + saveTo, status.Text);
                AssertPdf(saveTo);

                // Export: straight to Downloads, never over an earlier copy.
                await PressAndWaitAsync(window, "Export");
                await PressAndWaitAsync(window, "Export");
                string stem = Path.GetFileNameWithoutExtension(InvoiceDocumentService.FileName(detail));
                Assert.Equal("Saved to Downloads: " + stem + " (2).pdf", status.Text);
                AssertPdf(Path.Combine(downloads, stem + ".pdf"));
                AssertPdf(Path.Combine(downloads, stem + " (2).pdf"));
                Assert.Equal(detail.IsWholesale ? "Wholesale invoice" : "Retail bill", stem.Split(' ')[0] + " " + stem.Split(' ')[1]);
                window.Close();
            }
        });
    }

    private static async Task PressAndWaitAsync(System.Windows.Window window, string label)
    {
        Button button = Ui.Button(window, label);
        await Ui.PressAsync(button);
        await Ui.UntilAsync(() => button.IsEnabled, "\"" + label + "\" to finish");
    }

    private static void AssertPdf(string path)
    {
        Assert.True(File.Exists(path), "no PDF at " + path);
        byte[] head = File.ReadAllBytes(path)[..5];
        Assert.Equal("%PDF-", System.Text.Encoding.ASCII.GetString(head));
    }

    private sealed class Allowed : IEntitlementService
    {
        public Task<EntitlementStatus> GetStatusAsync(CancellationToken cancellationToken = default) => Task.FromResult(EntitlementStatus.TRIAL);
        public Task<bool> IsReadOnlyAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> CanPerformAsync(ProtectedOperation operation, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }
}
