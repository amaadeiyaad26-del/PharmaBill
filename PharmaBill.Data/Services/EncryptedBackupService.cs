using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Data.Services;

public sealed class EncryptedBackupService(PharmaBillDbContext context, DatabaseStorageOptions storage, DatabaseEncryptionKeyProvider keyProvider)
{
	private sealed class BoundedReadStream(Stream inner, long remaining) : Stream
	{
		private long _remaining = remaining;

		public override bool CanRead => inner.CanRead;

		public override bool CanSeek => false;

		public override bool CanWrite => false;

		public override long Length
		{
			get
			{
				throw new NotSupportedException();
			}
		}

		public override long Position
		{
			get
			{
				throw new NotSupportedException();
			}
			set
			{
				throw new NotSupportedException();
			}
		}

		public override int Read(byte[] buffer, int offset, int count)
		{
			if (_remaining <= 0)
			{
				return 0;
			}
			int num = inner.Read(buffer, offset, (int)Math.Min(count, _remaining));
			_remaining -= num;
			return num;
		}

		public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default(CancellationToken))
		{
			if (_remaining <= 0)
			{
				return 0;
			}
			int num = await inner.ReadAsync(buffer.Slice(0, (int)Math.Min(buffer.Length, _remaining)), cancellationToken);
			_remaining -= num;
			return num;
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				inner.Dispose();
			}
			base.Dispose(disposing);
		}

		public override void Flush()
		{
			throw new NotSupportedException();
		}

		public override long Seek(long offset, SeekOrigin origin)
		{
			throw new NotSupportedException();
		}

		public override void SetLength(long value)
		{
			throw new NotSupportedException();
		}

		public override void Write(byte[] buffer, int offset, int count)
		{
			throw new NotSupportedException();
		}
	}

	private static readonly byte[] Magic = Encoding.ASCII.GetBytes("PHARMBK1");

	private static readonly string[] KeyTables = new string[8] { "Drugs", "Batches", "StockMovements", "Sales", "SaleItems", "WholesaleInvoices", "PurchaseInvoices", "ScheduleRegisterEntries" };

	private const int BackupVersion = 1;

	private const int Pbkdf2Iterations = 600000;

	public async Task<BackupManifest> CreateBackupAsync(string destinationPath, string password, CancellationToken cancellationToken = default(CancellationToken))
	{
		ValidatePassword(password);
		ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath, "destinationPath");
		cancellationToken.ThrowIfCancellationRequested();
		string workingDirectory = Path.Combine(Path.GetTempPath(), $"PharmaBill.Backup.{Guid.NewGuid():N}");
		Directory.CreateDirectory(workingDirectory);
		string databaseSnapshotPath = Path.Combine(workingDirectory, "database.db");
		string archivePath = Path.Combine(workingDirectory, "backup.zip");
		try
		{
			byte[] databaseKey = keyProvider.GetOrCreateKey();
			try
			{
				await CreateDatabaseSnapshotAsync(databaseSnapshotPath, databaseKey, cancellationToken);
				IEnumerable<string> appliedMigrations = await context.Database.GetAppliedMigrationsAsync(cancellationToken);
				(Dictionary<string, long>, Dictionary<string, string>) tuple = await ReadIntegritySummaryAsync(databaseSnapshotPath, databaseKey, cancellationToken);
				List<(string, string)> attachmentFiles = GetAttachmentFiles();
				BackupManifest manifest = new BackupManifest("PharmaBill encrypted ZIP backup", 1, typeof(EncryptedBackupService).Assembly.GetName().Version?.ToString() ?? "unknown", appliedMigrations.ToArray(), DateTime.UtcNow, new FileInfo(databaseSnapshotPath).Length, tuple.Item1, tuple.Item2, attachmentFiles.Select(((string FullPath, string RelativePath) item) => item.RelativePath).ToArray());
				await CreateZipAsync(archivePath, databaseSnapshotPath, databaseKey, manifest, attachmentFiles, cancellationToken);
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

	public async Task<BackupRestoreResult> RestoreBackupAsync(string backupPath, string password, CancellationToken cancellationToken = default(CancellationToken))
	{
		ValidatePassword(password);
		ArgumentException.ThrowIfNullOrWhiteSpace(backupPath, "backupPath");
		if (!File.Exists(backupPath))
		{
			throw new FileNotFoundException("The selected backup file was not found.", backupPath);
		}
		string workingDirectory = Path.Combine(Path.GetTempPath(), $"PharmaBill.Restore.{Guid.NewGuid():N}");
		Directory.CreateDirectory(workingDirectory);
		string archivePath = Path.Combine(workingDirectory, "backup.zip");
		string stagedDirectory = Path.Combine(workingDirectory, "payload");
		Directory.CreateDirectory(stagedDirectory);
		byte[] restoredKey = null;
		try
		{
			await DecryptArchiveAsync(backupPath, archivePath, password, cancellationToken);
			BackupManifest manifest = await ExtractAndValidateAsync(archivePath, stagedDirectory, cancellationToken);
			string databasePath = Path.Combine(stagedDirectory, "database.db");
			restoredKey = await File.ReadAllBytesAsync(Path.Combine(stagedDirectory, "database-key.bin"), cancellationToken);
			if (restoredKey.Length != 32)
			{
				throw new InvalidDataException("Backup contains an invalid database encryption key.");
			}
			HashSet<string> availableMigrations = context.Database.GetMigrations().ToHashSet(StringComparer.Ordinal);
			if (manifest.AppliedMigrations.Any((string migration) => !availableMigrations.Contains(migration)))
			{
				throw new InvalidDataException("This backup was created by a newer or incompatible PharmaBill version.");
			}
			(Dictionary<string, long>, Dictionary<string, string>) tuple = await ReadIntegritySummaryAsync(databasePath, restoredKey, cancellationToken);
			if (!DictionaryEquals(manifest.TableRowCounts, tuple.Item1) || !DictionaryEquals(manifest.KeyTableChecksums, tuple.Item2))
			{
				throw new InvalidDataException("Backup database integrity checks did not match its manifest.");
			}
			await RemapAttachmentPathsAsync(databasePath, restoredKey, manifest.Attachments, cancellationToken);
			long num = new FileInfo(databasePath).Length * 2 + DirectorySize(stagedDirectory);
			if (new DriveInfo(Path.GetPathRoot(storage.DatabasePath) ?? throw new InvalidOperationException("The database path has no volume root.")).AvailableFreeSpace < num)
			{
				throw new IOException("There is not enough free space to restore this backup safely.");
			}
			string safetyBackup = Path.Combine(storage.RootDirectory, $"safety-before-restore-{DateTime.UtcNow:yyyyMMdd-HHmmss}.pbbak");
			await CreateBackupAsync(safetyBackup, password, cancellationToken);
			await ReplaceApplicationDataAsync(databasePath, stagedDirectory, restoredKey, cancellationToken);
			return new BackupRestoreResult(safetyBackup, manifest);
		}
		finally
		{
			if (restoredKey != null)
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
			throw new ArgumentException("Use a backup password of at least 10 characters.", "password");
		}
	}

	private async Task CreateDatabaseSnapshotAsync(string destinationPath, byte[] key, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		string connectionString = new SqliteConnectionStringBuilder
		{
			DataSource = storage.DatabasePath,
			Mode = SqliteOpenMode.ReadOnly,
			Pooling = false,
			Password = Convert.ToHexString(key)
		}.ToString();
		string connectionString2 = new SqliteConnectionStringBuilder
		{
			DataSource = destinationPath,
			Mode = SqliteOpenMode.ReadWriteCreate,
			Pooling = false,
			Password = Convert.ToHexString(key)
		}.ToString();
		await using SqliteConnection source = new SqliteConnection(connectionString);
		await using SqliteConnection destination = new SqliteConnection(connectionString2);
		await source.OpenAsync(cancellationToken);
		await destination.OpenAsync(cancellationToken);
		source.BackupDatabase(destination);
	}

	private async Task CreateZipAsync(string archivePath, string databasePath, byte[] databaseKey, BackupManifest manifest, IReadOnlyList<(string FullPath, string RelativePath)> attachments, CancellationToken cancellationToken)
	{
		await using FileStream output = new FileStream(archivePath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 131072, useAsync: true);
		using ZipArchive archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
		ZipArchiveEntry zipArchiveEntry = archive.CreateEntry("database.db", CompressionLevel.Optimal);
		await using (Stream entryStream = zipArchiveEntry.Open())
		{
			await using FileStream input = new FileStream(databasePath, FileMode.Open, FileAccess.Read, FileShare.Read, 131072, useAsync: true);
			await input.CopyToAsync(entryStream, cancellationToken);
		}
		ZipArchiveEntry zipArchiveEntry2 = archive.CreateEntry("database-key.bin", CompressionLevel.NoCompression);
		await using (Stream entryStream = zipArchiveEntry2.Open())
		{
			await entryStream.WriteAsync(databaseKey, cancellationToken);
		}
		ZipArchiveEntry zipArchiveEntry3 = archive.CreateEntry("manifest.json", CompressionLevel.Optimal);
		await using (Stream entryStream = zipArchiveEntry3.Open())
		{
			await JsonSerializer.SerializeAsync(entryStream, manifest, (JsonSerializerOptions?)null, cancellationToken);
		}
		foreach (var attachment in attachments)
		{
			cancellationToken.ThrowIfCancellationRequested();
			ZipArchiveEntry zipArchiveEntry4 = archive.CreateEntry("attachments/" + attachment.RelativePath, CompressionLevel.Optimal);
			await using Stream entryStream = zipArchiveEntry4.Open();
			await using FileStream input = new FileStream(attachment.FullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
			await input.CopyToAsync(entryStream, cancellationToken);
		}
		archive.Dispose();
		await output.FlushAsync(cancellationToken);
	}

	private async Task<BackupManifest> ExtractAndValidateAsync(string archivePath, string destination, CancellationToken cancellationToken)
	{
		BackupManifest result;
		await using (FileStream input = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read, 131072, useAsync: true))
		{
			using ZipArchive archive = new ZipArchive(input, ZipArchiveMode.Read);
			ZipArchiveEntry zipArchiveEntry = archive.GetEntry("manifest.json") ?? throw new InvalidDataException("Backup is missing its manifest.");
			BackupManifest manifest;
			await using (Stream manifestStream = zipArchiveEntry.Open())
			{
				manifest = (await JsonSerializer.DeserializeAsync<BackupManifest>(manifestStream, (JsonSerializerOptions?)null, cancellationToken)) ?? throw new InvalidDataException("Backup manifest is invalid.");
			}
			if (manifest.Format != "PharmaBill encrypted ZIP backup" || manifest.BackupVersion != 1 || manifest.DatabaseBytes <= 0)
			{
				throw new InvalidDataException("Backup version or format is unsupported.");
			}
			long num = archive.Entries.Aggregate(0L, (long size, ZipArchiveEntry entry) => checked(size + entry.Length));
			if (new DriveInfo(Path.GetPathRoot(destination) ?? throw new InvalidOperationException("The restore staging path has no volume root.")).AvailableFreeSpace < num * 2)
			{
				throw new IOException("There is not enough free space to stage and restore this backup.");
			}
			HashSet<string> allowedFiles = new HashSet<string>(StringComparer.Ordinal) { "database.db", "database-key.bin", "manifest.json" };
			foreach (string attachment in manifest.Attachments)
			{
				string text = NormalizeZipPath("attachments/" + attachment);
				if (!text.StartsWith("attachments/", StringComparison.Ordinal))
				{
					throw new InvalidDataException("Backup contains an unsafe attachment path.");
				}
				allowedFiles.Add(text);
			}
			foreach (ZipArchiveEntry entry in archive.Entries)
			{
				cancellationToken.ThrowIfCancellationRequested();
				string text2 = NormalizeZipPath(entry.FullName);
				if (!allowedFiles.Contains(text2))
				{
					throw new InvalidDataException("Backup contains an unexpected file entry.");
				}
				string fullPath = Path.GetFullPath(Path.Combine(destination, text2.Replace('/', Path.DirectorySeparatorChar)));
				if (!fullPath.StartsWith(Path.GetFullPath(destination) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
				{
					throw new InvalidDataException("Backup contains an unsafe path.");
				}
				Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
				await using Stream manifestStream = entry.Open();
				await using FileStream targetStream = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 131072, useAsync: true);
				await manifestStream.CopyToAsync(targetStream, cancellationToken);
			}
			if (!File.Exists(Path.Combine(destination, "database.db")) || new FileInfo(Path.Combine(destination, "database.db")).Length != manifest.DatabaseBytes || !File.Exists(Path.Combine(destination, "database-key.bin")))
			{
				throw new InvalidDataException("Backup database or key is missing or incomplete.");
			}
			result = manifest;
		}
		return result;
	}

	private async Task ReplaceApplicationDataAsync(string stagedDatabase, string stagedDirectory, byte[] newKey, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		byte[] oldKey = keyProvider.GetOrCreateKey();
		string rollbackDirectory = Path.Combine(Path.GetTempPath(), $"PharmaBill.Rollback.{Guid.NewGuid():N}");
		Directory.CreateDirectory(rollbackDirectory);
		string oldDatabase = Path.Combine(rollbackDirectory, "pharmabill.db");
		bool databaseMoved = false;
		bool keyChanged = false;
		try
		{
			DbConnection liveConnection = context.Database.GetDbConnection();
			if (liveConnection.State != ConnectionState.Closed)
			{
				await liveConnection.CloseAsync();
			}
			// Pooled SQLite connections keep the database file open and block the move below.
			SqliteConnection.ClearAllPools();
			string[] array = new string[2] { "prescriptions", "licences" };
			foreach (string path in array)
			{
				string text = Path.Combine(storage.RootDirectory, path);
				if (Directory.Exists(text))
				{
					string destDirName = Path.Combine(rollbackDirectory, path);
					Directory.Move(text, destDirName);
				}
			}
			if (File.Exists(storage.DatabasePath))
			{
				File.Move(storage.DatabasePath, oldDatabase);
				databaseMoved = true;
			}
			keyProvider.ReplaceKey(newKey);
			keyChanged = true;
			string text2 = Path.Combine(storage.RootDirectory, $"pharmabill-restore-{Guid.NewGuid():N}.tmp");
			try
			{
				File.Copy(stagedDatabase, text2);
				File.Move(text2, storage.DatabasePath);
			}
			finally
			{
				if (File.Exists(text2))
				{
					File.Delete(text2);
				}
			}
			array = new string[2] { "prescriptions", "licences" };
			foreach (string text3 in array)
			{
				string text4 = Path.Combine(stagedDirectory, "attachments", text3);
				if (Directory.Exists(text4))
				{
					Directory.Move(text4, Path.Combine(storage.RootDirectory, text3));
				}
			}
		}
		catch
		{
			// Only remove the file at DatabasePath if the original was moved aside first; otherwise
			// it IS the original live database and deleting it would lose all pharmacy data.
			if (databaseMoved && File.Exists(storage.DatabasePath))
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
			string[] array = new string[2] { "prescriptions", "licences" };
			foreach (string path2 in array)
			{
				string text5 = Path.Combine(storage.RootDirectory, path2);
				string text6 = Path.Combine(rollbackDirectory, path2);
				if (Directory.Exists(text5))
				{
					Directory.Delete(text5, recursive: true);
				}
				if (Directory.Exists(text6))
				{
					Directory.Move(text6, text5);
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

	private async Task EncryptArchiveAsync(string archivePath, string destinationPath, string password, CancellationToken cancellationToken)
	{
		string fullDestination = Path.GetFullPath(destinationPath);
		Directory.CreateDirectory(Path.GetDirectoryName(fullDestination));
		string temporaryPath = $"{fullDestination}.{Guid.NewGuid():N}.tmp";
		byte[] salt = RandomNumberGenerator.GetBytes(16);
		byte[] iv = RandomNumberGenerator.GetBytes(16);
		byte[] keyMaterial = Rfc2898DeriveBytes.Pbkdf2(password, salt, 600000, HashAlgorithmName.SHA256, 64);
		try
		{
			await using (FileStream output = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 131072, useAsync: true))
			{
				await output.WriteAsync(Magic, cancellationToken);
				await output.WriteAsync(BitConverter.GetBytes(600000), cancellationToken);
				await output.WriteAsync(salt, cancellationToken);
				await output.WriteAsync(iv, cancellationToken);
				using Aes aes = Aes.Create();
				aes.KeySize = 256;
				aes.Mode = CipherMode.CBC;
				aes.Padding = PaddingMode.PKCS7;
				aes.Key = keyMaterial[..32];
				aes.IV = iv;
				await using CryptoStream crypto = new CryptoStream(output, aes.CreateEncryptor(), CryptoStreamMode.Write, leaveOpen: true);
				await using FileStream input = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read, 131072, useAsync: true);
				await input.CopyToAsync(crypto, cancellationToken);
				await crypto.FlushFinalBlockAsync(cancellationToken);
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

	private async Task DecryptArchiveAsync(string backupPath, string archivePath, string password, CancellationToken cancellationToken)
	{
		await using FileStream input = new FileStream(backupPath, FileMode.Open, FileAccess.Read, FileShare.Read, 131072, useAsync: true);
		byte[] header = new byte[Magic.Length + 4 + 16 + 16];
		if (await input.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, cancellationToken) != header.Length || !((ReadOnlySpan<byte>)header.AsSpan(0, Magic.Length)).SequenceEqual((ReadOnlySpan<byte>)Magic))
		{
			throw new InvalidDataException("The selected file is not a PharmaBill backup.");
		}
		int num = BitConverter.ToInt32(header, Magic.Length);
		if ((num < 100000 || num > 2000000) ? true : false)
		{
			throw new InvalidDataException("Backup encryption parameters are unsupported.");
		}
		int num2 = Magic.Length + 4;
		byte[] salt = header.AsSpan(num2, 16).ToArray();
		byte[] iv = header.AsSpan(num2 + 16, 16).ToArray();
		long fileLength = input.Length;
		long macLength = 32L;
		if (fileLength <= header.Length + macLength)
		{
			throw new InvalidDataException("Backup file is truncated.");
		}
		byte[] keyMaterial = Rfc2898DeriveBytes.Pbkdf2(password, salt, num, HashAlgorithmName.SHA256, 64);
		try
		{
			using HMACSHA256 hmac = new HMACSHA256(keyMaterial[32..]);
			input.Position = 0L;
			long remaining = fileLength - macLength;
			byte[] buffer = new byte[131072];
			while (remaining > 0)
			{
				cancellationToken.ThrowIfCancellationRequested();
				int num3 = await input.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), cancellationToken);
				if (num3 == 0)
				{
					throw new InvalidDataException("Backup file is truncated.");
				}
				hmac.TransformBlock(buffer, 0, num3, null, 0);
				remaining -= num3;
			}
			hmac.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
			byte[] expectedMac = hmac.Hash;
			byte[] actualMac = new byte[macLength];
			await input.ReadExactlyAsync(actualMac, cancellationToken);
			if (!CryptographicOperations.FixedTimeEquals(expectedMac, actualMac))
			{
				throw new CryptographicException("Backup password is incorrect or the file was modified.");
			}
			input.Position = header.Length;
			long remaining2 = fileLength - header.Length - macLength;
			using Aes aes = Aes.Create();
			aes.KeySize = 256;
			aes.Mode = CipherMode.CBC;
			aes.Padding = PaddingMode.PKCS7;
			aes.Key = keyMaterial[..32];
			aes.IV = iv;
			await using FileStream output = new FileStream(archivePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 131072, useAsync: true);
			using CryptoStream crypto = new CryptoStream(new BoundedReadStream(input, remaining2), aes.CreateDecryptor(), CryptoStreamMode.Read);
			await crypto.CopyToAsync(output, cancellationToken);
		}
		finally
		{
			CryptographicOperations.ZeroMemory(keyMaterial);
		}
	}

	private static async Task AppendMacAsync(string path, byte[] key, CancellationToken cancellationToken)
	{
		await using FileStream stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None, 131072, useAsync: true);
		using HMACSHA256 hmac = new HMACSHA256(key);
		byte[] buffer = new byte[131072];
		int inputCount;
		while ((inputCount = await stream.ReadAsync(buffer, cancellationToken)) > 0)
		{
			hmac.TransformBlock(buffer, 0, inputCount, null, 0);
		}
		hmac.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
		stream.Position = stream.Length;
		await stream.WriteAsync(hmac.Hash, cancellationToken);
		await stream.FlushAsync(cancellationToken);
	}

	private async Task<(Dictionary<string, long> RowCounts, Dictionary<string, string> Checksums)> ReadIntegritySummaryAsync(string databasePath, byte[] key, CancellationToken cancellationToken)
	{
		string connectionString = new SqliteConnectionStringBuilder
		{
			DataSource = databasePath,
			Mode = SqliteOpenMode.ReadOnly,
			Pooling = false,
			Password = Convert.ToHexString(key)
		}.ToString();
		(Dictionary<string, long> RowCounts, Dictionary<string, string> Checksums) result;
		await using (SqliteConnection connection = new SqliteConnection(connectionString))
		{
			await connection.OpenAsync(cancellationToken);
			await using (SqliteCommand check = connection.CreateCommand())
			{
				check.CommandText = "PRAGMA quick_check;";
				if (!string.Equals(Convert.ToString(await check.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture), "ok", StringComparison.OrdinalIgnoreCase))
				{
					throw new InvalidDataException("The database failed SQLite integrity validation.");
				}
			}
			List<string> tables = new List<string>();
			await using (SqliteCommand check = connection.CreateCommand())
			{
				check.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name;";
				await using SqliteDataReader reader = await check.ExecuteReaderAsync(cancellationToken);
				while (await reader.ReadAsync(cancellationToken))
				{
					tables.Add(reader.GetString(0));
				}
			}
			Dictionary<string, long> counts = new Dictionary<string, long>(StringComparer.Ordinal);
			Dictionary<string, string> checksums = new Dictionary<string, string>(StringComparer.Ordinal);
			foreach (string table in tables)
			{
				await using SqliteCommand check = connection.CreateCommand();
				check.CommandText = "SELECT COUNT(*) FROM " + QuoteIdentifier(table) + ";";
				Dictionary<string, long> dictionary = counts;
				string key2 = table;
				dictionary[key2] = Convert.ToInt64(await check.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
				if (KeyTables.Contains(table, StringComparer.Ordinal))
				{
					Dictionary<string, string> dictionary2 = checksums;
					key2 = table;
					dictionary2[key2] = await ComputeTableChecksumAsync(connection, table, cancellationToken);
				}
			}
			result = (RowCounts: counts, Checksums: checksums);
		}
		return result;
	}

	private async Task RemapAttachmentPathsAsync(string databasePath, byte[] key, IReadOnlyList<string> attachmentPaths, CancellationToken cancellationToken)
	{
		Dictionary<string, string> knownFiles = attachmentPaths.ToDictionary((string item) => Path.GetFileName(item), (string item) => Path.Combine(storage.RootDirectory, item.Replace('/', Path.DirectorySeparatorChar)), StringComparer.OrdinalIgnoreCase);
		if (knownFiles.Count == 0)
		{
			return;
		}
		string connectionString = new SqliteConnectionStringBuilder
		{
			DataSource = databasePath,
			Mode = SqliteOpenMode.ReadWrite,
			Pooling = false,
			Password = Convert.ToHexString(key)
		}.ToString();
		await using SqliteConnection connection = new SqliteConnection(connectionString);
		await connection.OpenAsync(cancellationToken);
		await using DbTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
		await using (SqliteCommand query = connection.CreateCommand())
		{
			query.Transaction = (SqliteTransaction)transaction;
			query.CommandText = "SELECT Id, DocumentPath FROM CustomerLicences WHERE DocumentPath IS NOT NULL;";
			List<(string Id, string Path)> records = new List<(string, string)>();
			await using (SqliteDataReader reader = await query.ExecuteReaderAsync(cancellationToken))
			{
				while (await reader.ReadAsync(cancellationToken))
				{
					string path = reader.GetString(1);
					if (knownFiles.TryGetValue(Path.GetFileName(path), out var value))
					{
						records.Add((reader.GetString(0), value));
					}
				}
			}
			foreach (var item in records)
			{
				await using SqliteCommand update = connection.CreateCommand();
				update.Transaction = (SqliteTransaction)transaction;
				update.CommandText = "UPDATE CustomerLicences SET DocumentPath = $path WHERE Id = $id;";
				update.Parameters.AddWithValue("$path", item.Path);
				update.Parameters.AddWithValue("$id", item.Id);
				await update.ExecuteNonQueryAsync(cancellationToken);
			}
		}
		await using (SqliteCommand query = connection.CreateCommand())
		{
			query.Transaction = (SqliteTransaction)transaction;
			query.CommandText = "SELECT Id, Notes FROM Prescriptions WHERE Notes LIKE 'DocumentPath=%';";
			List<(string Id, string Path)> records = new List<(string, string)>();
			await using (SqliteDataReader reader = await query.ExecuteReaderAsync(cancellationToken))
			{
				while (await reader.ReadAsync(cancellationToken))
				{
					string path2 = reader.GetString(1).Substring("DocumentPath=".Length);
					if (knownFiles.TryGetValue(Path.GetFileName(path2), out var value2))
					{
						records.Add((reader.GetString(0), "DocumentPath=" + value2));
					}
				}
			}
			foreach (var item2 in records)
			{
				await using SqliteCommand update = connection.CreateCommand();
				update.Transaction = (SqliteTransaction)transaction;
				update.CommandText = "UPDATE Prescriptions SET Notes = $notes WHERE Id = $id;";
				update.Parameters.AddWithValue("$notes", item2.Path);
				update.Parameters.AddWithValue("$id", item2.Id);
				await update.ExecuteNonQueryAsync(cancellationToken);
			}
		}
		await transaction.CommitAsync(cancellationToken);
	}

	private static async Task<string> ComputeTableChecksumAsync(SqliteConnection connection, string table, CancellationToken cancellationToken)
	{
		string result;
		await using (SqliteCommand command = connection.CreateCommand())
		{
			command.CommandText = "SELECT * FROM " + QuoteIdentifier(table) + " ORDER BY \"Id\";";
			string text;
			await using (SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
			{
				using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
				byte[] buffer = new byte[8];
				while (await reader.ReadAsync(cancellationToken))
				{
					for (int i = 0; i < reader.FieldCount; i++)
					{
						object value = reader.GetValue(i);
						byte[] array = ((value is DBNull) ? new byte[1] { 255 } : Encoding.UTF8.GetBytes(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty));
						BitConverter.TryWriteBytes(buffer, array.Length);
						hash.AppendData(buffer);
						hash.AppendData(array);
					}
				}
				text = Convert.ToHexString(hash.GetHashAndReset());
			}
			result = text;
		}
		return result;
	}

	private List<(string FullPath, string RelativePath)> GetAttachmentFiles()
	{
		List<(string, string)> list = new List<(string, string)>();
		string[] array = new string[2] { "prescriptions", "licences" };
		foreach (string folderName in array)
		{
			string folder = Path.Combine(storage.RootDirectory, folderName);
			if (Directory.Exists(folder))
			{
				list.AddRange(from path in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
					select (path: path, Path.Combine(folderName, Path.GetRelativePath(folder, path)).Replace(Path.DirectorySeparatorChar, '/')));
			}
		}
		return list;
	}

	private static string NormalizeZipPath(string path)
	{
		if (path.Contains('\\') || Path.IsPathRooted(path) || path.Split('/').Any((string segment) => (segment == ".." || segment == ".") ? true : false))
		{
			throw new InvalidDataException("Backup contains an unsafe path.");
		}
		return path;
	}

	private static string QuoteIdentifier(string identifier)
	{
		return "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
	}

	private static bool DictionaryEquals<T>(IReadOnlyDictionary<string, T> first, IReadOnlyDictionary<string, T> second)
	{
		if (first.Count == second.Count)
		{
			return first.All((KeyValuePair<string, T> item) => second.TryGetValue(item.Key, out T value) && EqualityComparer<T>.Default.Equals(item.Value, value));
		}
		return false;
	}

	private static long DirectorySize(string path)
	{
		return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Sum((string file) => new FileInfo(file).Length);
	}

	private static void TryDeleteDirectory(string path)
	{
		if (Directory.Exists(path))
		{
			Directory.Delete(path, recursive: true);
		}
	}
}
