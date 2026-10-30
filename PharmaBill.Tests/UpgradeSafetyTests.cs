using System.Globalization;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PharmaBill.Data.Persistence;
using PharmaBill.Data.Services;
using Xunit;
using Xunit.Abstractions;

namespace PharmaBill.Tests;

// An upgrade never loses a pharmacy's records. A database left by each released version is filled with rows in every
// table, upgraded to the current schema, and every row is still there with every value it had. Before the schema
// changes a complete copy is made beside the database; if an upgrade step fails, that copy is put back.
[Collection(RunsAlone.Name)]
public sealed class UpgradeSafetyTests(ITestOutputHelper output) : IDisposable
{
    // The last migration each released build shipped with (from the release commits), oldest first.
    public static TheoryData<string, string> ReleasedSchemas => new()
    {
        { "20261003165000_WholesaleInvoices", "oldest build in the repository (9116630, 2026-10-04)" },
        { "20261008130000_BranchIdColumns", "first stable release installer (90a8567)" },
        { "20261015134518_UnregisteredBuyerPlaceOfSupply", "Store 1.0.3" },
        { "20261015180314_ProductNilRated", "Local Market 1.0.6 / Store 1.0.7" },
        { "20261021095025_PharmacyLogo", "Local Market 1.0.9 / Store 1.0.10" },
        { "20261022135624_SupplierPaymentAllocations", "Local Market 1.0.10 / Store 1.0.11 to 1.0.14" },
        { "20261029040957_BatchNoExpiryReason", "Store 1.0.15" },
    };

    private const string LocalMarket1010 = "20261022135624_SupplierPaymentAllocations";

    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"PharmaBill.Upgrade.{Guid.NewGuid():N}");

    private readonly string _key = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private string DbPath => Path.Combine(_directory, "data", "pharmabill.db");

    private string ConnectionString(string? path = null) =>
        new SqliteConnectionStringBuilder { DataSource = path ?? DbPath, Pooling = false, Password = _key }.ToString();

    [Theory]
    [MemberData(nameof(ReleasedSchemas))]
    public async Task EveryReleasedSchema_UpgradesToTheCurrentOne_KeepingEveryRowAndValue(string releasedMigration, string release)
    {
        await CreateDatabaseAtAsync(releasedMigration);
        int seeded = SeedEveryTable(rowsPerTable: 3);
        Dictionary<string, List<string>> before = Fingerprint();

        await using (PharmaBillDbContext context = CreateContext())
        {
            await CreateInitializer(context).InitializeAsync();
            Assert.Empty(await context.Database.GetPendingMigrationsAsync());
        }

        Dictionary<string, List<string>> after = Fingerprint(before);
        foreach ((string table, List<string> rows) in before)
        {
            Assert.True(after.ContainsKey(table), $"{release}: table {table} is gone after the upgrade.");
            List<string> remaining = [.. after[table]];
            foreach (string row in rows)
            {
                Assert.True(remaining.Remove(row), $"{release}: a row of {table} changed or is missing after the upgrade: {row}");
            }
        }

        string copy = Assert.Single(PreMigrationCopy.CopiesIn(Path.GetDirectoryName(DbPath)!));
        Assert.Equal(before.Keys.Order(), Fingerprint(null, copy).Keys.Order());
        output.WriteLine($"{release}: {before.Count} tables, {seeded} rows kept; copy {Path.GetFileName(copy)}");
    }

    [Fact]
    public async Task UpgradeFrom1010_MakesANamedCopyFirst_AndTheCopyHoldsTheOldDatabase()
    {
        await CreateDatabaseAtAsync(LocalMarket1010);
        SeedEveryTable(rowsPerTable: 2);
        Dictionary<string, List<string>> before = Fingerprint();

        await using (PharmaBillDbContext context = CreateContext())
        {
            await CreateInitializer(context).InitializeAsync();
        }

        string copy = Assert.Single(PreMigrationCopy.CopiesIn(Path.GetDirectoryName(DbPath)!));
        Assert.Equal($"PharmaBill_premigration_{DateTime.Now:yyyy-MM-dd}_1.0.10.db", Path.GetFileName(copy));
        Assert.Equal(Path.GetDirectoryName(DbPath), Path.GetDirectoryName(copy));
        // The copy is the database as it was: same history, same rows, and it opens on its own (no -wal beside it).
        Assert.Equal(LocalMarket1010, LastMigration(copy));
        Assert.Equal(before, Fingerprint(null, copy));
        Assert.False(File.Exists(copy + "-wal"));
        Assert.Equal(DbInitializer.LastPreMigrationCopy, copy);
    }

    [Fact]
    public async Task NothingPending_MakesNoCopy()
    {
        await using (PharmaBillDbContext context = CreateContext())
        {
            await CreateInitializer(context).InitializeAsync();
        }
        await using (PharmaBillDbContext context = CreateContext())
        {
            await CreateInitializer(context).InitializeAsync();
        }

        // A brand-new database has nothing to copy, and an up-to-date one has nothing to upgrade.
        Assert.Empty(PreMigrationCopy.CopiesIn(Path.GetDirectoryName(DbPath)!));
    }

    [Fact]
    public async Task FailedUpgrade_PutsTheDatabaseBackAsItWas_AndStops()
    {
        await CreateDatabaseAtAsync(LocalMarket1010);
        SeedEveryTable(rowsPerTable: 2);
        // The last migration adds WholesaleReturnItems.BatchExpiry; with that column already there it fails, after the
        // migrations before it have committed. The upgrade must not leave the database half-way.
        Execute("ALTER TABLE WholesaleReturnItems ADD COLUMN BatchExpiry TEXT NULL;");
        Dictionary<string, List<string>> before = Fingerprint();

        DatabaseMigrationException error;
        await using (PharmaBillDbContext context = CreateContext())
        {
            error = await Assert.ThrowsAsync<DatabaseMigrationException>(() => CreateInitializer(context).InitializeAsync());
        }

        Assert.True(error.Restored);
        Assert.Equal("20261029155419_RecordCreditDebitNoteValues", error.MigrationId);
        string copy = Assert.Single(PreMigrationCopy.CopiesIn(Path.GetDirectoryName(DbPath)!));
        Assert.Equal(copy, error.SafeCopyPath);
        // Back exactly as before the upgrade: the 1.0.10 history and every row.
        Assert.Equal(LocalMarket1010, LastMigration(DbPath));
        Assert.Equal(before, Fingerprint());
        // The half-upgraded file is kept aside for support, never deleted; no empty database was made.
        string failed = Assert.Single(Directory.GetFiles(Path.GetDirectoryName(DbPath)!, "pharmabill.db.failed-upgrade-*").Where(path => !path.EndsWith("-wal") && !path.EndsWith("-shm")));
        Assert.NotEqual(LocalMarket1010, LastMigration(failed));
        Assert.Contains("Your data is safe", error.PharmacistMessage);
        Assert.Contains(copy, error.PharmacistMessage);
        Assert.Contains("contact Pharma Remit support", error.PharmacistMessage);

        // The next start tries again from the same 1.0.10 database: still refused, still intact.
        await using (PharmaBillDbContext context = CreateContext())
        {
            await Assert.ThrowsAsync<DatabaseMigrationException>(() => CreateInitializer(context).InitializeAsync());
        }
        Assert.Equal(before, Fingerprint());
    }

    [Fact]
    public async Task OnlyTheLastThreeCopiesAreKept()
    {
        await CreateDatabaseAtAsync(LocalMarket1010);
        DateTime start = new(2026, 10, 1, 9, 0, 0);
        var made = new List<string>();
        for (int i = 0; i < 5; i++)
        {
            made.Add(PreMigrationCopy.Create(ConnectionString(), LocalMarket1010, start.AddDays(i)));
        }

        IReadOnlyList<string> kept = PreMigrationCopy.CopiesIn(Path.GetDirectoryName(DbPath)!);
        Assert.Equal(made.TakeLast(3).Reverse(), kept);
        Assert.Equal("PharmaBill_premigration_2026-10-05_1.0.10.db", Path.GetFileName(kept[0]));
        Assert.True(File.Exists(DbPath));
    }

    [Fact]
    public void Migrations_OnlyAddToTheSchema()
    {
        // No migration's Up drops or renames a table or column, or deletes rows. (Indexes may be replaced.)
        string folder = Path.Combine(RepositoryRoot(), "PharmaBill.Data", "Migrations");
        string[] forbidden = ["DropTable(", "DropColumn(", "RenameTable(", "RenameColumn(", "DELETE FROM", "DROP TABLE", "EnsureDeleted"];
        var found = new List<string>();
        foreach (string file in Directory.GetFiles(folder, "*.cs").Where(path => !path.EndsWith(".Designer.cs") && !path.EndsWith("ModelSnapshot.cs")))
        {
            string text = File.ReadAllText(file);
            int up = text.IndexOf("void Up(", StringComparison.Ordinal);
            int down = text.IndexOf("void Down(", StringComparison.Ordinal);
            string upBody = text[up..(down > up ? down : text.Length)];
            found.AddRange(forbidden.Where(word => upBody.Contains(word, StringComparison.OrdinalIgnoreCase)).Select(word => Path.GetFileName(file) + ": " + word));
        }
        Assert.Empty(found);
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

    private async Task CreateDatabaseAtAsync(string migration)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DbPath)!);
        // As that version's first start did: an empty encrypted database file, then its migrations.
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder(ConnectionString()) { Mode = SqliteOpenMode.ReadWriteCreate }.ToString()))
        {
            connection.Open();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE __created (x INTEGER); DROP TABLE __created; " + SqliteWal.Pragmas;
            command.ExecuteNonQuery();
        }
        await using PharmaBillDbContext context = CreateContext();
        await context.GetService<IMigrator>().MigrateAsync(migration);
        Assert.Equal(migration, (await context.Database.GetAppliedMigrationsAsync()).Last());
    }

    private PharmaBillDbContext CreateContext()
    {
        var deviceId = new DatabaseDeviceId(Guid.NewGuid());
        string connectionString = ConnectionString();
        var options = new DbContextOptionsBuilder<PharmaBillDbContext>()
            .UseSqlite(connectionString)
            .AddInterceptors(new SaveChangesAuditInterceptor(deviceId), SqliteWalInterceptor.Instance)
            .Options;
        return new PharmaBillDbContext(options, deviceId);
    }

    private DbInitializer CreateInitializer(PharmaBillDbContext context)
    {
        var storage = new DatabaseStorageOptions(_directory);
        return new DbInitializer(context, new StorageLocationService(context), new BranchService(new UnitOfWork(context), new BranchSettingsStore(storage), storage), storage);
    }

    private void Execute(string sql)
    {
        using var connection = new SqliteConnection(ConnectionString());
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private string LastMigration(string path)
    {
        using var connection = new SqliteConnection(ConnectionString(path));
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT MigrationId FROM \"__EFMigrationsHistory\" ORDER BY MigrationId DESC LIMIT 1;";
        return (string)command.ExecuteScalar()!;
    }

    private static List<string> DataTables(SqliteConnection connection)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT name, sql FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' AND substr(name, 1, 4) <> '__EF' ORDER BY name;";
        var tables = new List<(string Name, string Sql)>();
        using (SqliteDataReader reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                tables.Add((reader.GetString(0), reader.IsDBNull(1) ? "" : reader.GetString(1)));
            }
        }
        // Full-text search indexes are rebuilt from the catalogue; their shadow tables are not records.
        string[] virtualTables = tables.Where(table => table.Sql.Contains("VIRTUAL TABLE", StringComparison.OrdinalIgnoreCase)).Select(table => table.Name).ToArray();
        return tables.Select(table => table.Name)
            .Where(name => !virtualTables.Any(fts => name == fts || name.StartsWith(fts + "_", StringComparison.Ordinal)))
            .ToList();
    }

    private static List<(string Name, string Type, bool NotNull)> Columns(SqliteConnection connection, string table)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info(\"{table}\");";
        var columns = new List<(string, string, bool)>();
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            columns.Add((reader.GetString(1), reader.GetString(2), reader.GetInt64(3) == 1));
        }
        return columns;
    }

    // Rows in every table, every column filled with a value unique to that row and column.
    private int SeedEveryTable(int rowsPerTable)
    {
        using var connection = new SqliteConnection(ConnectionString());
        connection.Open();
        int total = 0;
        foreach (string table in DataTables(connection))
        {
            var columns = Columns(connection, table);
            for (int row = 1; row <= rowsPerTable; row++)
            {
                using SqliteCommand insert = connection.CreateCommand();
                insert.CommandText = $"INSERT INTO \"{table}\" ({string.Join(", ", columns.Select(c => $"\"{c.Name}\""))}) VALUES ({string.Join(", ", columns.Select((_, i) => "$p" + i))});";
                for (int i = 0; i < columns.Count; i++)
                {
                    insert.Parameters.AddWithValue("$p" + i, ValueFor(table, columns[i].Name, columns[i].Type, row));
                }
                insert.ExecuteNonQuery();
                total++;
            }
        }
        return total;
    }

    private static object ValueFor(string table, string column, string type, int row)
    {
        string upper = type.ToUpperInvariant();
        if (upper.Contains("INT"))
        {
            // A business mode is an enum stored as text in the oldest schema; keep it a value every schema accepts.
            return row;
        }
        if (upper.Contains("DEC") || upper.Contains("REAL") || upper.Contains("NUM"))
        {
            return row + 0.25m;
        }
        if (upper.Contains("BLOB"))
        {
            return new byte[] { (byte)row, 7, 7 };
        }
        if (column == "BusinessMode")
        {
            return "Retail";
        }
        return $"{table}.{column}.{row}";
    }

    // Every row of every table as text, over the columns the database had before the upgrade.
    private Dictionary<string, List<string>> Fingerprint(Dictionary<string, List<string>>? before = null, string? path = null)
    {
        using var connection = new SqliteConnection(ConnectionString(path));
        connection.Open();
        var result = new Dictionary<string, List<string>>();
        foreach (string table in DataTables(connection))
        {
            if (before != null && !before.ContainsKey(table))
            {
                continue;
            }
            List<string> columns = Columns(connection, table).Select(c => c.Name)
                .Where(name => before == null || OriginalColumns.TryGetValue(table, out HashSet<string>? original) && original.Contains(name))
                .Order(StringComparer.Ordinal)
                .ToList();
            if (before == null)
            {
                OriginalColumns[table] = [.. columns];
            }
            // BusinessMode was text in the first schema and became a number: compared by meaning, not by spelling.
            columns.Remove("BusinessMode");
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = columns.Count == 0
                ? $"SELECT '' FROM \"{table}\";"
                : $"SELECT {string.Join(", ", columns.Select(c => $"\"{c}\""))} FROM \"{table}\";";
            var rows = new List<string>();
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                rows.Add(string.Join(" | ", Enumerable.Range(0, reader.FieldCount).Select(i => columns.Count == 0 ? "" : columns[i] + "=" + Convert.ToString(reader.GetValue(i), CultureInfo.InvariantCulture))));
            }
            rows.Sort(StringComparer.Ordinal);
            result[table] = rows;
        }
        return result;
    }

    private readonly Dictionary<string, HashSet<string>> OriginalColumns = new(StringComparer.Ordinal);
}
