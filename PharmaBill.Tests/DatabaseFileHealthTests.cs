using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PharmaBill.Data.Persistence;
using PharmaBill.Data.Services;
using Xunit;

namespace PharmaBill.Tests;

public sealed class DatabaseFileHealthTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"PharmaBill.DbHealth.{Guid.NewGuid():N}");

    private readonly string _key = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    public DatabaseFileHealthTests()
    {
        SQLitePCL.Batteries_V2.Init();
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_directory, recursive: true);
    }

    private string DbPath => Path.Combine(_directory, "data", "pharmabill.db");

    private string ConnectionString(string? key = null) =>
        new SqliteConnectionStringBuilder { DataSource = DbPath, Pooling = false, Password = key ?? _key }.ToString();

    [Fact]
    public void Inspect_MissingFile_IsMissing()
    {
        Assert.Equal(DatabaseFileState.Missing, DatabaseFileHealth.Inspect(ConnectionString()).State);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(8192)]
    public void Inspect_ZeroLengthOrAllZeroFile_IsEmpty(int zeroBytes)
    {
        WriteFile(new byte[zeroBytes]);

        Assert.Equal(DatabaseFileState.Empty, DatabaseFileHealth.Inspect(ConnectionString()).State);
    }

    [Fact]
    public void Inspect_GarbageFile_IsUnreadable_AndLeftUntouched()
    {
        byte[] garbage = RandomNumberGenerator.GetBytes(4096);
        WriteFile(garbage);

        DatabaseFileCheck check = DatabaseFileHealth.Inspect(ConnectionString());

        Assert.Equal(DatabaseFileState.Unreadable, check.State);
        Assert.Equal(garbage, File.ReadAllBytes(DbPath));
    }

    [Fact]
    public void Inspect_ValidDatabaseWithTheWrongKey_IsUnreadable_NotEmpty()
    {
        // The case that must never be auto-replaced: the file is fine, only the key differs.
        CreateEncryptedDatabase(_key);

        Assert.Equal(DatabaseFileState.Readable, DatabaseFileHealth.Inspect(ConnectionString()).State);
        Assert.Equal(DatabaseFileState.Unreadable, DatabaseFileHealth.Inspect(ConnectionString(Convert.ToHexString(RandomNumberGenerator.GetBytes(32)))).State);
    }

    [Fact]
    public void Inspect_UnencryptedSqliteFile_IsUnreadable_AndRecognised()
    {
        CreateEncryptedDatabase(key: null);

        DatabaseFileCheck check = DatabaseFileHealth.Inspect(ConnectionString());

        Assert.Equal(DatabaseFileState.Unreadable, check.State);
        Assert.True(check.IsUnencryptedSqlite);
    }

    [Fact]
    public void Quarantine_RenamesTheFileAndItsCompanions_AndDeletesNothing()
    {
        WriteFile([1, 2, 3]);
        File.WriteAllBytes(DbPath + "-wal", [4]);
        File.WriteAllBytes(DbPath + "-shm", [5]);
        var when = new DateTime(2026, 10, 21, 16, 5, 9);

        string first = DatabaseFileHealth.Quarantine(DbPath, "unreadable", when);
        WriteFile([9]);
        string second = DatabaseFileHealth.Quarantine(DbPath, "unreadable", when);

        Assert.Equal(DbPath + ".unreadable-20261021-160509", first);
        Assert.Equal(first + "-2", second);
        Assert.False(File.Exists(DbPath));
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(first));
        Assert.Equal(new byte[] { 4 }, File.ReadAllBytes(first + "-wal"));
        Assert.Equal(new byte[] { 5 }, File.ReadAllBytes(first + "-shm"));
        Assert.Equal(new byte[] { 9 }, File.ReadAllBytes(second));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4096)]
    public async Task Initializer_EmptyFile_IsMovedAside_AndAFreshDatabaseIsCreated(int zeroBytes)
    {
        WriteFile(new byte[zeroBytes]);

        await using (PharmaBillDbContext context = CreateContext())
        {
            await CreateInitializer(context).InitializeAsync();
            // The fresh database has the schema: querying the profile table must not throw.
            Assert.True(await context.PharmacyProfiles.CountAsync() >= 0);
        }

        Assert.True(new FileInfo(DbPath).Length > 0);
        Assert.Equal(DatabaseFileState.Readable, DatabaseFileHealth.Inspect(ConnectionString()).State);
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(DbPath)!, "pharmabill.db.empty-*"));
    }

    [Fact]
    public async Task Initializer_UnreadableFile_IsArchivedUnchanged_AndAFreshDatabaseIsCreated()
    {
        byte[] garbage = RandomNumberGenerator.GetBytes(8192);
        WriteFile(garbage);

        await using (PharmaBillDbContext context = CreateContext())
        {
            await CreateInitializer(context).InitializeAsync();
            Assert.True(await context.PharmacyProfiles.CountAsync() >= 0);
        }

        string archived = Assert.Single(Directory.GetFiles(Path.GetDirectoryName(DbPath)!, "pharmabill.db.unreadable-*"));
        Assert.Equal(garbage, File.ReadAllBytes(archived));
        Assert.Equal(archived, DbInitializer.LastArchivedUnreadableDatabase);
        Assert.Equal(DatabaseFileState.Readable, DatabaseFileHealth.Inspect(ConnectionString()).State);
    }

    [Fact]
    public async Task Initializer_DatabaseEncryptedWithAnotherKey_IsArchived_AndStillOpensWithThatKey()
    {
        string otherKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        CreateEncryptedDatabase(otherKey);

        await using (PharmaBillDbContext context = CreateContext())
        {
            await CreateInitializer(context).InitializeAsync();
        }

        // The archived file is the original, intact: with its own key the records are still there.
        string archived = Assert.Single(Directory.GetFiles(Path.GetDirectoryName(DbPath)!, "pharmabill.db.unreadable-*"));
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = archived, Pooling = false, Password = otherKey }.ToString());
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT Name FROM Records;";
        Assert.Equal("kept", command.ExecuteScalar());
    }

    [Fact]
    public async Task Initializer_UnreadableFileThatCannotBeArchived_StopsWithARecoverableError()
    {
        byte[] garbage = RandomNumberGenerator.GetBytes(8192);
        WriteFile(garbage);
        using FileStream heldOpen = new FileStream(DbPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

        await using PharmaBillDbContext context = CreateContext();
        DatabaseUnreadableException error = await Assert.ThrowsAsync<DatabaseUnreadableException>(() => CreateInitializer(context).InitializeAsync());

        Assert.Contains("could not be archived", error.Message);
        Assert.Equal(DbPath, error.DatabasePath);
    }
    private void WriteFile(byte[] content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DbPath)!);
        File.WriteAllBytes(DbPath, content);
    }

    private void CreateEncryptedDatabase(string? key)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DbPath)!);
        var builder = new SqliteConnectionStringBuilder { DataSource = DbPath, Pooling = false };
        if (key != null)
        {
            builder.Password = key;
        }

        using var connection = new SqliteConnection(builder.ToString());
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE Records (Id INTEGER PRIMARY KEY, Name TEXT); INSERT INTO Records (Name) VALUES ('kept');";
        command.ExecuteNonQuery();
    }

    private PharmaBillDbContext CreateContext()
    {
        var deviceId = new DatabaseDeviceId(Guid.NewGuid());
        var options = new DbContextOptionsBuilder<PharmaBillDbContext>()
            .UseSqlite(ConnectionString())
            .AddInterceptors(new SaveChangesAuditInterceptor(deviceId), SqliteWalInterceptor.Instance)
            .Options;
        return new PharmaBillDbContext(options, deviceId);
    }

    private DbInitializer CreateInitializer(PharmaBillDbContext context)
    {
        var storage = new DatabaseStorageOptions(_directory);
        return new DbInitializer(context, new StorageLocationService(context), new BranchService(new UnitOfWork(context), new BranchSettingsStore(storage), storage), storage);
    }
}
