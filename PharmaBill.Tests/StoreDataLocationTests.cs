using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using PharmaBill.Data.Persistence;
using Xunit;

namespace PharmaBill.Tests;

public sealed class StoreDataLocationTests : IDisposable
{
    private readonly string _sandbox = Path.Combine(Path.GetTempPath(), $"PharmaBill.DataLocation.{Guid.NewGuid():N}");

    public StoreDataLocationTests()
    {
        SQLitePCL.Batteries_V2.Init();
        Directory.CreateDirectory(PackageRoot);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_sandbox, recursive: true);
    }

    /// <summary>Stands in for the Store package's virtualised %LocalAppData%\PharmaBill.</summary>
    private string PackageRoot => Path.Combine(_sandbox, "package", "PharmaBill");

    private string Documents => Path.Combine(_sandbox, "Documents");

    [Fact]
    public void FirstRun_HasNoLocation_ThenTheChosenFolderIsRememberedAndUsed()
    {
        Assert.Equal(DataLocationStatus.NotChosen, StoreDataLocation.Check(PackageRoot).Status);
        string profile = Path.Combine(_sandbox, "Users", "Shop Assistant");
        string chosen = StoreDataLocation.DefaultFolder(profile);
        // In the user's profile (no administrator rights needed), not in Documents, which is often synced by OneDrive.
        Assert.Equal(Path.Combine(profile, "Pharma Remit Data"), chosen.Replace(" Debug", string.Empty));

        DataLocationResult result = StoreDataLocation.Choose(PackageRoot, chosen);

        Assert.Equal(DataLocationOutcome.NewEmpty, result.Outcome);
        DataLocationCheck check = StoreDataLocation.Check(PackageRoot);
        Assert.Equal(DataLocationStatus.Ready, check.Status);
        Assert.Equal(Path.GetFullPath(chosen), check.DataRoot);
        Assert.True(File.Exists(Path.Combine(chosen, StoreDataLocation.MarkerFileName)));
    }

    [Fact]
    public void Upgrade_ExistingPackageData_IsCopiedVerifiedAndSwitched_LeavingTheOriginalIntact()
    {
        string password = CreatePackageData();
        string target = Path.Combine(Documents, "Pharma Remit");
        Dictionary<string, byte[]> before = HashTree(PackageRoot);

        DataLocationResult result = StoreDataLocation.Choose(PackageRoot, target);

        Assert.Equal(DataLocationOutcome.MigratedFromPackage, result.Outcome);
        // Records, key, backups and attachments arrived byte for byte; logs and licence state stayed on the PC.
        foreach (string relative in new[] { @"Sameerchemists\pharmabill.db", @"data\database.key", @"data\backups\daily.pbbak", @"prescriptions\RX-1.pdf" })
        {
            Assert.Equal(before[relative], SHA256.HashData(File.ReadAllBytes(Path.Combine(target, relative))));
        }

        Assert.False(File.Exists(Path.Combine(target, @"data\logs\pharmabill-20261021.log")));
        Assert.False(File.Exists(Path.Combine(target, @"data\license.state")));
        Assert.Equal("Bill WIN1/2026-27/000001", ReadRecord(Path.Combine(target, @"Sameerchemists\pharmabill.db"), password));

        // The original is untouched; the app now points at the copy.
        foreach ((string relative, byte[] hash) in before)
        {
            Assert.Equal(hash, SHA256.HashData(File.ReadAllBytes(Path.Combine(PackageRoot, relative))));
        }

        Assert.Equal(DataLocationStatus.Ready, StoreDataLocation.Check(PackageRoot).Status);
    }

    [Fact]
    public void Upgrade_CopyThatDoesNotOpen_IsNotSwitchedTo_AndTheOriginalIsKept()
    {
        CreatePackageData();
        // Replace the key with a different one: the copied database would not open with it.
        File.Delete(Path.Combine(PackageRoot, @"data\database.key"));
        new DatabaseEncryptionKeyProvider(Path.Combine(PackageRoot, @"data\database.key")).GetOrCreateKey();
        string target = Path.Combine(Documents, "Pharma Remit");

        var error = Assert.Throws<InvalidOperationException>(() => StoreDataLocation.Choose(PackageRoot, target));

        Assert.Contains("not moved", error.Message);
        Assert.Empty(Directory.EnumerateFileSystemEntries(target));
        Assert.True(File.Exists(Path.Combine(PackageRoot, @"Sameerchemists\pharmabill.db")));
        Assert.Equal(DataLocationStatus.NotChosen, StoreDataLocation.Check(PackageRoot).Status);
    }

    [Fact]
    public void MissingFolderAtStartup_IsReported_NotTreatedAsAFreshStart()
    {
        CreatePackageData();
        string target = Path.Combine(Documents, "Pharma Remit");
        StoreDataLocation.Choose(PackageRoot, target);
        SqliteConnection.ClearAllPools();
        Directory.Delete(target, recursive: true);

        DataLocationCheck check = StoreDataLocation.Check(PackageRoot);

        Assert.Equal(DataLocationStatus.FolderMissing, check.Status);
        Assert.Contains(target, check.Message);
    }

    [Fact]
    public void DatabaseGoneFromAFolderThatHadOne_IsReported_NotTreatedAsAFreshStart()
    {
        string target = Path.Combine(Documents, "Pharma Remit");
        StoreDataLocation.Choose(PackageRoot, target);
        Directory.CreateDirectory(Path.Combine(target, "Sameerchemists"));
        File.WriteAllBytes(Path.Combine(target, @"Sameerchemists\pharmabill.db"), [1, 2, 3]);
        StoreDataLocation.MarkHasDatabase(target);
        File.Delete(Path.Combine(target, @"Sameerchemists\pharmabill.db"));

        Assert.Equal(DataLocationStatus.DatabaseMissing, StoreDataLocation.Check(PackageRoot).Status);
    }

    [Fact]
    public void Reinstall_PointsAtTheExistingFolder_WithoutCopyingOrOverwriting()
    {
        string target = Path.Combine(Documents, "Pharma Remit");
        Directory.CreateDirectory(Path.Combine(target, "Sameerchemists"));
        File.WriteAllBytes(Path.Combine(target, @"Sameerchemists\pharmabill.db"), [9, 9, 9]);

        DataLocationResult result = StoreDataLocation.Choose(PackageRoot, target);

        Assert.Equal(DataLocationOutcome.AttachedExisting, result.Outcome);
        Assert.Equal(new byte[] { 9, 9, 9 }, File.ReadAllBytes(Path.Combine(target, @"Sameerchemists\pharmabill.db")));
    }

    [Fact]
    public void Choose_RefusesToMixTwoSetsOfData_OrToUseTheAppsOwnFolder()
    {
        CreatePackageData();
        string other = Path.Combine(Documents, "Other pharmacy");
        Directory.CreateDirectory(Path.Combine(other, "data"));
        File.WriteAllBytes(Path.Combine(other, @"data\pharmabill.db"), [1]);

        Assert.Throws<InvalidOperationException>(() => StoreDataLocation.Choose(PackageRoot, other));
        Assert.Throws<InvalidOperationException>(() => StoreDataLocation.Choose(PackageRoot, Path.Combine(PackageRoot, "inside")));
        Assert.Throws<InvalidOperationException>(() => StoreDataLocation.Relocate(PackageRoot, Path.Combine(Documents, "empty")));
    }

    [Theory]
    [InlineData("OneDrive", @"OneDrive\Documents\Pharma Remit")]
    [InlineData("Google Drive", @"My Drive\Pharmacy\Pharma Remit Data")]
    [InlineData("Dropbox", @"Dropbox (Personal)\Pharma Remit Data")]
    public void FoldersInsideCloudSyncedFolders_AreRecognised_WithTheServiceNamed(string provider, string relative)
    {
        CloudSyncRoot[] roots =
        [
            new("OneDrive", Path.Combine(_sandbox, "OneDrive")),
            new("Google Drive", Path.Combine(_sandbox, "My Drive")),
            new("Dropbox", Path.Combine(_sandbox, "Dropbox (Personal)"))
        ];

        Assert.Equal(provider, StoreDataLocation.CloudSyncProvider(Path.Combine(_sandbox, relative), roots));
        Assert.Null(StoreDataLocation.CloudSyncProvider(Path.Combine(_sandbox, "Users", "Shop", "Pharma Remit Data"), roots));
        // A folder merely named like a sync root, but not inside one, is not flagged.
        Assert.Null(StoreDataLocation.CloudSyncProvider(Path.Combine(_sandbox, "OneDrive Notes"), roots));
    }
    [Fact]
    public void Upgrade_PointsStoredDocumentPathsAtTheMovedFiles_InTheCopyOnly()
    {
        string password = CreatePackageData();
        string dbPath = Path.Combine(PackageRoot, @"Sameerchemists\pharmabill.db");
        string rx = Path.Combine(PackageRoot, @"prescriptions\2026\WIN1_000004\rx-1.jpg");
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = dbPath, Password = password, Pooling = false }.ToString()))
        {
            connection.Open();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE Sales (Id TEXT, PrescriptionFilePath TEXT); CREATE TABLE Prescriptions (Id TEXT, Notes TEXT);"
                + "INSERT INTO Sales VALUES ('s1', $path); INSERT INTO Prescriptions VALUES ('p1', 'DocumentPath=' || $path);";
            command.Parameters.AddWithValue("$path", rx);
            command.ExecuteNonQuery();
        }

        Directory.CreateDirectory(Path.GetDirectoryName(rx)!);
        File.WriteAllBytes(rx, [1, 2, 3]);
        SqliteConnection.ClearAllPools();
        string target = Path.Combine(_sandbox, "Users", "Shop", "Pharma Remit Data");

        StoreDataLocation.Choose(PackageRoot, target);

        string moved = Path.Combine(target, @"prescriptions\2026\WIN1_000004\rx-1.jpg");
        Assert.Equal(moved, Query(Path.Combine(target, @"Sameerchemists\pharmabill.db"), password, "SELECT PrescriptionFilePath FROM Sales"));
        Assert.Equal("DocumentPath=" + moved, Query(Path.Combine(target, @"Sameerchemists\pharmabill.db"), password, "SELECT Notes FROM Prescriptions"));
        Assert.True(File.Exists(moved));
        // The original database still records the original location.
        Assert.Equal(rx, Query(dbPath, password, "SELECT PrescriptionFilePath FROM Sales"));
    }

    private static string? Query(string database, string password, string sql)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = database, Password = password, Pooling = false }.ToString());
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar() as string;
    }
    /// <summary>A package folder as a live Store install has it: an encrypted store database, key, backup, attachment, logs and licence state.</summary>
    private string CreatePackageData()
    {
        string keyPath = Path.Combine(PackageRoot, @"data\database.key");
        string password = Convert.ToHexString(new DatabaseEncryptionKeyProvider(keyPath).GetOrCreateKey());
        Directory.CreateDirectory(Path.Combine(PackageRoot, "Sameerchemists"));
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.Combine(PackageRoot, @"Sameerchemists\pharmabill.db"), Password = password, Pooling = false }.ToString()))
        {
            connection.Open();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE Records (Text TEXT); INSERT INTO Records VALUES ('Bill WIN1/2026-27/000001');";
            command.ExecuteNonQuery();
        }

        foreach ((string relative, string content) in new[]
        {
            (@"data\backups\daily.pbbak", "backup"),
            (@"prescriptions\RX-1.pdf", "%PDF-1.7 prescription"),
            (@"data\logs\pharmabill-20261021.log", "log line"),
            (@"data\license.state", "licence")
        })
        {
            string path = Path.Combine(PackageRoot, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }

        SqliteConnection.ClearAllPools();
        return password;
    }

    private static Dictionary<string, byte[]> HashTree(string root) =>
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .ToDictionary(file => Path.GetRelativePath(root, file), file => SHA256.HashData(File.ReadAllBytes(file)), StringComparer.OrdinalIgnoreCase);

    private static string? ReadRecord(string database, string password)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = database, Password = password, Pooling = false }.ToString());
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT Text FROM Records;";
        return command.ExecuteScalar() as string;
    }
}
