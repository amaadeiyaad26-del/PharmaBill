using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using PharmaBill.App.Services;
using PharmaBill.App.ViewModels;
using Xunit;

namespace PharmaBill.Tests;

// Grid columns are not part of the visual tree, so a column bound with RelativeSource silently gets nothing: an
// empty dropdown, a blank header. These columns reach their page through a proxy; the screens show them filled.
public sealed class GridColumnBindingTests
{
    [Fact]
    public void PurchaseReturn_BatchColumn_ListsTheReturnableBatches()
    {
        Ui.Run(async () =>
        {
            using BindingErrorWatch bindings = BindingErrorWatch.Start();
            using ServiceProvider services = new ServiceCollection().BuildServiceProvider();
            var page = new PurchasePageViewModel(services.GetRequiredService<IServiceScopeFactory>(), new CurrentSession(), null!, null!, null!, null!, null!, null!, null!, null!, null!);
            var batch = new BatchReturnOption(Guid.NewGuid(), Guid.NewGuid(), "PCM-01", "Paracetamol 650 / PCM-01 / Valley / Qty 10", 18m);
            page.ReturnBatches.Add(batch);
            page.ReturnLines.Add(new PurchaseReturnLineDraft(null, 1m));
            Window host = await ShowAsync(page, typeof(PurchasePageViewModel));
            Descendants(host).OfType<Expander>().Single(expander => Equals(expander.Header, "Purchase return")).IsExpanded = true;
            await Ui.SettleAsync();

            DataGrid grid = Descendants(host).OfType<DataGrid>().Single(candidate => candidate.ItemsSource == page.ReturnLines);
            var column = Assert.IsType<DataGridComboBoxColumn>(grid.Columns[0]);
            Assert.Same(page.ReturnBatches, column.ItemsSource);
            bindings.AssertNone();
            host.Close();
        });
    }

    [Fact]
    public void Ageing_FirstColumn_IsHeadedWithThePartyKind()
    {
        Ui.Run(async () =>
        {
            using BindingErrorWatch bindings = BindingErrorWatch.Start();
            using ServiceProvider services = new ServiceCollection().BuildServiceProvider();
            var page = new WholesaleAccountsPageViewModel(services.GetRequiredService<IServiceScopeFactory>(), new CurrentSession(), null!, null!, new BillingDeskState());
            Window host = await ShowAsync(page, typeof(WholesaleAccountsPageViewModel));
            Descendants(host).OfType<TabItem>().Single(tab => Equals(tab.Header, page.CustomerAccountsTabHeader)).IsSelected = true;
            await Ui.SettleAsync();

            DataGrid ageing = Descendants(host).OfType<DataGrid>().Single(candidate => candidate.ItemsSource == page.Dues);
            Assert.False(string.IsNullOrWhiteSpace(page.AgeingPartyHeader));
            Assert.Equal(page.AgeingPartyHeader, ageing.Columns[0].Header);
            bindings.AssertNone();
            host.Close();
        });
    }

    private static async Task<Window> ShowAsync(object page, Type pageType)
    {
        var mainWindow = new PharmaBill.App.MainWindow(null!, null!, null!, null!, new BindingSafetyTests.NullScannerService());
        BindingSafetyTests.Retire(mainWindow);
        var host = new Window { Width = 1500, Height = 1000 };
        Ui.Prepare(host);
        host.Show();
        host.Content = new ContentControl { Content = page, ContentTemplate = (DataTemplate)mainWindow.Resources[new DataTemplateKey(pageType)], Resources = mainWindow.Resources };
        await Ui.SettleAsync();
        return host;
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            foreach (DependencyObject child in Descendants(VisualTreeHelper.GetChild(root, i)))
            {
                yield return child;
            }
        }
    }
}
