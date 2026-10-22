// Pharma Remit - Store data-folder migration check.
// Run inside the TEST Windows account, with Pharma Remit closed:
//   PharmaRemitMigrationCheck.exe before           (live 1.0.5.0 shop set up, before upgrading)
//   PharmaRemitMigrationCheck.exe after-upgrade    (after installing 1.0.11 and choosing the folder)
//   PharmaRemitMigrationCheck.exe after-reinstall  (after uninstall + reinstall + choosing the same folder)
// It only ever reads copies of the database, never the live files, and refuses to run against anything that
// is not this account's "Migration Test Pharmacy".
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;

const string ExpectedShop = "Migration Test Pharmacy";
const string PackageFamily = "S.A.E.R.PharmaBill_jprxtand66ex6";
const string DevAccount = "newth";
string[] businessTables = ["PharmacyProfiles", "AppUsers", "Drugs", "Batches", "StockMovements", "Suppliers", "PurchaseInvoices",
    "PurchaseItems", "Patients", "Prescriptions", "Sales", "SaleItems", "Receipts", "ScheduleRegisterEntries"];
string[] stages = ["before", "after-upgrade", "after-reinstall"];

string stage = args.FirstOrDefault()?.Trim().ToLowerInvariant() ?? "";
if (!stages.Contains(stage))
{
    Console.WriteLine("Usage: PharmaRemitMigrationCheck.exe before | after-upgrade | after-reinstall");
    return 64;
}

#if SELFTEST
// Self-test build only (never shipped): a fake profile in a scratch folder.
string profile = Environment.GetEnvironmentVariable("MIGRATIONCHECK_SELFTEST_PROFILE") ?? throw new InvalidOperationException("Set MIGRATIONCHECK_SELFTEST_PROFILE.");
string localAppData = Path.Combine(profile, "AppData", "Local");
#else
string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
#endif
string packageRoot = Path.Combine(localAppData, "Packages", PackageFamily, "LocalCache", "Local", "PharmaBill");
string realRoot = Path.Combine(localAppData, "PharmaBill");
string defaultChosen = Path.Combine(profile, "Pharma Remit Data");
string resultsDir = Path.Combine(AppContext.BaseDirectory, "results", Environment.UserName);
var json = new JsonSerializerOptions { WriteIndented = true };
var checks = new List<(bool? Ok, string Name, string Detail)>();
SQLitePCL.Batteries_V2.Init();

// ---- Guards: never this PC's own account, never a running app, never someone else's data. ----
#if !SELFTEST
if (string.Equals(Environment.UserName, DevAccount, StringComparison.OrdinalIgnoreCase)
    || profile.TrimEnd('\\').EndsWith("\\" + DevAccount, StringComparison.OrdinalIgnoreCase))
{
    return Stop($"You are signed in to Windows as '{Environment.UserName}', the development account that holds the real data. Sign in to the TEST account and run this there.");
}
#endif
if (Process.GetProcessesByName("PharmaBill.App").Length > 0)
{
    return Stop("Pharma Remit is still running. Close it completely (check the taskbar and system tray), then run this again.");
}
Directory.CreateDirectory(resultsDir);
Console.WriteLine($"Windows account: {Environment.UserName}   Stage: {stage}   {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
Console.WriteLine($"Results folder:  {resultsDir}");
Console.WriteLine();

return stage switch
{
    "before" => Before(),
    "after-upgrade" => AfterUpgrade(),
    _ => AfterReinstall()
};

int Before()
{
    string[] withData = new[] { packageRoot, realRoot }.Where(root => FindDatabases(root).Count > 0).ToArray();
    if (withData.Length == 0)
    {
        return Stop($"No Pharma Remit database found for this account.\nLooked in:\n  {packageRoot}\n  {realRoot}\nSet up the shop in the live Store app, close it, then run 'before' again.");
    }
    if (withData.Length > 1)
    {
        return Stop($"Both of these hold a Pharma Remit database:\n  {packageRoot}\n  {realRoot}\nThis account is not clean. Use a brand-new Windows account.");
    }
    string root = withData[0];
    if (Directory.Exists(defaultChosen))
    {
        return Stop($"{defaultChosen} already exists, so this account is not clean (or 'before' is being run after the upgrade).");
    }
    if (FindPointer() != null)
    {
        return Stop("A data-location pointer already exists: the new version has already run. 'before' must be taken before upgrading.");
    }

    Snapshot snap = TakeSnapshot(root);
    Check(true, "Live shop found", $"{snap.Shop} in {root}");
    Check(snap.DatabaseFiles.Length == 1, "Exactly one database", string.Join(", ", snap.DatabaseFiles));
    Check(snap.Counts.GetValueOrDefault("Sales") >= 4, "At least 4 bills", $"{snap.Counts.GetValueOrDefault("Sales")} bills: {string.Join(", ", snap.Invoices)}");
    Check(snap.Stock.Length >= 2, "At least 2 stock batches", string.Join("  ", snap.Stock));
    Check(snap.Register.Length >= 1, "At least 1 register entry", string.Join("\n      ", snap.Register));
    Check(snap.Attachments.Any(a => a.EndsWith("|exists", StringComparison.Ordinal)), "Prescription attachment on disk", string.Join("\n      ", snap.Attachments));
    Check(null, "Database migrations", $"{snap.Migrations.Length}, last {snap.Migrations.LastOrDefault()}");
    Check(null, "Files recorded for the 'original intact' check", $"{snap.Files.Count} files under {root}");
    Save("before", snap);
    return Finish("Before-upgrade state recorded. Compare the bills, stock and register lines above with what you noted down.");
}

int AfterUpgrade()
{
    Snapshot before = Load("before");
    string? pointerRoot = FindPointer();
    if (pointerRoot == null)
    {
        Check(false, "Data-location pointer", "Not found. The new version never moved the data (did the folder window appear and was it confirmed?).");
        return Finish(null);
    }
    string chosen = ReadPointer(pointerRoot);
    Check(true, "Data-location pointer", $"{Path.Combine(pointerRoot, "data-location.json")} -> {chosen}");
    if (!IsInside(chosen, profile))
    {
        return Stop($"The chosen data folder {chosen} is outside this account's profile ({profile}). Not reading it.");
    }
    Check(!IsInside(chosen, Path.Combine(localAppData, "Packages")), "Chosen folder is outside the app package (survives uninstall)", chosen);
    Check(string.Equals(Full(chosen), Full(defaultChosen), StringComparison.OrdinalIgnoreCase) ? true : null,
        "Chosen folder is the suggested default", string.Equals(Full(chosen), Full(defaultChosen), StringComparison.OrdinalIgnoreCase) ? chosen : $"{chosen} (you picked a different folder; fine if intended)");
    Check(File.Exists(Path.Combine(chosen, "Pharma Remit data.json")), "Marker file in the chosen folder", "Pharma Remit data.json");

    Check(NoSuspiciousFiles(before.Root, chosen, out string suspicious), "No database was archived, quarantined or replaced", suspicious);
    Snapshot moved = TakeSnapshot(chosen);
    Check(moved.DatabaseFiles.Length == 1, "Exactly one database in the chosen folder", string.Join(", ", moved.DatabaseFiles));
    CompareRecords(before, moved, "chosen folder vs before");
    CheckAttachmentsInside(moved, chosen, before.Attachments.Length);

    // The original: every file that was there before is byte-for-byte the same.
    Dictionary<string, string> nowFiles = Inventory(before.Root);
    string[] changed = before.Files.Where(f => !IsLocalOnly(f.Key) && (!nowFiles.TryGetValue(f.Key, out string? h) || h != f.Value)).Select(f => f.Key).ToArray();
    string[] added = nowFiles.Keys.Except(before.Files.Keys, StringComparer.OrdinalIgnoreCase).ToArray();
    string[] unexpectedAdded = added.Where(f => !IsLocalOnly(f) && !f.Equals("data-location.json", StringComparison.OrdinalIgnoreCase)
        && !f.StartsWith("DATA MOVED", StringComparison.OrdinalIgnoreCase)).ToArray();
    string originalDb = before.DatabaseFiles[0];
    Check(!changed.Any(f => f.StartsWith(originalDb, StringComparison.OrdinalIgnoreCase)), "Original database file unchanged (the new version did not open or migrate it)", originalDb);
    Check(changed.Length == 0, "Original folder intact", changed.Length == 0 ? $"all {before.Files.Count(f => !IsLocalOnly(f.Key))} files identical" : "CHANGED OR MISSING: " + string.Join(", ", changed));
    Check(unexpectedAdded.Length == 0, "Nothing unexpected added to the original folder",
        unexpectedAdded.Length == 0 ? "added only: " + (added.Length == 0 ? "(nothing)" : string.Join(", ", added)) : "UNEXPECTED: " + string.Join(", ", unexpectedAdded));
    if (File.Exists(Path.Combine(before.Root, originalDb)) && File.Exists(Path.Combine(before.Root, "data", "database.key")))
    {
        Snapshot original = TakeSnapshot(before.Root, inventory: false);
        Check(original.Invoices.SequenceEqual(before.Invoices) && original.Register.SequenceEqual(before.Register), "Original database still opens with every record", $"{original.Invoices.Length} bills, {original.Register.Length} register entries");
    }
    else
    {
        Check(false, "Original database still opens with every record", "MISSING: " + Path.Combine(before.Root, originalDb));
    }

    ShowLogLines(before);
    Save("after-upgrade", moved);
    return Finish("Upgrade checks complete.");
}

int AfterReinstall()
{
    Snapshot before = Load("before");
    Snapshot upgraded = Load("after-upgrade");
    Check(null, "Old package folder", Directory.Exists(before.Root) ? $"{before.Root} still exists" : $"{before.Root} removed by the uninstall (expected)");
    Check(FindDatabases(packageRoot).Count == 0 && FindDatabases(realRoot).Count == 0, "Reinstalled app did not start a new, empty shop",
        "no database in " + packageRoot + " or " + realRoot);
    string? pointerRoot = FindPointer();
    if (pointerRoot == null)
    {
        Check(false, "Data-location pointer", "Not found. After reinstalling, the app should have asked for the data folder and been pointed at the same one.");
        return Finish(null);
    }
    string chosen = ReadPointer(pointerRoot);
    Check(string.Equals(Full(chosen), Full(upgraded.Root), StringComparison.OrdinalIgnoreCase), "Reinstall points at the same data folder", $"{chosen} (was {upgraded.Root})");
    if (!IsInside(chosen, profile))
    {
        return Stop($"The data folder {chosen} is outside this account's profile ({profile}). Not reading it.");
    }
    Check(NoSuspiciousFiles(chosen, chosen, out string suspicious), "No database was archived, quarantined or replaced", suspicious);
    Snapshot again = TakeSnapshot(chosen);
    Check(again.DatabaseFiles.Length == 1, "Exactly one database in the data folder", string.Join(", ", again.DatabaseFiles));
    CompareRecords(before, again, "after reinstall vs before");
    CheckAttachmentsInside(again, chosen, before.Attachments.Length);
    string[] gone = upgraded.Files.Keys.Where(f => !IsLocalOnly(f) && !f.Contains("pharmabill.db", StringComparison.OrdinalIgnoreCase) && !again.Files.ContainsKey(f)).ToArray();
    Check(gone.Length == 0, "Nothing deleted from the data folder", gone.Length == 0 ? $"{again.Files.Count} files present" : "MISSING: " + string.Join(", ", gone));
    Save("after-reinstall", again);
    return Finish("Reinstall checks complete.");
}

// ---------------- helpers ----------------

void CompareRecords(Snapshot expected, Snapshot actual, string label)
{
    Check(actual.Shop == expected.Shop, $"Shop name ({label})", actual.Shop);
    foreach (string table in businessTables.Where(t => expected.Counts.ContainsKey(t)))
    {
        long want = expected.Counts[table];
        long got = actual.Counts.GetValueOrDefault(table, -1);
        Check(got == want, $"{table} count", got == want ? $"{got}" : $"expected {want}, found {got}");
    }
    Check(actual.Invoices.SequenceEqual(expected.Invoices), "Every invoice number", Diff(expected.Invoices, actual.Invoices));
    Check(actual.SalesTotal == expected.SalesTotal, "Sales total", actual.SalesTotal == expected.SalesTotal ? actual.SalesTotal : $"expected {expected.SalesTotal}, found {actual.SalesTotal}");
    Check(actual.Stock.SequenceEqual(expected.Stock), "Stock on hand per batch", Diff(expected.Stock, actual.Stock));
    Check(actual.Register.SequenceEqual(expected.Register), "Schedule register entries", Diff(expected.Register, actual.Register));
    Check(expected.Migrations.All(m => actual.Migrations.Contains(m)), "Database history kept (no fresh database)",
        $"{actual.Migrations.Length} migrations, last {actual.Migrations.LastOrDefault()}");
}

void CheckAttachmentsInside(Snapshot snap, string root, int expectedCount)
{
    Check(snap.Attachments.Length == expectedCount, "Prescription/document links", $"{snap.Attachments.Length} (expected {expectedCount})");
    foreach (string attachment in snap.Attachments)
    {
        string path = attachment[..attachment.LastIndexOf('|')];
        bool exists = attachment.EndsWith("|exists", StringComparison.Ordinal);
        Check(exists && IsInside(path, root), "Attachment opens from the data folder", path);
    }
}

Snapshot TakeSnapshot(string root, bool inventory = true)
{
    if (!IsInside(root, profile))
    {
        Environment.Exit(Stop($"{root} is outside this account's profile ({profile}). Not reading it."));
    }
    List<string> databases = FindDatabases(root);
    if (databases.Count == 0)
    {
        Environment.Exit(Stop($"No database (pharmabill.db) found under {root}."));
    }
    string keyPath = Path.Combine(root, "data", "database.key");
    if (!File.Exists(keyPath))
    {
        Environment.Exit(Stop($"No database key at {keyPath}."));
    }
    byte[] key;
    try
    {
        key = ProtectedData.Unprotect(File.ReadAllBytes(keyPath), null, DataProtectionScope.CurrentUser);
    }
    catch (CryptographicException)
    {
        Environment.Exit(Stop($"The key at {keyPath} cannot be decrypted by Windows account '{Environment.UserName}'. This is not this account's data - stopping without reading anything."));
        throw;
    }

    // Read a copy: the live files are never opened, so a check can never change them.
    string db = databases[0];
    string temp = Path.Combine(Path.GetTempPath(), "PharmaRemitMigrationCheck-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(temp);
    try
    {
        foreach (string suffix in new[] { "", "-wal", "-shm" })
        {
            if (File.Exists(db + suffix))
            {
                File.Copy(db + suffix, Path.Combine(temp, "copy.db" + suffix));
            }
        }
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(temp, "copy.db"),
            Mode = SqliteOpenMode.ReadOnly,
            Password = Convert.ToHexString(key),
            Pooling = false
        }.ToString());
        connection.Open();
        string shop = Scalar(connection, "SELECT Name FROM PharmacyProfiles LIMIT 1") ?? "(none)";
        if (shop != ExpectedShop)
        {
            Environment.Exit(Stop($"The database in {root} belongs to '{shop}', not the test shop '{ExpectedShop}'. Stopping without reading anything else."));
        }
        var counts = new Dictionary<string, long>();
        foreach (string table in businessTables.Where(t => TableExists(connection, t)))
        {
            counts[table] = long.Parse(Scalar(connection, $"SELECT count(*) FROM \"{table}\"") ?? "0");
        }
        var attachments = new List<string>();
        void AddPaths(string sql)
        {
            foreach (string p in Column(connection, sql).Where(p => !string.IsNullOrWhiteSpace(p)))
            {
                // The packaged app sees %LocalAppData%\PharmaBill; Windows keeps those files in the package's
                // LocalCache. A path the app stored there exists at the redirected location.
                bool exists = File.Exists(p)
                    || (IsInside(p, realRoot) && File.Exists(Path.Combine(packageRoot, Path.GetRelativePath(realRoot, p))));
                attachments.Add(p + (exists ? "|exists" : "|MISSING"));
            }
        }
        if (ColumnExists(connection, "Sales", "PrescriptionFilePath")) AddPaths("SELECT PrescriptionFilePath FROM Sales ORDER BY InvoiceNo");
        if (TableExists(connection, "Prescriptions")) AddPaths("SELECT substr(Notes, 14) FROM Prescriptions WHERE Notes LIKE 'DocumentPath=%' ORDER BY Id");
        if (ColumnExists(connection, "CustomerLicences", "DocumentPath")) AddPaths("SELECT DocumentPath FROM CustomerLicences ORDER BY Id");
        if (ColumnExists(connection, "PurchaseInvoices", "AttachedInvoicePath")) AddPaths("SELECT AttachedInvoicePath FROM PurchaseInvoices ORDER BY Id");

        return new Snapshot(
            Full(root),
            shop,
            Column(connection, "SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId").ToArray(),
            counts,
            Column(connection, "SELECT InvoiceNo FROM Sales ORDER BY InvoiceNo").ToArray(),
            Scalar(connection, "SELECT printf('%.2f', coalesce(sum(TotalAmount), 0)) FROM Sales") ?? "0.00",
            Column(connection, "SELECT b.BatchNo || '=' || printf('%.2f', coalesce(sum(m.QuantityChange), 0)) FROM Batches b LEFT JOIN StockMovements m ON m.BatchId = b.Id GROUP BY b.Id ORDER BY b.BatchNo").ToArray(),
            Column(connection, "SELECT RegisterType || ' | batch ' || coalesce(BatchNo, '') || ' | qty ' || printf('%.2f', coalesce(Quantity, 0)) || ' | ' || coalesce(PatientName, '') || ' | ' || coalesce(PrescriberName, '') || ' ' || coalesce(PrescriberRegistrationNumber, '') || ' | ' || EntryAtUtc FROM ScheduleRegisterEntries ORDER BY EntryAtUtc, Id").ToArray(),
            attachments.ToArray(),
            databases.Select(d => Path.GetRelativePath(root, d)).ToArray(),
            DateTime.UtcNow,
            inventory ? Inventory(root) : new Dictionary<string, string>());
    }
    finally
    {
        SqliteConnection.ClearAllPools();
        CryptographicOperations.ZeroMemory(key);
        try { Directory.Delete(temp, recursive: true); } catch { }
    }
}

List<string> FindDatabases(string root)
{
    if (!Directory.Exists(root)) return [];
    return Directory.EnumerateFiles(root, "pharmabill.db", SearchOption.AllDirectories)
        .Where(p => !p.Contains("\\archives\\", StringComparison.OrdinalIgnoreCase) && !p.Contains("\\backups\\", StringComparison.OrdinalIgnoreCase))
        .OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
}

// Evidence that the app found a database empty or unreadable at some point: it renames it aside.
bool NoSuspiciousFiles(string first, string second, out string detail)
{
    string[] found = new[] { first, second, packageRoot, realRoot }.Distinct(StringComparer.OrdinalIgnoreCase).Where(Directory.Exists)
        .SelectMany(root => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        .Where(p =>
        {
            string name = Path.GetFileName(p);
            return name.Contains(".empty-", StringComparison.OrdinalIgnoreCase) || name.Contains(".key-mismatch-", StringComparison.OrdinalIgnoreCase)
                || name.Contains(".unreadable-", StringComparison.OrdinalIgnoreCase);
        }).ToArray();
    detail = found.Length == 0 ? "none found" : "FOUND: " + string.Join(", ", found);
    return found.Length == 0;
}

string? FindPointer()
{
    string[] found = new[] { packageRoot, realRoot }.Where(r => File.Exists(Path.Combine(r, "data-location.json"))).ToArray();
    if (found.Length > 1)
    {
        Environment.Exit(Stop($"A data-location pointer exists in both {packageRoot} and {realRoot}. This account is not clean."));
    }
    return found.FirstOrDefault();
}

string ReadPointer(string root)
{
    using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "data-location.json")));
    return doc.RootElement.GetProperty("DataRoot").GetString() ?? "";
}

void ShowLogLines(Snapshot before)
{
    string[] words = ["quarantin", "archived", "unreadable", "empty database", "key mismatch", "key-mismatch", "fresh", "seeded", "template"];
    var hits = new List<string>();
    foreach (string root in new[] { packageRoot, realRoot }.Where(Directory.Exists))
    {
        foreach (string log in Directory.EnumerateFiles(root, "*.log", SearchOption.AllDirectories).Where(f => File.GetLastWriteTimeUtc(f) >= before.TakenAtUtc))
        {
            try
            {
                hits.AddRange(File.ReadLines(log).Where(l => words.Any(w => l.Contains(w, StringComparison.OrdinalIgnoreCase))).Select(l => Path.GetFileName(log) + ": " + l));
            }
            catch (IOException) { }
        }
    }
    Check(null, "Log lines since 'before' that mention empty/unreadable/seeded databases", hits.Count == 0 ? "none" : string.Join("\n      ", hits.Take(20)));
}

Dictionary<string, string> Inventory(string root)
{
    if (!Directory.Exists(root)) return new();
    return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).ToDictionary(
        f => Path.GetRelativePath(root, f),
        f => { using FileStream s = File.OpenRead(f); return Convert.ToHexString(SHA256.HashData(s)); },
        StringComparer.OrdinalIgnoreCase);
}

static bool IsLocalOnly(string relative)
{
    string p = relative.Replace('/', '\\');
    return p.StartsWith("data\\logs\\", StringComparison.OrdinalIgnoreCase) || p.StartsWith("logs\\", StringComparison.OrdinalIgnoreCase)
        || p.Equals("data\\license.lic", StringComparison.OrdinalIgnoreCase) || p.Equals("data\\license.state", StringComparison.OrdinalIgnoreCase)
        || p.Equals("data\\trial.anchor", StringComparison.OrdinalIgnoreCase);
}

static string Full(string path) => Path.GetFullPath(path).TrimEnd('\\');

static bool IsInside(string path, string root) =>
    Full(path).Equals(Full(root), StringComparison.OrdinalIgnoreCase) || Full(path).StartsWith(Full(root) + "\\", StringComparison.OrdinalIgnoreCase);

static string Diff(string[] expected, string[] actual)
{
    if (expected.SequenceEqual(actual)) return actual.Length == 0 ? "(none)" : string.Join("\n      ", actual);
    return "EXPECTED:\n        " + string.Join("\n        ", expected) + "\n      FOUND:\n        " + string.Join("\n        ", actual);
}

static string? Scalar(SqliteConnection c, string sql) { using var cmd = c.CreateCommand(); cmd.CommandText = sql; return Convert.ToString(cmd.ExecuteScalar()); }

static List<string> Column(SqliteConnection c, string sql)
{
    var list = new List<string>();
    using var cmd = c.CreateCommand();
    cmd.CommandText = sql;
    using var r = cmd.ExecuteReader();
    while (r.Read()) list.Add(r.IsDBNull(0) ? "" : Convert.ToString(r.GetValue(0)) ?? "");
    return list;
}

static bool TableExists(SqliteConnection c, string table) =>
    Scalar(c, $"SELECT count(*) FROM sqlite_master WHERE type = 'table' AND name = '{table}'") == "1";

static bool ColumnExists(SqliteConnection c, string table, string column) =>
    TableExists(c, table) && Column(c, $"SELECT name FROM pragma_table_info('{table}')").Contains(column);

void Save(string name, Snapshot snap) => File.WriteAllText(Path.Combine(resultsDir, name + ".json"), JsonSerializer.Serialize(snap, json));

Snapshot Load(string name)
{
    string path = Path.Combine(resultsDir, name + ".json");
    if (!File.Exists(path))
    {
        Environment.Exit(Stop($"{path} not found. Run the '{name}' step first, in this same Windows account."));
    }
    return JsonSerializer.Deserialize<Snapshot>(File.ReadAllText(path))!;
}

void Check(bool? ok, string name, string detail)
{
    checks.Add((ok, name, detail));
    Console.ForegroundColor = ok switch { true => ConsoleColor.Green, false => ConsoleColor.Red, null => ConsoleColor.Gray };
    Console.Write(ok switch { true => "[PASS] ", false => "[FAIL] ", null => "[INFO] " });
    Console.ResetColor();
    Console.WriteLine($"{name}: {detail}");
}

int Finish(string? message)
{
    int failed = checks.Count(c => c.Ok == false);
    File.WriteAllLines(Path.Combine(resultsDir, stage + "-report.txt"),
        checks.Select(c => $"{(c.Ok switch { true => "PASS", false => "FAIL", null => "INFO" })}  {c.Name}: {c.Detail}"));
    Console.WriteLine();
    Console.ForegroundColor = failed == 0 ? ConsoleColor.Green : ConsoleColor.Red;
    Console.WriteLine(failed == 0 ? $"ALL CHECKS PASSED. {message}" : $"{failed} CHECK(S) FAILED. Stop here and send the results folder: {resultsDir}");
    Console.ResetColor();
    return failed == 0 ? 0 : 1;
}

int Stop(string message)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine("==================== STOP ====================");
    Console.WriteLine(message);
    if (checks.Count == 0)
    {
        Console.WriteLine("Nothing was checked. Nothing was changed.");
    }
    else
    {
        Console.WriteLine("Stopped part-way: this counts as a FAIL. Nothing was changed. Checks so far are saved in " + Path.Combine(resultsDir, stage + "-report.txt"));
        File.WriteAllLines(Path.Combine(resultsDir, stage + "-report.txt"),
            checks.Select(c => $"{(c.Ok switch { true => "PASS", false => "FAIL", null => "INFO" })}  {c.Name}: {c.Detail}").Append("STOP  " + message));
    }
    Console.WriteLine("==============================================");
    Console.ResetColor();
    return 2;
}

sealed record Snapshot(string Root, string Shop, string[] Migrations, Dictionary<string, long> Counts, string[] Invoices, string SalesTotal,
    string[] Stock, string[] Register, string[] Attachments, string[] DatabaseFiles, DateTime TakenAtUtc, Dictionary<string, string> Files);
