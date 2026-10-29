using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PharmaBill.App.Services;
using PharmaBill.App.ViewModels;
using PharmaBill.Core.Ai;
using PharmaBill.Core.Entities;
using PharmaBill.Core.Security;
using PharmaBill.Data.Persistence;
using PharmaBill.Data.Services;
using Xunit;

namespace PharmaBill.Tests;

// A supplier's invoice scanned on the purchase screen and saved, the whole way: the photo is read on this PC (Gemini is
// not available), each line keeps its own HSN and GST from its own row on a mixed-rate invoice, the medicine that is
// not in the master is created from them, and nothing asks for an HSN or a rate.
public sealed class ScannedPurchaseEndToEndTests
{
    private sealed class InvoiceOcr(string text) : IOcrTextRecognizer
    {
        public Task<string> RecognizeAsync(byte[] imageBytes, CancellationToken cancellationToken = default) => Task.FromResult(text);
        public Task<IReadOnlyList<OcrWordPosition>> RecognizeWordsAsync(byte[] imageBytes, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<OcrWordPosition>>([]);
    }

    private sealed class NeverAsked : IPromptService
    {
        public List<string> Asked { get; } = [];
        public string? AskText(string title, string message, string label, string? initialText = null) { Asked.Add(title); return null; }
        public ProductTaxPromptResult? AskProductTax(string productName, string? currentHsn, decimal? currentRate, bool currentNilRated, bool newProduct = false, ProductTaxHints? hints = null)
        {
            Asked.Add("tax: " + productName);
            return null;
        }
    }

    private sealed class Allowed : IEntitlementService
    {
        public Task<EntitlementStatus> GetStatusAsync(CancellationToken cancellationToken = default) => Task.FromResult(EntitlementStatus.TRIAL);
        public Task<bool> IsReadOnlyAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> CanPerformAsync(ProtectedOperation operation, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    [Fact]
    public void ScannedMixedRateInvoice_IsSaved_WithEachLinesOwnTax_AndNothingIsAsked()
    {
        Ui.Run(async () =>
        {
            await using var database = await DatabaseTestContext.CreateAsync();
            var catalog = await E2EHealthSeed.SeedAsync(database.Context);
            Supplier supplier = catalog.Suppliers[0];
            string invoiceText = $"""
                Seller (Wholesaler)
                M/s {supplier.Name}
                Invoice No.: MIX/2026/0042
                Date: 20/10/2026
                Product                        HSN       Batch    Exp    Qty   Rate    GST   Amount
                Zentrix 250 Tablet             30042019  ZTX2501  06/27  10    40.00   12%   400.00
                Paracetamol Tablets IP 500 mg  30049099  PCT2407  06/27  20    18.00   5%    360.00
                Taxable value
                Rs. 760.00
                CGST @ 2.5% SGST @ 2.5% CGST @ 6% SGST @ 6%
                Grand total
                Rs. 826.00
                """;

            var prompt = new NeverAsked();
            var allowed = new Allowed();
            ServiceProvider services = new ServiceCollection()
                .AddSingleton(database.Storage)
                .AddSingleton(database.BranchSettings)
                .AddSingleton<IEntitlementService>(allowed)
                .AddSingleton<ILicenseRuntimeGuard, NullLicenseRuntimeGuard>()
                .AddScoped(_ => database.OpenContext())
                .AddScoped<IUnitOfWork>(sp => new UnitOfWork(sp.GetRequiredService<PharmaBillDbContext>()))
                .AddScoped<NumberSeriesService>()
                .AddScoped<StorageLocationService>()
                .AddScoped<BranchService>()
                .AddScoped<PurchaseService>()
                .AddScoped<AddStockService>()
                .AddScoped<CustomMedicineService>()
                .AddScoped<ProductTaxService>()
                .AddSingleton<IPromptService>(prompt)
                // Gemini is not registered: the screen reads the photo on this PC, as it does when Gemini is unavailable.
                .AddSingleton(_ => new InvoiceDocumentExtractor(null!, null!, new LaidOutOcr(invoiceText)))
                .BuildServiceProvider();
            var session = new CurrentSession();
            session.SignIn(catalog.Admin);
            var page = new PurchasePageViewModel(services.GetRequiredService<IServiceScopeFactory>(), session, null!, new BackupPageEndToEndTests.AlwaysYes(),
                null!, prompt, null!, null!, null!, new LaidOutOcr(invoiceText), null!);
            await page.LoadAsync();

            var mainWindow = new PharmaBill.App.MainWindow(null!, null!, null!, null!, new BindingSafetyTests.NullScannerService());
            BindingSafetyTests.Retire(mainWindow);
            var host = new Window { Width = 1366, Height = 900 };
            Ui.Prepare(host);
            host.Show();
            host.Content = new ContentControl { Content = page, ContentTemplate = (DataTemplate)mainWindow.Resources[new DataTemplateKey(typeof(PurchasePageViewModel))], Resources = mainWindow.Resources };
            await Ui.SettleAsync();

            // The scan: a photo of the invoice.
            string photo = Path.Combine(TestAppData.Root, "invoice-" + Guid.NewGuid().ToString("N") + ".png");
            WritePhoto(photo);
            await page.ImportDocumentFromPathAsync(photo);
            await Ui.SettleAsync();
            string staged = string.Join(" | ", page.StagedItems.Select(item => $"{item.ItemName} [{item.Batch}] gst {item.Gst} hsn {item.HsnCode}"));
            Assert.True(page.StagedItems.Count == 2, "staged: " + staged);
            await Ui.PressAsync(Ui.Button(host, "Import Rows into Table"));
            Assert.True(page.Lines.Count == 2, $"lines applied: {page.Lines.Count}, staged {page.StagedItems.Count}; {page.ErrorMessage} {page.ImportStatus}");
            PurchaseLineDraft zentrix = page.Lines.Single(line => line.MedicineName.Contains("Zentrix", StringComparison.OrdinalIgnoreCase));
            PurchaseLineDraft paracetamol = page.Lines.Single(line => line.MedicineName.Contains("Paracetamol", StringComparison.OrdinalIgnoreCase));
            Assert.Equal((12m, "30042019"), (zentrix.GstRate, zentrix.HsnCode));
            Assert.Equal(5m, paracetamol.GstRate);
            Assert.Null(zentrix.Drug);

            // Saved through the screen's own button: the new medicine is created from its invoice line, nothing asked.
            await Ui.PressAsync(Ui.Button(host, "Transfer to Stock / F10"));
            Assert.Empty(prompt.Asked);
            Assert.True(page.ErrorMessage.Length == 0, page.ErrorMessage);
            await using PharmaBillDbContext check = database.OpenContext();
            Drug created = await check.Drugs.AsNoTracking().SingleAsync(drug => drug.Name.Contains("Zentrix"));
            Assert.Equal(("30042019", (decimal?)12m), (created.HsnCode, created.GstRate));
            PurchaseInvoice invoice = await check.PurchaseInvoices.AsNoTracking().SingleAsync(item => item.InvoiceNo == "MIX/2026/0042");
            List<decimal> rates = await check.PurchaseItems.AsNoTracking().Where(item => item.PurchaseInvoiceId == invoice.Id).Select(item => item.TaxRate).OrderBy(rate => rate).ToListAsync();
            Assert.Equal([5m, 12m], rates);
            host.Close();
        });
    }

    internal static void WritePhoto(string path)
    {
        var bitmap = new RenderTargetBitmap(64, 64, 96, 96, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (DrawingContext context = visual.RenderOpen())
        {
            context.DrawRectangle(Brushes.White, null, new Rect(0, 0, 64, 64));
        }
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using FileStream file = File.Create(path);
        encoder.Save(file);
    }
}
