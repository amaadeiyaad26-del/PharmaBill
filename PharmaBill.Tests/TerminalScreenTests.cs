using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using PharmaBill.App;
using PharmaBill.App.Services;
using PharmaBill.App.ViewModels;
using PharmaBill.App.Views;
using PharmaBill.Core.Entities;
using PharmaBill.Data.Services;
using PharmaBill.Sync.Terminals;
using Xunit;

namespace PharmaBill.Tests;

// The billing-counter screens the way a cashier and an owner use them: the real windows, typed into and pressed,
// against a real HTTPS counter server on this PC. Only what needs a second PC is left out (see SESSION_HANDOVER.md).
public sealed class TerminalScreenTests : IAsyncLifetime
{
    private readonly TerminalServerTests _shop = new();

    public Task InitializeAsync() => _shop.InitializeAsync();

    public Task DisposeAsync() => _shop.DisposeAsync();

    [Fact]
    public void Pairing_TheCodeOnTheDevicesPanel_TypedIntoTheWelcomeWindow()
    {
        var serverSettings = new TerminalServerSettingsStore(Path.Combine(_shop.Root, "server-pc"));
        serverSettings.Save(new TerminalServerSettings(true, _shop.Server.Port));
        var counterStore = new TerminalClientSettingsStore(Path.Combine(_shop.Root, "counter-pc"));
        var owner = new CurrentSession();
        owner.SignIn(new AppUser { DisplayName = "Ravi (owner)", UserName = "owner", Role = UserRole.Owner });

        Ui.Run(async () =>
        {
            // On the server PC: Sync Station > Devices > Add a billing counter.
            var devices = new DevicesViewModel(_shop.Server, serverSettings, _shop.Devices, _shop.ServerPairing,
                new TerminalClientSettingsStore(Path.Combine(_shop.Root, "server-pc")), owner, new AlwaysYes());
            Window serverScreen = await Ui.ShowAsync(new DevicesView { DataContext = devices });
            await Ui.PressAsync(Ui.Button(serverScreen, "Add a billing counter"));
            string shown = Ui.Named<TextBlock>(serverScreen, "Pairing code").Text;
            Assert.Matches("^[A-Z2-9]{3} [A-Z2-9]{3}$", shown);
            string code = shown.Replace(" ", string.Empty);
            char other = TerminalPairingProof.Alphabet.First(c => c != code[0]);

            // On the new PC: first start, "This PC is a billing counter", the address and the code as read aloud.
            var welcome = new WelcomeWindow(counterStore);
            Ui.Prepare(welcome);
            var pairing = (TerminalPairingViewModel)welcome.DataContext;
            var done = new TaskCompletionSource();
            _ = welcome.Dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    await Ui.SettleAsync();
                    await Ui.PressAsync(Ui.Button(welcome, "This PC is a billing counter"));
                    Ui.Type(Ui.Named<TextBox>(welcome, "Server address"), "127.0.0.1:" + _shop.Server.Port);
                    Ui.Type(Ui.Named<TextBox>(welcome, "Counter name"), "Front counter");

                    Ui.Type(Ui.Named<TextBox>(welcome, "Pairing code"), other + code[1..]);
                    await Ui.PressAsync(Ui.Button(welcome, "Pair"));
                    Assert.True(welcome.IsVisible, "a wrong code closed the pairing window");
                    Assert.Contains("not right", pairing.ErrorMessage);
                    Ui.AssertPlain(pairing.ErrorMessage);

                    Ui.Type(Ui.Named<TextBox>(welcome, "Pairing code"), shown.ToLowerInvariant());
                    await Ui.PressAsync(Ui.Button(welcome, "Pair"));
                    done.TrySetResult();
                }
                catch (Exception exception)
                {
                    done.TrySetException(exception);
                    welcome.Close();
                }
            });
            bool? result = welcome.ShowDialog();
            await done.Task;

            Assert.True(result);
            TerminalClientSettings paired = Assert.IsType<TerminalClientSettings>(welcome.PairedCounter);
            Assert.Equal(_shop.Server.Fingerprint, paired.ServerFingerprint);
            Assert.Equal(paired.DeviceId, counterStore.Load()!.DeviceId);

            // Back on the server PC: the code is gone and the counter is listed.
            await Ui.UntilAsync(() => devices.Counters.Any(c => c.Name == "Front counter"), "the Devices panel to list the new counter");
            Assert.False(devices.IsPairingOpen);
            Assert.False(Ui.Named<TextBlock>(serverScreen, "Pairing code", visibleOnly: false).IsVisible);
            using var counter = new TerminalClient(paired);
            Assert.Equal("Front counter", (await counter.HelloAsync()).DeviceName);
        });
    }

    [Fact]
    public void SignIn_WithThePinTypedIntoThePinBox()
    {
        Ui.Run(async () =>
        {
            (Window window, TerminalViewModel counter, _) = await OpenCounterAsync();

            await SignInAsync(window, "clerk", "9999");
            Assert.False(counter.IsSignedIn);
            Assert.NotEqual(string.Empty, counter.ErrorMessage);
            Ui.AssertPlain(counter.ErrorMessage);
            Assert.True(Ui.Button(window, "Sign in").IsVisible);

            await SignInAsync(window, "clerk", "2222");
            Assert.True(counter.IsSignedIn, "signing in failed: " + counter.ErrorMessage);
            Assert.Equal(string.Empty, counter.ErrorMessage);
            Assert.StartsWith("Asha (cashier)", counter.SignedInAs);
            Assert.EndsWith("000001", counter.NextInvoiceNo);
            Assert.Equal(string.Empty, Ui.Named<PasswordBox>(window, "PIN", visibleOnly: false).Password);
            Assert.False(Ui.Named<PasswordBox>(window, "PIN", visibleOnly: false).IsVisible, "the sign-in panel is still covering the counter");

            await Ui.PressAsync(Ui.Button(window, "Sign out"));
            Assert.False(counter.IsSignedIn);
            Assert.True(Ui.Button(window, "Sign in").IsVisible);
        });
    }

    [Fact]
    public void ScheduleH1_WithoutAPrescription_IsBilledOnlyWithTheAdminPinTypedOnScreen()
    {
        Ui.Run(async () =>
        {
            (Window window, TerminalViewModel counter, List<RetailBillPrintData> printed) = await OpenCounterAsync();
            await SignInAsync(window, "clerk", "2222");
            await AddToBillAsync(window, counter, "Alprazolam");
            Ui.Type(Ui.Named<TextBox>(window, "Patient phone"), "9876500011");
            Ui.Type(Ui.Named<TextBox>(window, "Patient name"), "Rahul Kumar");
            Ui.Type(Ui.Named<TextBox>(window, "Patient address"), "4 Lal Chowk, Srinagar");
            Ui.Type(Ui.Named<TextBox>(window, "Doctor name"), "Dr A. Shah");
            Ui.Type(Ui.Named<TextBox>(window, "Doctor registration number"), "JKMC-4411");

            // The override panel appears for a controlled line. A wrong admin PIN bills nothing and keeps the bill.
            Ui.Type(Ui.Named<TextBox>(window, "Override admin user name"), "owner");
            PasswordBox overridePin = Ui.Named<PasswordBox>(window, "Override admin PIN");
            Ui.Type(overridePin, "0000");
            await Ui.PressAsync(Ui.Named<Button>(window, "Save bill"));
            Assert.NotEqual(string.Empty, counter.ErrorMessage);
            Ui.AssertPlain(counter.ErrorMessage);
            Assert.Single(counter.Lines);
            Assert.Equal(0, await _shop.QueryAsync(db => db.Sales.CountAsync()));

            Ui.Type(overridePin, "1111");
            await Ui.PressAsync(Ui.Named<Button>(window, "Save bill"));
            Assert.True(counter.ErrorMessage.Length == 0, counter.ErrorMessage);
            Assert.Contains("saved on the server", counter.StatusMessage);
            RetailBillPrintData bill = Assert.Single(printed);
            Assert.Equal("Rahul Kumar", bill.PatientName);
            Assert.Equal(1, await _shop.QueryAsync(db => db.AuditLogs.CountAsync(a => a.Action == "AdminOverrideGranted")));
            Assert.Equal(1, await _shop.QueryAsync(db => db.ScheduleRegisterEntries.CountAsync(e => e.SaleId != null)));

            // The bill is cleared for the next customer, and the admin PIN with it.
            Assert.Empty(counter.Lines);
            Assert.Equal(string.Empty, counter.OverridePin);
            Assert.Equal(string.Empty, overridePin.Password);
        });
    }

    [Fact]
    public void ReturnAndVoid_FromTheRecentBillsTab()
    {
        Ui.Run(async () =>
        {
            (Window window, TerminalViewModel counter, _) = await OpenCounterAsync();
            await SignInAsync(window, "pharmacist", "5555");
            string returned = await BillParacetamolAsync(window, counter, quantity: "3");
            string voided = await BillParacetamolAsync(window, counter, quantity: "1");
            Assert.Equal(2m, await _shop.StockOnHandAsync(_shop.ParacetamolBatch));

            Ui.Tab(window, "Recent bills");
            await Ui.PressAsync(Ui.Button(window, "Show"));
            await Ui.UntilAsync(() => counter.RecentBills.Count == 2, "the recent bills to load");
            DataGrid bills = Ui.Single<DataGrid>(window);

            // Return one strip of the first bill.
            await SelectBillAsync(bills, counter, returned);
            await Ui.UntilAsync(() => counter.ReturnLines.Count == 1, "the bill's lines to load");
            await Ui.SettleAsync();
            Ui.Type(Ui.Named<TextBox>(window, "Quantity to return"), "1");
            Ui.Type(Ui.Named<TextBox>(window, "Return reason"), "Customer brought one strip back");
            await Ui.PressAsync(Ui.Button(window, "Issue return"));
            Assert.True(counter.ErrorMessage.Length == 0, counter.ErrorMessage);
            Assert.StartsWith("Credit note", counter.StatusMessage);
            Assert.Equal(1, await _shop.QueryAsync(db => db.ReturnNotes.CountAsync()));
            Assert.Equal(3m, await _shop.StockOnHandAsync(_shop.ParacetamolBatch));

            // Void the second bill: a wrong admin PIN is refused, the right one voids it and puts the stock back.
            await SelectBillAsync(bills, counter, voided);
            Ui.Type(Ui.Named<TextBox>(window, "Void reason"), "Wrong medicine");
            Ui.Type(Ui.Named<TextBox>(window, "Admin user name"), "owner");
            PasswordBox adminPin = Ui.Named<PasswordBox>(window, "Admin PIN");
            Ui.Type(adminPin, "0000");
            await Ui.PressAsync(Ui.Button(window, "Void bill"));
            Assert.NotEqual(string.Empty, counter.ErrorMessage);
            Ui.AssertPlain(counter.ErrorMessage);
            Assert.Equal(3m, await _shop.StockOnHandAsync(_shop.ParacetamolBatch));

            Ui.Type(adminPin, "1111");
            await Ui.PressAsync(Ui.Button(window, "Void bill"));
            Assert.True(counter.ErrorMessage.Length == 0, counter.ErrorMessage);
            Assert.Contains(voided + " voided", counter.StatusMessage);
            Assert.Equal(4m, await _shop.StockOnHandAsync(_shop.ParacetamolBatch));
            Assert.Equal(string.Empty, adminPin.Password);

            // The void issued its own credit note for the whole bill.
            Assert.Equal(2, await _shop.QueryAsync(db => db.ReturnNotes.CountAsync()));

            // A cashier at the same counter is refused a return, and is told so.
            await Ui.PressAsync(Ui.Button(window, "Sign out"));
            await SignInAsync(window, "clerk", "2222");
            Ui.Tab(window, "Recent bills");
            await Ui.PressAsync(Ui.Button(window, "Show"));
            await SelectBillAsync(bills, counter, returned);
            await Ui.UntilAsync(() => counter.ReturnLines.Count == 1, "the bill's lines to load");
            await Ui.SettleAsync();
            Ui.Type(Ui.Named<TextBox>(window, "Quantity to return"), "1");
            Ui.Type(Ui.Named<TextBox>(window, "Return reason"), "Second strip");
            await Ui.PressAsync(Ui.Button(window, "Issue return"));
            Assert.NotEqual(string.Empty, counter.ErrorMessage);
            Ui.AssertPlain(counter.ErrorMessage);
            Assert.Equal(2, await _shop.QueryAsync(db => db.ReturnNotes.CountAsync()));
        });
    }

    // ---- helpers ----

    private async Task<(Window Window, TerminalViewModel Counter, List<RetailBillPrintData> Printed)> OpenCounterAsync()
    {
        TerminalClientSettings paired = await TerminalPairingClient.PairAsync("127.0.0.1", _shop.Server.Port, _shop.ServerPairing.Open().Code, "Front counter");
        var counter = new TerminalViewModel(paired, new TerminalClientSettingsStore(Path.Combine(_shop.Root, "counter-pc")),
            new DocumentOutputSettingsStore(Path.Combine(_shop.Root, "counter-pc", "print")));
        var printed = new List<RetailBillPrintData>();
        counter.PrintBill = bill =>
        {
            printed.Add(bill);
            return Task.CompletedTask;
        };
        var window = new TerminalWindow(counter);
        Ui.Prepare(window);
        window.Show();
        await Ui.UntilAsync(() => counter.IsConnected, "the counter to reach the server");
        return (window, counter, printed);
    }

    private static async Task SignInAsync(Window window, string user, string pin)
    {
        Ui.Type(Ui.Named<TextBox>(window, "User name"), user);
        Ui.Type(Ui.Named<PasswordBox>(window, "PIN"), pin);
        await Ui.PressAsync(Ui.Button(window, "Sign in"));
    }

    private static async Task AddToBillAsync(Window window, TerminalViewModel counter, string medicine)
    {
        Ui.Tab(window, "Bill");
        Ui.Type(Ui.Named<TextBox>(window, "Search medicine"), medicine);
        await Ui.PressAsync(Ui.Button(window, "Search"));
        Assert.NotEmpty(counter.SearchResults);
        int before = counter.Lines.Count;
        await Ui.PressAsync(Ui.Button(window, "Add to bill"));
        Assert.Equal(before + 1, counter.Lines.Count);
    }

    private static async Task<string> BillParacetamolAsync(Window window, TerminalViewModel counter, string quantity)
    {
        await AddToBillAsync(window, counter, "Paracetamol");
        Ui.Type(Ui.Named<TextBox>(window, "Quantity"), quantity);
        Ui.Type(Ui.Named<TextBox>(window, "Patient phone"), "9876543210");
        Ui.Type(Ui.Named<TextBox>(window, "Patient name"), "Walk-in");
        string invoice = counter.NextInvoiceNo;
        await Ui.PressAsync(Ui.Named<Button>(window, "Save bill"));
        Assert.True(counter.ErrorMessage.Length == 0, counter.ErrorMessage);
        Assert.Contains($"Bill {invoice} saved on the server", counter.StatusMessage);
        return invoice;
    }

    private static async Task SelectBillAsync(DataGrid bills, TerminalViewModel counter, string invoice)
    {
        await Ui.UntilAsync(() => counter.RecentBills.Any(b => b.InvoiceNo == invoice), "bill " + invoice + " in the list");
        bills.SelectedItem = counter.RecentBills.Single(b => b.InvoiceNo == invoice);
        // Selecting a bill fetches its returnable lines; the buttons are greyed out until they arrive.
        await Ui.UntilAsync(() => !counter.IsBusy, "bill " + invoice + " to finish loading", seconds: 5);
        Assert.Equal(invoice, counter.SelectedRecent?.InvoiceNo);
    }

    private sealed class AlwaysYes : IConfirmationService
    {
        public bool Confirm(string message, string title) => true;
        public bool? Choose(string message, string title, string acceptLabel, string alternateLabel) => true;
        public void Notify(string title, string message) { }
        public void NotifyError(string title, string message) { }
    }
}

// Drives real WPF windows on the shared UI-test thread: type into boxes, press buttons, wait for what the screen shows.
internal static class Ui
{
    public static void Run(Func<Task> body)
    {
        StaRunner.Run(() =>
        {
            EnsureApplication();
            Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext? previous = SynchronizationContext.Current;
            // Other UI tests share this thread; only the windows this test opens are its to close.
            var alreadyOpen = Application.Current.Windows.Cast<Window>().ToHashSet();
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            try
            {
                var frame = new DispatcherFrame();
                Task task = body();
                task.ContinueWith(_ => dispatcher.BeginInvoke(new Action(() => frame.Continue = false)), TaskScheduler.Default);
                var timeout = new DispatcherTimer(TimeSpan.FromMinutes(2), DispatcherPriority.Normal, (_, _) => frame.Continue = false, dispatcher);
                Dispatcher.PushFrame(frame);
                timeout.Stop();
                if (!task.IsCompleted)
                {
                    throw new TimeoutException("The screen test did not finish within two minutes.");
                }
                task.GetAwaiter().GetResult();
            }
            finally
            {
                foreach (Window window in Application.Current.Windows.Cast<Window>().Where(w => !alreadyOpen.Contains(w)).ToList())
                {
                    window.Close();
                }
                SynchronizationContext.SetSynchronizationContext(previous);
            }
        });
    }

    public static void Prepare(Window window)
    {
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -20000;
        window.Top = 0;
        window.ShowActivated = false;
        window.ShowInTaskbar = false;
    }

    public static async Task<Window> ShowAsync(UIElement content)
    {
        var window = new Window { Content = content, Width = 1100, Height = 900 };
        Prepare(window);
        window.Show();
        await SettleAsync();
        return window;
    }

    public static async Task SettleAsync()
    {
        await Dispatcher.Yield(DispatcherPriority.ContextIdle);
        foreach (Window window in Application.Current.Windows)
        {
            window.UpdateLayout();
        }
        await Dispatcher.Yield(DispatcherPriority.ContextIdle);
    }

    public static async Task PressAsync(Button button)
    {
        await SettleAsync();
        string label = Label(button);
        Assert.True(button.IsVisible, $"\"{label}\" is not on screen");
        Assert.True(button.IsEnabled, $"\"{label}\" is greyed out");
        var invoke = (IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke);
        invoke.Invoke();
        await SettleAsync();
        if (button.Command is IAsyncRelayCommand { ExecutionTask: Task running })
        {
            await running;
        }
        await SettleAsync();
    }

    public static void Type(TextBox box, string text)
    {
        Assert.True(box.IsVisible && box.IsEnabled, $"the \"{AutomationProperties.GetName(box)}\" box can't be typed into");
        box.Focus();
        box.Text = text;
    }

    public static void Type(PasswordBox box, string text)
    {
        Assert.True(box.IsVisible && box.IsEnabled, $"the \"{AutomationProperties.GetName(box)}\" box can't be typed into");
        box.Focus();
        box.Password = text;
    }

    public static void Tab(Window window, string header)
    {
        Descendants(window).OfType<TabItem>().Single(tab => Equals(tab.Header, header)).IsSelected = true;
        window.UpdateLayout();
    }

    public static async Task UntilAsync(Func<bool> condition, string what, int seconds = 30)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("Waited " + seconds + " s for " + what + ".");
            }
            await Task.Delay(50);
        }
        await SettleAsync();
    }

    public static T Named<T>(DependencyObject root, string name, bool visibleOnly = true) where T : FrameworkElement
    {
        List<T> found = Descendants(root).OfType<T>().Where(e => AutomationProperties.GetName(e) == name && (!visibleOnly || e.IsVisible)).ToList();
        Assert.True(found.Count == 1, $"expected one {(visibleOnly ? "visible " : string.Empty)}{typeof(T).Name} named \"{name}\", found {found.Count}");
        return found[0];
    }

    public static Button Button(DependencyObject root, string label)
    {
        List<Button> found = Descendants(root).OfType<Button>().Where(b => b.IsVisible && Label(b) == label).ToList();
        Assert.True(found.Count == 1, $"expected one \"{label}\" button on screen, found {found.Count}");
        return found[0];
    }

    public static T Single<T>(DependencyObject root) where T : FrameworkElement => Descendants(root).OfType<T>().Single(e => e.IsVisible);

    /// <summary>What a shopkeeper reads: no parameter names, type names or stack text.</summary>
    public static void AssertPlain(string message)
    {
        Assert.DoesNotContain("Parameter", message);
        Assert.DoesNotContain("Exception", message);
        Assert.DoesNotContain(" at ", message);
    }

    private static string Label(Button button) => button.Content as string
        ?? Descendants(button).OfType<TextBlock>().Select(t => t.Text).FirstOrDefault()
        ?? AutomationProperties.GetName(button);

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        var pending = new Stack<DependencyObject>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            DependencyObject node = pending.Pop();
            yield return node;
            if (node is Visual or Visual3D)
            {
                for (int i = VisualTreeHelper.GetChildrenCount(node) - 1; i >= 0; i--)
                {
                    pending.Push(VisualTreeHelper.GetChild(node, i));
                }
            }
        }
    }

    private static void EnsureApplication()
    {
        _ = System.IO.Packaging.PackUriHelper.UriSchemePack;
        Application app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        if (app.TryFindResource("PrimaryButton") == null)
        {
            foreach (string path in new[] { "Themes/Light.xaml", "Resources/Strings.en.xaml", "Resources/Controls.xaml" })
            {
                app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"pack://application:,,,/PharmaBill.App;component/{path}", UriKind.Absolute) });
            }
        }
        TestAppResources.AddAppLevelBrushes(app.Resources);
    }
}
