using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PharmaBill.App.Behaviors;
using PharmaBill.App.Controls;
using PharmaBill.App.ViewModels;
using Xunit;

namespace PharmaBill.Tests;

// A counter runs all day, opening the same screens again and again. A screen's controls are built afresh on every
// visit while the bills and search results they show live all session: a closed screen must not stay in memory.
[Collection(RunsAlone.Name)]
public sealed class ScreenLeakTests
{
    // The two things RowFlash used to hold a closed screen's grid by: its handler on the bill (which lives all session) and
    // WPF's property-descriptor table, which keeps every element registered with AddValueChanged. Checked directly rather
    // than by collecting the grid: in a shared test process, once a PIN dialog has come to the foreground an accessibility
    // client can attach and keep any DataGrid alive, RowFlash or not.
    [Fact]
    public void BillGrid_WithRowFlash_LetsGoOfTheBillWhenItsScreenCloses()
    {
        Ui.Run(async () =>
        {
            var bill = new ObservableCollection<string> { "Paracetamol" };
            var host = new ContentControl();
            Window window = await Ui.ShowAsync(host);
            var grid = new DataGrid { ItemsSource = bill };
            RowFlash.SetEnabled(grid, true);

            host.Content = grid;
            await Ui.SettleAsync();
            Assert.Equal(1, RowFlashHandlers(bill));
            Assert.True(TrackedForItemsSource(grid), "the check cannot see the descriptor table");

            host.Content = null;
            await Ui.SettleAsync();
            Assert.Equal(0, RowFlashHandlers(bill));
            Assert.False(TrackedForItemsSource(grid), "the closed grid is still registered with the property descriptor");

            // Shown again (the same screen reopened), it flashes new rows again.
            host.Content = grid;
            await Ui.SettleAsync();
            Assert.Equal(1, RowFlashHandlers(bill));
            window.Close();
        });
    }

    private static int RowFlashHandlers<T>(ObservableCollection<T> collection) =>
        (typeof(ObservableCollection<T>).GetField("CollectionChanged", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(collection) as Delegate)?
            .GetInvocationList()
            .Count(handler => handler.Method.DeclaringType == typeof(RowFlash) || handler.Method.DeclaringType?.DeclaringType == typeof(RowFlash)) ?? 0;

    private static bool TrackedForItemsSource(DataGrid grid)
    {
        var descriptor = DependencyPropertyDescriptor.FromProperty(ItemsControl.ItemsSourceProperty, typeof(DataGrid));
        object inner = typeof(DependencyPropertyDescriptor).GetProperty("Property", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(descriptor)!;
        var trackers = (System.Collections.IDictionary?)inner.GetType().GetField("_trackers", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(inner);
        return trackers?.Contains(grid) == true;
    }

    [Fact]
    public void StockSearchControl_IsReleasedWhenItsScreenCloses()
    {
        Ui.Run(async () =>
        {
            using ServiceProvider services = new ServiceCollection().BuildServiceProvider();
            var search = new CatalogSearchViewModel(services.GetRequiredService<IServiceScopeFactory>(), NullLogger<CatalogSearchViewModel>.Instance) { IsVisible = true };
            var host = new ContentControl();
            Window window = await Ui.ShowAsync(host);

            WeakReference control = await ShowAndRemoveAsync(host, () => new GlobalMedicineSearchControl { ShowActions = true, DataContext = search });
            Assert.False(await IsAliveAfterCollectionAsync(control), "the search control of a closed screen is still in memory");
            window.Close();
        });
    }

    // Built and dropped in its own method so no local in the test keeps it alive.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<WeakReference> ShowAndRemoveAsync(ContentControl host, Func<FrameworkElement> build)
    {
        FrameworkElement element = build();
        host.Content = element;
        await SettleAsync(host);
        Assert.True(element.IsLoaded);
        host.Content = null;
        await SettleAsync(host);
        return new WeakReference(element);
    }

    // Lets this thread's dispatcher finish layout and loading; uses nothing owned by another UI thread.
    private static async Task SettleAsync(FrameworkElement host)
    {
        await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ContextIdle);
        host.UpdateLayout();
        await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ContextIdle);
    }

    private static async Task<bool> IsAliveAfterCollectionAsync(WeakReference reference)
    {
        for (int attempt = 0; attempt < 5 && reference.IsAlive; attempt++)
        {
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ContextIdle);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
        return reference.IsAlive;
    }
}
