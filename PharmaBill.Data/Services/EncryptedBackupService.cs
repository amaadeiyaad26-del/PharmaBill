using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Data.Services;

public sealed record BackupManifest(
    string Format,
    int BackupVersion,
    string ApplicationVersion,
    IReadOnlyList<string> AppliedMigrations,
    DateTime CreatedAtUtc,
    long DatabaseBytes,
    IReadOnlyDictionary<string, long> TableRowCounts,
    IReadOnlyDictionary<string, string> KeyTableChecksums,
    IReadOnlyList<string> Attachments);

public sealed record BackupRestoreResult(string SafetyBackupPath, BackupManifest Manifest);

public sealed class EncryptedBackupService(
    PharmaBillDbContext context,
    DatabaseStorageOptions storage,
    DatabaseEncryptionKeyProvider keyProvider)
{
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("PHARMBK1");
    private static readonly string[] KeyTables =
    [
        "Drugs", "Batches", "StockMovements", "Sales", "SaleItems",
        "WholesaleInvoices", "PurchaseInvoices", "ScheduleRegisterEntries"
    ];
    private const int BackupVersion = 1;
    private const int Pbkdf2Iterations = 600_000;

    public async Task<BackupManifest> CreateBackupAsync(
        string destinationPath,
        string password,
        CancellationToken cancellationToken = default)
    {
        ValidatePassword(password);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        cancellationToken.ThrowIfCancellationRequested();
        var workingDirectory = Path.Combine(Path.GetTempPath(), $"PharmaBill.Backup.{Guid.NewGuid():N}");
        Directory.CreateDirectory(workingDirectory);
        var databaseSnapshotPath = Path.Combine(workingDirectory, "database.db");
        var archivePath = Path.Combine(workingDirectory, "backup.zip");
        try
        {
            var databaseKey = keyProvider.GetOrCreateKey();
            try
            {
                await CreateDatabaseSnapshotAsync(databaseSnapshotPath, databaseKey, cancellationToken);
                var appliedMigrations = await context.Database.GetAppliedMigrationsAsync(cancellationToken);
                var integrity = await ReadIntegritySummaryAsync(databaseSnapshotPath, databaseKey, cancellationToken);
                var attachments = GetAttachmentFiles();
                var manifest = new BackupManifest(
                    "PharmaBill encrypted ZIP backup",
                    BackupVersion,
                    typeof(EncryptedBackupService).Assembly.GetName().Version?.ToString() ?? "unknown",
                    appliedMigrations.ToArray(),
                    DateTime.UtcNow,
                    new FileInfo(databaseSnapshotPath).Length,
                    integrity.RowCounts,
                    integrity.Checksums,
                    attachments.Select(item => item.RelativePath).ToArray());

                await CreateZipAsync(archivePath, databaseSnapshotPath, databaseKey, manifest, attachments, cancellationToken);
                await EncryptArchiveAsync(archivePath, destinationPath, password, cancellationToken);
                return manifest;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(databaseKey);
            }
        }
        finally
        {
            TryDeleteDirectory(workingDirectory);
        }
    }

    public async Task<BackupRestoreResult> RestoreBackupAsync(
        string backupPath,
        string password,
        CancellationToken cancellationToken = default)
    {
        ValidatePassword(password);
        ArgumentException.ThrowIfNullOrWhiteSpace(backupPath);
        if (!File.Exists(backupPath))
        {
            throw new FileNotFoundException("The selected backup file was not found.", backupPath);
        }

        var workingDirectory = Path.Combine(Path.GetTempPath(), $"PharmaBill.Restore.{Guid.NewGuid():N}");
        Directory.CreateDirectory(workingDirectory);
        var archivePath = Path.Combine(workingDirectory, "backup.zip");
        var stagedDirectory = Path.Combine(workingDirectory, "payload");
        Directory.CreateDirectory(stagedDirectory);
        byte[]? restoredKey = null;
        try
        {
            await DecryptArchiveAsync(backupPath, archivePath, password, cancellationToken);
            var manifest = await ExtractAndValidateAsync(archivePath, stagedDirectory, cancellationToken);
            var databasePath = Path.Combine(stagedDirectory, "database.db");
            restoredKey = await File.ReadAllBytesAsync(Path.Combine(stagedDirectory, "database-key.bin"), cancellationToken);
            if (restoredKey.Length != 32)
            {
                throw new InvalidDataException("Backup contains an invalid database encryption key.");
            }

            var availableMigrations = context.Database.GetMigrations().ToHashSet(StringComparer.Ordinal);
            if (manifest.AppliedMigrations.Any(migration => !availableMigrations.Contains(migration)))
            {
                throw new InvalidDataException("This backup was created by a newer or incompatible PharmaBill version.");
            }

            var integrity = await ReadIntegritySummaryAsync(databasePath, restoredKey, cancellationToken);
            if (!DictionaryEquals(manifest.TableRowCounts, integrity.RowCounts) ||
                !DictionaryEquals(manifest.KeyTableChecksums, integrity.Checksums))
            {
                throw new InvalidDataException("Backup database integrity checks did not match its manifest.");
            }

            await RemapAttachmentPathsAsync(databasePath, restoredKey, manifest.Attachments, cancellationToken);
            var requiredBytes = new FileInfo(databasePath).Length * 2L +
                                DirectorySize(stagedDirectory);
            var root = Path.GetPathRoot(storage.DatabasePath)
                ?? throw new InvalidOperationException("The database path has no volume root.");
            if (new DriveInfo(root).AvailableFreeSpace < requiredBytes)
            {
                throw new IOException("There is not enough free space to restore this backup safely.");
            }

            var safetyBackup = Path.Combine(
                storage.RootDirectory,
                $"safety-before-restore-{DateTime.UtcNow:yyyyMMdd-HHmmss}.pbbak");
            await CreateBackupAsync(safetyBackup, password, cancellationToken);
            await ReplaceApplicationDataAsync(databasePath, stagedDirectory, restoredKey, cancellationToken);
            return new BackupRestoreResult(safetyBackup, manifest);
        }
        finally
        {
            if (restoredKey is not null)
            {
                CryptographicOperations.ZeroMemory(restoredKey);
            }

            TryDeleteDirectory(workingDirectory);
        }
    }

    public static void ValidatePassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < 10)
        {
            throw new ArgumentException("Use a backup password of at least 10 characters.", nameof(password));
        }
    }

    private async Task CreateDatabaseSnapshotAsync(
        string destinationPath,
        byte[] key,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = storage.DatabasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
            Password = Convert.ToHexString(key)
        }.ToString();
        var destinationString = new SqliteConnectionStringBuilder
        {
            DataSource = destinationPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
            Password = Convert.ToHexString(key)
        }.ToString();
        await using var source = new SqliteConnection(connectionString);
        await using var destination = new SqliteConnection(destinationString);
        await source.OpenAsync(cancellationToken);
        await destination.OpenAsync(cancellationToken);
        source.BackupDatabase(destination);
    }

    private async Task CreateZipAsync(
        string archivePath,
        string databasePath,
        byte[] databaseKey,
        BackupManifest manifest,
        IReadOnlyList<(string FullPath, string RelativePath)> attachments,
        CancellationToken cancellationToken)
    {
        await using var output = new FileStream(archivePath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 131072, true);
        using var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
        var databaseEntry = archive.CreateEntry("database.db", CompressionLevel.Optimal);
        await using (var entryStream = databaseEntry.Open())
        await using (var input = new FileStream(databasePath, FileMode.Open, FileAccess.Read, FileShare.Read, 131072, true))
        {
            await input.CopyToAsync(entryStream, cancellationToken);
        }

        var keyEntry = archive.CreateEntry("database-key.bin", CompressionLevel.NoCompression);
        await using (var stream = keyEntry.Open())
        {
            await stream.WriteAsync(databaseKey, cancellationToken);
        }

        var manifestEntry = archive.CreateEntry("manifest.json", CompressionLevel.Optimal);
        await using (var stream = manifestEntry.Open())
        {
            await JsonSerializer.SerializeAsync(stream, manifest, cancellationToken: cancellationToken);
        }

        foreach (var attachment in attachments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = archive.CreateEntry($"attachments/{attachment.RelativePath}", CompressionLevel.Optimal);
            await using var entryStream = entry.Open();
            await using var fileStream = new FileStream(attachment.FullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
            await fileStream.CopyToAsync(entryStream, cancellationToken);
        }

        archive.Dispose();
        await output.FlushAsync(cancellationToken);
    }

    private async Task<BackupManifest> ExtractAndValidateAsync(
        string archivePath,
        string destination,
        CancellationToken cancellationToken)
    {
        await using var input = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read, 131072, true);
        using var archive = new ZipArchive(input, ZipArchiveMode.Read);
        var manifestEntry = archive.GetEntry("manifest.json")
            ?? throw new InvalidDataException("Backup is missing its manifest.");
        BackupManifest manifest;
        await using (var manifestStream = manifestEntry.Open())
        {
            manifest = await JsonSerializer.DeserializeAsync<BackupManifest>(manifestStream, cancellationToken: cancellationToken)
                ?? throw new InvalidDataException("Backup manifest is invalid.");
        }

        if (manifest.Format != "PharmaBill encrypted ZIP backup" ||
            manifest.BackupVersion != BackupVersion ||
            manifest.DatabaseBytes <= 0)
        {
            throw new InvalidDataException("Backup version or format is unsupported.");
        }

        var uncompressedSize = archive.Entries.Aggregate(0L, (size, entry) => checked(size + entry.Length));
        var tempRoot = Path.GetPathRoot(destination)
            ?? throw new InvalidOperationException("The restore staging path has no volume root.");
        if (new DriveInfo(tempRoot).AvailableFreeSpace < uncompressedSize * 2)
        {
            throw new IOException("There is not enough free space to stage and restore this backup.");
        }

        var allowedFiles = new HashSet<string>(StringComparer.Ordinal)
        {
            "database.db", "database-key.bin", "manifest.json"
        };
        foreach (var relative in manifest.Attachments)
        {
            var normalized = NormalizeZipPath($"attachments/{relative}");
            if (!normalized.StartsWith("attachments/", StringComparison.Ordinal))
            {
                throw new InvalidDataException("Backup contains an unsafe attachment path.");
            }

            allowedFiles.Add(normalized);
        }

        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var normalized = NormalizeZipPath(entry.FullName);
            if (!allowedFiles.Contains(normalized))
            {
                throw new InvalidDataException("Backup contains an unexpected file entry.");
            }

            var target = Path.GetFullPath(Path.Combine(destination, normalized.Replace('/', Path.DirectorySeparatorChar)));
            if (!target.StartsWith(Path.GetFullPath(destination) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Backup contains an unsafe path.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await using var entryStream = entry.Open();
            await using var targetStream = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 131072, true);
            await entryStream.CopyToAsync(targetStream, cancellationToken);
        }

        if (!File.Exists(Path.Combine(destination, "database.db")) ||
            new FileInfo(Path.Combine(destination, "database.db")).Length != manifest.DatabaseBytes ||
            !File.Exists(Path.Combine(destination, "database-key.bin")))
        {
            throw new InvalidDataException("Backup database or key is missing or incomplete.");
        }

        return manifest;
    }

    private async Task ReplaceApplicationDataAsync(
        string stagedDatabase,
        string stagedDirectory,
        byte[] newKey,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var oldKey = keyProvider.GetOrCreateKey();
        var rollbackDirectory = Path.Combine(Path.GetTempPath(), $"PharmaBill.Rollback.{Guid.NewGuid():N}");
        Directory.CreateDirectory(rollbackDirectory);
        var oldDatabase = Path.Combine(rollbackDirectory, "pharmabill.db");
        var databaseMoved = false;
        var keyChanged = false;
        try
        {
            var connection = context.Database.GetDbConnection();
            if (connection.State != System.Data.ConnectionState.Closed)
            {
                await context.Database.CloseConnectionAsync();
            }

            foreach (var folderName in new[] { "prescriptions", "licences" })
            {
                var original = Path.Combine(storage.RootDirectory, folderName);
                if (Directory.Exists(original))
                {
                    var moved = Path.Combine(rollbackDirectory, folderName);
                    Directory.Move(original, moved);
                }
            }

            if (File.Exists(storage.DatabasePath))
            {
                File.Move(storage.DatabasePath, oldDatabase);
                databaseMoved = true;
            }

            keyProvider.ReplaceKey(newKey);
            keyChanged = true;
            var databaseIncoming = Path.Combine(storage.RootDirectory, $"pharmabill-restore-{Guid.NewGuid():N}.tmp");
            try
            {
                File.Copy(stagedDatabase, databaseIncoming);
                File.Move(databaseIncoming, storage.DatabasePath);
            }
            finally
            {
                if (File.Exists(databaseIncoming))
                {
                    File.Delete(databaseIncoming);
                }
            }
            foreach (var folderName in new[] { "prescriptions", "licences" })
            {
                var stagedFolder = Path.Combine(stagedDirectory, "attachments", folderName);
                if (Directory.Exists(stagedFolder))
                {
                    Directory.Move(stagedFolder, Path.Combine(storage.RootDirectory, folderName));
                }
            }
        }
        catch
        {
            if (File.Exists(storage.DatabasePath))
            {
                File.Delete(storage.DatabasePath);
            }

            if (databaseMoved && File.Exists(oldDatabase))
            {
                File.Move(oldDatabase, storage.DatabasePath);
            }

            if (keyChanged)
            {
                keyProvider.ReplaceKey(oldKey);
            }

            foreach (var folderName in new[] { "prescriptions", "licences" })
            {
                var original = Path.Combine(storage.RootDirectory, folderName);
                var moved = Path.Combine(rollbackDirectory, folderName);
                if (Directory.Exists(original))
                {
                    Directory.Delete(original, recursive: true);
                }

                if (Directory.Exists(moved))
                {
                    Directory.Move(moved, original);
                }
            }

            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(oldKey);
            TryDeleteDirectory(rollbackDirectory);
        }
    }

    private async Task EncryptArchiveAsync(
        string archivePath,
        string destinationPath,
        string password,
        CancellationToken cancellationToken)
    {
        var fullDestination = Path.GetFullPath(destinationPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullDestination)!);
        var temporaryPath = $"{fullDestination}.{Guid.NewGuid():N}.tmp";
        var salt = RandomNumberGenerator.GetBytes(16);
        var iv = RandomNumberGenerator.GetBytes(16);
        var keyMaterial = Rfc2898DeriveBytes.Pbkdf2(password, salt, Pbkdf2Iterations, HashAlgorithmName.SHA256, 64);
        try
        {
            await using (var output = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 131072, true))
            {
                await output.WriteAsync(Magic, cancellationToken);
                await output.WriteAsync(BitConverter.GetBytes(Pbkdf2Iterations), cancellationToken);
                await output.WriteAsync(salt, cancellationToken);
                await output.WriteAsync(iv, cancellationToken);
                using var aes = Aes.Create();
                aes.KeySize = 256;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.Key = keyMaterial[..32];
                aes.IV = iv;
                await using (var crypto = new CryptoStream(output, aes.CreateEncryptor(), CryptoStreamMode.Write, leaveOpen: true))
                await using (var input = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read, 131072, true))
                {
                    await input.CopyToAsync(crypto, cancellationToken);
                    await crypto.FlushFinalBlockAsync(cancellationToken);
                }
            }

            await AppendMacAsync(temporaryPath, keyMaterial[32..], cancellationToken);
            File.Move(temporaryPath, fullDestination, overwrite: true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(keyMaterial);
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private async Task DecryptArchiveAsync(
        string backupPath,
        string archivePath,
        string password,
        CancellationToken cancellationToken)
    {
        await using var input = new FileStream(backupPath, FileMode.Open, FileAccess.Read, FileShare.Read, 131072, true);
        var header = new byte[Magic.Length + sizeof(int) + 16 + 16];
        if (await input.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, cancellationToken) != header.Length ||
            !header.AsSpan(0, Magic.Length).SequenceEqual(Magic))
        {
            throw new InvalidDataException("The selected file is not a PharmaBill backup.");
        }

        var iterations = BitConverter.ToInt32(header, Magic.Length);
        if (iterations is < 100_000 or > 2_000_000)
        {
            throw new InvalidDataException("Backup encryption parameters are unsupported.");
        }

        var saltOffset = Magic.Length + sizeof(int);
        var salt = header.AsSpan(saltOffset, 16).ToArray();
        var iv = header.AsSpan(saltOffset + 16, 16).ToArray();
        var fileLength = input.Length;
        var macLength = 32L;
        if (fileLength <= header.Length + macLength)
        {
            throw new InvalidDataException("Backup file is truncated.");
        }

        var keyMaterial = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, 64);
        try
        {
            using var hmac = new HMACSHA256(keyMaterial[32..]);
            input.Position = 0;
            var remaining = fileLength - macLength;
            var buffer = new byte[131072];
            while (remaining > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var read = await input.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), cancellationToken);
                if (read == 0)
                {
                    throw new InvalidDataException("Backup file is truncated.");
                }

                hmac.TransformBlock(buffer, 0, read, null, 0);
                remaining -= read;
            }

            hmac.TransformFinalBlock([], 0, 0);
            var expectedMac = hmac.Hash!;
            var actualMac = new byte[macLength];
            await input.ReadExactlyAsync(actualMac, cancellationToken);
            if (!CryptographicOperations.FixedTimeEquals(expectedMac, actualMac))
            {
                throw new CryptographicException("Backup password is incorrect or the file was modified.");
            }

            input.Position = header.Length;
            var cipherLength = fileLength - header.Length - macLength;
            using var aes = Aes.Create();
            aes.KeySize = 256;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.Key = keyMaterial[..32];
            aes.IV = iv;
            await using var output = new FileStream(archivePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 131072, true);
            using var crypto = new CryptoStream(new BoundedReadStream(input, cipherLength), aes.CreateDecryptor(), CryptoStreamMode.Read);
            await crypto.CopyToAsync(output, cancellationToken);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(keyMaterial);
        }
    }

    private static async Task AppendMacAsync(string path, byte[] key, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None, 131072, true);
        using var hmac = new HMACSHA256(key);
        var buffer = new byte[131072];
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            hmac.TransformBlock(buffer, 0, read, null, 0);
        }

        hmac.TransformFinalBlock([], 0, 0);
        stream.Position = stream.Length;
        await stream.WriteAsync(hmac.Hash!, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private async Task<(Dictionary<string, long> RowCounts, Dictionary<string, string> Checksums)> ReadIntegritySummaryAsync(
        string databasePath,
        byte[] key,
        CancellationToken cancellationToken)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
            Password = Convert.ToHexString(key)
        }.ToString();
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using (var check = connection.CreateCommand())
        {
            check.CommandText = "PRAGMA quick_check;";
            var result = Convert.ToString(await check.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
            if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The database failed SQLite integrity validation.");
            }
        }

        var tables = new List<string>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name;";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                tables.Add(reader.GetString(0));
            }
        }

        var counts = new Dictionary<string, long>(StringComparer.Ordinal);
        var checksums = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var table in tables)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT COUNT(*) FROM {QuoteIdentifier(table)};";
            counts[table] = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
            if (KeyTables.Contains(table, StringComparer.Ordinal))
            {
                checksums[table] = await ComputeTableChecksumAsync(connection, table, cancellationToken);
            }
        }

        return (counts, checksums);
    }

    private async Task RemapAttachmentPathsAsync(
        string databasePath,
        byte[] key,
        IReadOnlyList<string> attachmentPaths,
        CancellationToken cancellationToken)
    {
        var knownFiles = attachmentPaths.ToDictionary(
            item => Path.GetFileName(item),
            item => Path.Combine(storage.RootDirectory, item.Replace('/', Path.DirectorySeparatorChar)),
            StringComparer.OrdinalIgnoreCase);
        if (knownFiles.Count == 0)
        {
            return;
        }

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false,
            Password = Convert.ToHexString(key)
        }.ToString();
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var query = connection.CreateCommand())
        {
            query.Transaction = (SqliteTransaction)transaction;
            query.CommandText = "SELECT Id, DocumentPath FROM CustomerLicences WHERE DocumentPath IS NOT NULL;";
            var records = new List<(string Id, string Path)>();
            await using (var reader = await query.ExecuteReaderAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    var originalPath = reader.GetString(1);
                    if (knownFiles.TryGetValue(Path.GetFileName(originalPath), out var newPath))
                    {
                        records.Add((reader.GetString(0), newPath));
                    }
                }
            }

            foreach (var record in records)
            {
                await using var update = connection.CreateCommand();
                update.Transaction = (SqliteTransaction)transaction;
                update.CommandText = "UPDATE CustomerLicences SET DocumentPath = $path WHERE Id = $id;";
                update.Parameters.AddWithValue("$path", record.Path);
                update.Parameters.AddWithValue("$id", record.Id);
                await update.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        await using (var query = connection.CreateCommand())
        {
            query.Transaction = (SqliteTransaction)transaction;
            query.CommandText = "SELECT Id, Notes FROM Prescriptions WHERE Notes LIKE 'DocumentPath=%';";
            var records = new List<(string Id, string Notes)>();
            await using (var reader = await query.ExecuteReaderAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    var notes = reader.GetString(1);
                    var oldPath = notes["DocumentPath=".Length..];
                    if (knownFiles.TryGetValue(Path.GetFileName(oldPath), out var newPath))
                    {
                        records.Add((reader.GetString(0), $"DocumentPath={newPath}"));
                    }
                }
            }

            foreach (var record in records)
            {
                await using var update = connection.CreateCommand();
                update.Transaction = (SqliteTransaction)transaction;
                update.CommandText = "UPDATE Prescriptions SET Notes = $notes WHERE Id = $id;";
                update.Parameters.AddWithValue("$notes", record.Notes);
                update.Parameters.AddWithValue("$id", record.Id);
                await update.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task<string> ComputeTableChecksumAsync(
        SqliteConnection connection,
        string table,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT * FROM {QuoteIdentifier(table)} ORDER BY \"Id\";";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[8];
        while (await reader.ReadAsync(cancellationToken))
        {
            for (var index = 0; index < reader.FieldCount; index++)
            {
                var value = reader.GetValue(index);
                var bytes = value is DBNull
                    ? [byte.MaxValue]
                    : Encoding.UTF8.GetBytes(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty);
                BitConverter.TryWriteBytes(buffer, bytes.Length);
                hash.AppendData(buffer);
                hash.AppendData(bytes);
            }
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private List<(string FullPath, string RelativePath)> GetAttachmentFiles()
    {
        var files = new List<(string FullPath, string RelativePath)>();
        foreach (var folderName in new[] { "prescriptions", "licences" })
        {
            var folder = Path.Combine(storage.RootDirectory, folderName);
            if (!Directory.Exists(folder))
            {
                continue;
            }

            files.AddRange(Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                .Select(path => (path, Path.Combine(folderName, Path.GetRelativePath(folder, path))
                    .Replace(Path.DirectorySeparatorChar, '/'))));
        }

        return files;
    }

    private static string NormalizeZipPath(string path)
    {
        if (path.Contains('\\') || Path.IsPathRooted(path) ||
            path.Split('/').Any(segment => segment is ".." or "."))
        {
            throw new InvalidDataException("Backup contains an unsafe path.");
        }

        return path;
    }

    private static string QuoteIdentifier(string identifier) =>
        $"\"{identifier.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

    private static bool DictionaryEquals<T>(IReadOnlyDictionary<string, T> first, IReadOnlyDictionary<string, T> second) =>
        first.Count == second.Count && first.All(item =>
            second.TryGetValue(item.Key, out var value) && EqualityComparer<T>.Default.Equals(item.Value, value));

    private static long DirectorySize(string path) =>
        Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Sum(file => new FileInfo(file).Length);

    private static void TryDeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private sealed class BoundedReadStream(Stream inner, long remaining) : Stream
    {
        private long _remaining = remaining;
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_remaining <= 0)
            {
                return 0;
            }

            var read = inner.Read(buffer, offset, (int)Math.Min(count, _remaining));
            _remaining -= read;
            return read;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_remaining <= 0)
            {
                return 0;
            }

            var read = await inner.ReadAsync(buffer[..(int)Math.Min(buffer.Length, _remaining)], cancellationToken);
            _remaining -= read;
            return read;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }
            base.Dispose(disposing);
        }

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
