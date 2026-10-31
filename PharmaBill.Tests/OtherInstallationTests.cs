using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Microsoft.Data.Sqlite;
using PharmaBill.App;
using PharmaBill.App.Controls;
using PharmaBill.App.Services;
using PharmaBill.App.ViewModels;
using PharmaBill.Data.Persistence;
using Xunit;
using Xunit.Abstractions;

namespace PharmaBill.Tests;

// Both versions of Pharma Remit on one computer: the Store version's first run never copies the website version's
// records without the pharmacist's choice; taking them over leaves exactly one usable copy and a new device identity;
// each edition warns on its dashboards when the other is installed, once, and never on a billing screen.
// Everything here works in a temporary folder; nothing reads or writes %LocalAppData%\PharmaBill.
[Collection(RunsAlone.Name)]
public sealed class OtherInstallationTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _sandbox = Path.Combine(Path.GetTempPath(), $"PharmaBill.TwoInstalls.{Guid.NewGuid():N}");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_sandbox))
        {
            Directory.Delete(_sandbox, recursive: true);
        }
    }

    /// <summary>Stands in for %LocalAppData%\PharmaBill holding the website version's records.</summary>
    private string WebsiteRoot => Path.Combine(_sandbox, "AppData", "Local", "PharmaBill");

    private string Profile => Path.Combine(_sandbox, "Users", "Shop");

    // The website version's records as it leaves them: a pharmacy database with its key and created-note, the device
    // identity and sync keys, a backup, a prescription, logs and licence state.
    private string CreateWebsiteRecords()
    {
        SQLitePCL.Batteries_V2.Init();
        string password = Convert.ToHexString(new DatabaseEncryptionKeyProvider(Path.Combine(WebsiteRoot, @"data\database.key")).GetOrCreateKey());
        string db = Path.Combine(WebsiteRoot, @"Sameerchemists\pharmabill.db");
        Directory.CreateDirectory(Path.GetDirectoryName(db)!);
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = db, Password = password, Pooling = false }.ToString()))
        {
            connection.Open();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE Records (Text TEXT); INSERT INTO Records VALUES ('Bill SM/2026-27/000412');";
            command.ExecuteNonQuery();
        }
        foreach ((string relative, string content) in new[]
        {
            (@"Sameerchemists\pharmabill.db.created", "A Pharma Remit database exists at this path; if it goes missing, it is not recreated. 2026-10-01T00:00:00Z"),
            ("device.id", Guid.NewGuid().ToString()),
            ("sync-peer-keys.dat", "paired phone keys"),
            (@"data\backups\daily.pbbak", "backup"),
            (@"prescriptions\RX-1.pdf", "%PDF-1.7"),
            (@"data\logs\pharmabill.log", "log"),
            (@"data\license.state", "licence")
        })
        {
            string path = Path.Combine(WebsiteRoot, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }
        SqliteConnection.ClearAllPools();
        return password;
    }

    private Dictionary<string, byte[]> Hashes(string root, bool withNotes = false) =>
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(file => withNotes || !file.EndsWith(".created", StringComparison.OrdinalIgnoreCase))
            // The Store version's own pointer: in a real install it is in the Store app's private storage, not here.
            .Where(file => !Path.GetFileName(file).Equals(StoreDataLocation.PointerFileName, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(file => Path.GetRelativePath(root, file), file => SHA256.HashData(File.ReadAllBytes(file)), StringComparer.OrdinalIgnoreCase);

    // ---------------------------------------------------------------- the rules

    [Fact]
    public void FirstRun_WithTheWebsiteVersionsRecords_CopiesNothing()
    {
        CreateWebsiteRecords();
        Dictionary<string, byte[]> before = Hashes(WebsiteRoot, withNotes: true);
        string target = StoreDataLocation.DefaultFolder(Profile);

        Assert.Single(StoreDataLocation.OtherInstallationDatabases(WebsiteRoot, ownPrivateRoot: null));
        OtherInstallationRecordsException refused = Assert.Throws<OtherInstallationRecordsException>(() => StoreDataLocation.Choose(WebsiteRoot, target));

        Assert.Contains("were not copied", refused.Message);
        Assert.False(Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any());
        Assert.Equal(DataLocationStatus.NotChosen, StoreDataLocation.Check(WebsiteRoot).Status);
        Assert.Equal(before, Hashes(WebsiteRoot, withNotes: true));
    }

    [Fact]
    public void TheStoreVersionsOwnEarlierData_StillMovesAsBefore()
    {
        CreateWebsiteRecords();
        // Windows keeps the files the Store package created in its private folder: these are its own.
        string own = Path.Combine(_sandbox, "Packages", "S.A.E.R.PharmaBill_x", "LocalCache", "Local", "PharmaBill");
        Directory.CreateDirectory(Path.Combine(own, "Sameerchemists"));
        File.Copy(Path.Combine(WebsiteRoot, @"Sameerchemists\pharmabill.db"), Path.Combine(own, @"Sameerchemists\pharmabill.db"));

        Assert.Empty(StoreDataLocation.OtherInstallationDatabases(WebsiteRoot, own));
        Assert.Equal(DataLocationOutcome.MigratedFromPackage, StoreDataLocation.Choose(WebsiteRoot, StoreDataLocation.DefaultFolder(Profile), own).Outcome);
    }

    [Fact]
    public void TakingOver_LeavesOneUsableCopy_WithItsOwnDeviceIdentity()
    {
        string password = CreateWebsiteRecords();
        Dictionary<string, byte[]> before = Hashes(WebsiteRoot);
        string target = StoreDataLocation.DefaultFolder(Profile);

        DataLocationResult result = StoreDataLocation.TakeOver(WebsiteRoot, target);

        Assert.Equal((DataLocationOutcome.TakenOver, Path.GetFullPath(target)), (result.Outcome, result.DataRoot));
        // The records arrived and open; the device identity, sync keys, logs and licence stayed behind.
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.Combine(target, @"Sameerchemists\pharmabill.db"), Password = password, Pooling = false }.ToString()))
        {
            connection.Open();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT Text FROM Records";
            Assert.Equal("Bill SM/2026-27/000412", command.ExecuteScalar());
        }
        Assert.True(File.Exists(Path.Combine(target, @"data\backups\daily.pbbak")));
        foreach (string staysBehind in new[] { "device.id", "sync-peer-keys.dat", @"data\logs\pharmabill.log", @"data\license.state", @"Sameerchemists\pharmabill.db.created" })
        {
            Assert.False(File.Exists(Path.Combine(target, staysBehind)), staysBehind + " was copied");
        }
        // The originals are unchanged; only the note beside the database now says it was taken over.
        Assert.Equal(before, Hashes(WebsiteRoot));
        Assert.Contains("TakenOverByMicrosoftStoreVersion", File.ReadAllText(Path.Combine(WebsiteRoot, @"Sameerchemists\pharmabill.db.created")));
        // The website version will not open them: exactly one copy is in use.
        TakeoverRecord takeover = Assert.IsType<TakeoverRecord>(StoreDataLocation.FindTakeover(WebsiteRoot, []));
        Assert.Equal(Path.GetFullPath(target), takeover.Folder);
        Assert.Equal((DataLocationStatus.Ready, Path.GetFullPath(target)), (StoreDataLocation.Check(WebsiteRoot).Status, StoreDataLocation.Check(WebsiteRoot).DataRoot));
    }

    [Fact]
    public void TheWebsiteVersion_FindsTheTakeover_FromTheStoreFolderAlone()
    {
        CreateWebsiteRecords();
        string target = StoreDataLocation.DefaultFolder(Profile);
        StoreDataLocation.TakeOver(WebsiteRoot, target);
        // Even if the note beside the database is not there, the Store folder's marker says where its records came from.
        File.Delete(Path.Combine(WebsiteRoot, @"Sameerchemists\pharmabill.db.created"));
        string pointer = Path.Combine(WebsiteRoot, StoreDataLocation.PointerFileName);

        Assert.Null(StoreDataLocation.FindTakeover(WebsiteRoot, []));
        Assert.Equal(Path.GetFullPath(target), StoreDataLocation.FindTakeover(WebsiteRoot, [pointer])!.Folder);
    }

    [Fact]
    public void StartingANewPharmacy_CopiesNothing_AndLocksNothing()
    {
        CreateWebsiteRecords();
        Dictionary<string, byte[]> before = Hashes(WebsiteRoot, withNotes: true);
        string target = Path.Combine(Profile, "New pharmacy");

        DataLocationResult result = StoreDataLocation.StartNew(WebsiteRoot, target);

        Assert.Equal(DataLocationOutcome.NewEmpty, result.Outcome);
        Assert.Equal([StoreDataLocation.MarkerFileName], Directory.EnumerateFileSystemEntries(target).Select(Path.GetFileName).ToArray());
        Assert.Equal(before, Hashes(WebsiteRoot, withNotes: true));
        Assert.Null(StoreDataLocation.FindTakeover(WebsiteRoot, []));
        Assert.Throws<InvalidOperationException>(() => StoreDataLocation.StartNew(WebsiteRoot, Path.Combine(WebsiteRoot, "inside")));
    }

    [Fact]
    public void NoOtherRecords_NoChoiceNeeded()
    {
        Directory.CreateDirectory(WebsiteRoot);
        Assert.Empty(StoreDataLocation.OtherInstallationDatabases(WebsiteRoot, null));
        Assert.Equal(DataLocationOutcome.NewEmpty, StoreDataLocation.Choose(WebsiteRoot, StoreDataLocation.DefaultFolder(Profile)).Outcome);
        Assert.Null(StoreDataLocation.FindTakeover(WebsiteRoot, []));
    }

    // ---------------------------------------------------------------- the warning text

    private static readonly OtherInstallationFacts Neither = new(false, null, false, null, []);

    [Fact]
    public void EachEdition_WarnsAboutTheOther_NamingBothFolders()
    {
        var both = new OtherInstallationFacts(true, @"C:\Users\Shop\Pharma Remit Data", true, @"C:\Program Files\Pharma Remit\", []);

        string website = OtherInstallationNotice.For(Edition.LocalMarket, both, @"C:\Users\Shop\AppData\Local\PharmaBill")!;
        Assert.Contains("Pharma Remit from the Microsoft Store is also installed", website);
        Assert.Contains(@"C:\Users\Shop\Pharma Remit Data", website);
        Assert.Contains(@"This app (the website version) uses C:\Users\Shop\AppData\Local\PharmaBill", website);

        string store = OtherInstallationNotice.For(Edition.Store, both, @"C:\Users\Shop\Pharma Remit Data")!;
        Assert.Contains(@"The website version of Pharma Remit is also installed on this computer (C:\Program Files\Pharma Remit)", store);
        Assert.Contains(OtherInstallationNotice.WebsiteDataFolder, store);
        Assert.Contains(@"This app (the Microsoft Store version) uses C:\Users\Shop\Pharma Remit Data", store);
    }

    [Fact]
    public void NoWarning_WhenTheOtherEditionIsAbsent()
    {
        Assert.Null(OtherInstallationNotice.For(Edition.LocalMarket, Neither, "x"));
        Assert.Null(OtherInstallationNotice.For(Edition.Store, Neither, "x"));
        // Each looks only for the other: the Store version is not warned about itself, nor the website version about itself.
        Assert.Null(OtherInstallationNotice.For(Edition.Store, Neither with { StorePackageInstalled = true }, "x"));
        Assert.Null(OtherInstallationNotice.For(Edition.LocalMarket, Neither with { WebsiteVersionInstalled = true }, "x"));
    }

    // ---------------------------------------------------------------- the first-run window

    private static T Find<T>(Window window, string name) where T : FrameworkElement =>
        Ui.Descendants(window).OfType<T>().Single(element => AutomationProperties.GetName(element) == name);

    [Theory]
    [InlineData("a")]
    [InlineData("b")]
    [InlineData("c")]
    public void FirstRunWindow_ExplainsWhatWasFound_AndEachChoiceDoesWhatItSays(string choice)
    {
        string password = CreateWebsiteRecords();
        Dictionary<string, byte[]> before = Hashes(WebsiteRoot);
        string target = Path.Combine(Profile, "Pharma Remit Data " + choice);
        Ui.Run(async () =>
        {
            var window = new DataLocationWindow(WebsiteRoot, StoreDataLocation.Check(WebsiteRoot), ownPrivateRoot: null, websiteVersionInstalled: true);
            Ui.Prepare(window);
            window.Show();
            await Ui.SettleAsync();

            Assert.True(window.IsOtherInstallationChoice);
            Assert.Equal("Records from another installation of Pharma Remit were found", Find<TextBlock>(window, "Data folder heading").Text);
            string explanation = Find<TextBlock>(window, "Data folder explanation").Text;
            Assert.Contains(Path.GetFullPath(WebsiteRoot), explanation);
            Assert.Contains("Sameerchemists", explanation);
            Assert.Contains("both the Microsoft Store version and the website version of Pharma Remit installed", explanation);
            string warning = Find<TextBlock>(window, "Two versions warning").Text;
            Assert.Contains("two sets of bills and two stock levels that disagree, with the same invoice numbers in each", warning);
            // Nothing is chosen for the pharmacist.
            Button proceed = Find<Button>(window, "Data folder continue");
            Assert.False(proceed.IsEnabled);
            Assert.All(Ui.Descendants(window).OfType<RadioButton>(), radio => Assert.False(radio.IsChecked));
            if (choice == "a")
            {
                ScreenshotOf(window, "first-run-choice");
            }

            RadioButton option = Find<RadioButton>(window, choice switch { "a" => "Use the existing records", "b" => "Start a new empty pharmacy", _ => "Close to uninstall one version" });
            option.IsChecked = true;
            await Ui.SettleAsync();
            if (choice != "c")
            {
                TextBox folder = Find<TextBox>(window, "Data folder");
                Assert.True(folder.IsVisible);
                Ui.Type(folder, target);
            }
            await Ui.PressAsync(proceed);
            await Ui.SettleAsync();

            switch (choice)
            {
                case "a":
                    Assert.Equal((true, DataLocationOutcome.TakenOver, Path.GetFullPath(target)), (window.Result, window.Outcome, window.DataRoot));
                    Assert.True(File.Exists(Path.Combine(target, @"Sameerchemists\pharmabill.db")));
                    Assert.False(File.Exists(Path.Combine(target, "device.id")));
                    Assert.NotNull(StoreDataLocation.FindTakeover(WebsiteRoot, []));
                    break;
                case "b":
                    Assert.Equal((true, DataLocationOutcome.NewEmpty), (window.Result, window.Outcome));
                    Assert.False(StoreDataLocation.ContainsDatabase(target));
                    Assert.Null(StoreDataLocation.FindTakeover(WebsiteRoot, []));
                    break;
                default:
                    Assert.Equal(false, window.Result);
                    Assert.Equal(DataLocationStatus.NotChosen, StoreDataLocation.Check(WebsiteRoot).Status);
                    Assert.Null(StoreDataLocation.FindTakeover(WebsiteRoot, []));
                    break;
            }
            // In every case the website version's records are exactly as they were.
            Assert.Equal(before, Hashes(WebsiteRoot));
        });
        _ = password;
    }

    // ---------------------------------------------------------------- the dashboard warning

    [Fact]
    public void DashboardWarning_ShowsOnce_IsDismissible_AndIsNeverOnABillingScreen()
    {
        var store = new OneTimeNoticeStore(Path.Combine(_sandbox, "dismissed-notices.json"));
        string warning = OtherInstallationNotice.For(Edition.LocalMarket, new OtherInstallationFacts(true, @"C:\Users\Shop\Pharma Remit Data", false, null, []), @"C:\Users\Shop\AppData\Local\PharmaBill")!;
        Ui.Run(async () =>
        {
            var mainWindow = new PharmaBill.App.MainWindow(null!, null!, null!, null!, new BindingSafetyTests.NullScannerService());
            BindingSafetyTests.Retire(mainWindow);
            var retail = (DataTemplate)mainWindow.Resources[new DataTemplateKey(typeof(DashboardPageViewModel))];

            var vm = (MainWindowViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainWindowViewModel));
            typeof(MainWindowViewModel).GetField("_otherInstallationWarning", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(vm, string.Empty);
            typeof(MainWindowViewModel).GetField("_updateButtonText", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(vm, "Check for Updates");
            vm.ApplyOtherInstallationWarning(warning, store);

            var host = new Window { DataContext = vm, Width = 1366, Height = 768 };
            Ui.Prepare(host);
            host.Show();
            host.Content = new ContentControl { Content = RuntimeHelpers.GetUninitializedObject(typeof(DashboardPageViewModel)), ContentTemplate = retail, Resources = mainWindow.Resources };
            await Ui.SettleAsync();
            Border banner = Find<Border>(host, "Two installations warning");
            Assert.True(banner.IsVisible);
            Assert.Equal(warning, Find<TextBlock>(host, "Two installations warning text").Text);
            ScreenshotOf(host, "dashboard-warning");

            await Ui.PressAsync(Find<Button>(host, "Dismiss two installations warning"));
            Assert.False(banner.IsVisible);

            // Next start: dismissed once, it stays dismissed.
            vm.ApplyOtherInstallationWarning(warning, store);
            await Ui.SettleAsync();
            Assert.False(banner.IsVisible);
            // The Wholesale dashboard carries it too.
            var fresh = new OneTimeNoticeStore(Path.Combine(_sandbox, "fresh.json"));
            vm.ApplyOtherInstallationWarning(warning, fresh);
            host.Content = new PharmaBill.App.Views.WholesaleDashboardView { DataContext = RuntimeHelpers.GetUninitializedObject(typeof(WholesaleDashboardPageViewModel)) };
            await Ui.SettleAsync();
            Assert.True(Find<Border>(host, "Two installations warning").IsVisible);
            // The other edition absent: nothing shown.
            vm.ApplyOtherInstallationWarning(null, fresh);
            await Ui.SettleAsync();
            Assert.False(Find<Border>(host, "Two installations warning").IsVisible);
            host.Close();

        });

        // Never on a billing screen: in the screens' sources it appears only in the two dashboards.
        string app = Path.Combine(RepositoryRoot(), "PharmaBill.App");
        var uses = Directory.EnumerateFiles(app, "*.xaml", SearchOption.AllDirectories)
            .Where(file => !file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar))
            .SelectMany(file => System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(file), "<controls:OtherInstallationBanner").Select(match => (File: Path.GetFileName(file), Text: File.ReadAllText(file), match.Index)))
            .ToList();
        Assert.Equal(["MainWindow.xaml", "WholesaleDashboardView.xaml"], uses.Select(use => use.File).Order().ToArray());
        var (_, mainWindowXaml, at) = uses.Single(use => use.File == "MainWindow.xaml");
        int template = mainWindowXaml.LastIndexOf("<DataTemplate ", at, StringComparison.Ordinal);
        Assert.Contains("vm:DashboardPageViewModel", mainWindowXaml[template..mainWindowXaml.IndexOf('>', template)]);
    }

    private static string RepositoryRoot()
    {
        string? directory = AppContext.BaseDirectory;
        while (directory != null && !File.Exists(Path.Combine(directory, "PharmaBill.slnx")))
        {
            directory = Path.GetDirectoryName(directory);
        }
        return directory ?? throw new DirectoryNotFoundException("Repository root not found.");
    }

    private static IEnumerable<object> LogicalDescendants(DependencyObject root)
    {
        foreach (object child in LogicalTreeHelper.GetChildren(root))
        {
            yield return child;
            if (child is DependencyObject node)
            {
                foreach (object deeper in LogicalDescendants(node))
                {
                    yield return deeper;
                }
            }
        }
    }

    private void ScreenshotOf(Window window, string name)
    {
        string? folder = Environment.GetEnvironmentVariable("PHARMAREMIT_SCREENSHOTS");
        if (string.IsNullOrEmpty(folder))
        {
            return;
        }
        window.UpdateLayout();
        var element = (FrameworkElement)window.Content;
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)element.ActualWidth, (int)element.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        var white = new System.Windows.Media.DrawingVisual();
        using (System.Windows.Media.DrawingContext dc = white.RenderOpen())
        {
            dc.DrawRectangle(System.Windows.Media.Brushes.White, null, new Rect(0, 0, element.ActualWidth, element.ActualHeight));
        }
        bitmap.Render(white);
        bitmap.Render(element);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(folder);
        using FileStream stream = File.Create(Path.Combine(folder, name + ".png"));
        encoder.Save(stream);
        output.WriteLine("screenshot " + name);
    }
}
