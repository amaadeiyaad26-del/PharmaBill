using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PharmaBill.Core.Entities;
using PharmaBill.Core.Sync;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Sync;

public sealed class SyncPackageService(PharmaBillDbContext context, DatabaseDeviceId localDeviceId, SyncDeviceService devices, FileTransferQueue files, SyncPayloadSerializer serializer, ChangeApplier applier, DatabaseStorageOptions storage)
{
	private sealed class EnvelopeComparer : IComparer<SyncChangeEnvelope>
	{
		public static readonly EnvelopeComparer Instance = new EnvelopeComparer();

		public int Compare(SyncChangeEnvelope? x, SyncChangeEnvelope? y)
		{
			if ((object)x == y)
			{
				return 0;
			}
			if ((object)x == null)
			{
				return -1;
			}
			if ((object)y == null)
			{
				return 1;
			}
			int num = HybridLogicalClockState.Compare(x.HlcStamp, y.HlcStamp);
			if (num == 0)
			{
				return string.Compare(x.ChangeId.ToString("D"), y.ChangeId.ToString("D"), StringComparison.Ordinal);
			}
			return num;
		}
	}

	private const int ProtocolVersion = 1;

	private const long MaximumPackageSize = 262144000L;

	private const int EncryptionHeaderLength = 42;

	private const int EncryptionNonceLength = 12;

	private const int EncryptionTagLength = 16;

	private static readonly byte[] EncryptionMagic = Encoding.ASCII.GetBytes("PBENC1");

	private const int MaximumChanges = 50000;

	private const int MaximumEntries = 50100;

	private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);

	public async Task<int> ExportAsync(Guid peerDeviceId, string destinationPath, CancellationToken cancellationToken = default(CancellationToken))
	{
		return await ExportCoreAsync(peerDeviceId, destinationPath, null, cancellationToken);
	}

	public async Task<int> ExportAsync(Guid peerDeviceId, string destinationPath, IReadOnlyCollection<Guid> changeIds, CancellationToken cancellationToken = default(CancellationToken))
	{
		return await ExportCoreAsync(peerDeviceId, destinationPath, changeIds, cancellationToken);
	}

	private async Task<int> ExportCoreAsync(Guid peerDeviceId, string destinationPath, IReadOnlyCollection<Guid>? changeIds, CancellationToken cancellationToken)
	{
		if (peerDeviceId == Guid.Empty || peerDeviceId == localDeviceId.Value)
		{
			throw new ArgumentException("Choose a paired device other than this device.", "peerDeviceId");
		}
		ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath, "destinationPath");
		byte[] peerKey = devices.GetPeerKey(peerDeviceId);
		try
		{
			Guid[] selectedIds = changeIds?.ToArray();
			IQueryable<ChangeLog> source = from change in context.ChangeLogs.AsNoTracking()
				where (int)change.SyncState == 0
				select change;
			if (selectedIds != null)
			{
				source = source.Where((ChangeLog change) => selectedIds.Contains(change.Id));
			}
			List<ChangeLog> list = await source.OrderBy((ChangeLog change) => change.ChangedAtUtc).Take(50001).ToListAsync(cancellationToken);
			if (list.Count > 50000)
			{
				throw new InvalidOperationException($"Export is limited to {50000} changes per package.");
			}
			SyncChangeEnvelope[] envelopes = list.Select(serializer.FromChangeLog).OrderBy((SyncChangeEnvelope result) => result, EnvelopeComparer.Instance).ToArray();
			Dictionary<string, byte[]> changesByPath = new Dictionary<string, byte[]>(StringComparer.Ordinal);
			List<SyncPackageEntry> changeDescriptors = new List<SyncPackageEntry>(envelopes.Length);
			SyncChangeEnvelope[] array = envelopes;
			foreach (SyncChangeEnvelope syncChangeEnvelope in array)
			{
				cancellationToken.ThrowIfCancellationRequested();
				string text = $"changes/{syncChangeEnvelope.ChangeId:D}.json";
				byte[] array2 = JsonSerializer.SerializeToUtf8Bytes(syncChangeEnvelope, JsonOptions);
				changesByPath.Add(text, array2);
				changeDescriptors.Add(new SyncPackageEntry(syncChangeEnvelope.ChangeId, text, array2.LongLength, Sha256(array2)));
			}
			FileTransferItem[] includedFiles = (from fileTransferItem in await files.EnsureAttachmentQueueAsync(context, list.Select((ChangeLog changeLog) => changeLog.Id), cancellationToken)
				group fileTransferItem by (AttachmentId: fileTransferItem.AttachmentId, Entity: fileTransferItem.Entity, EntityId: fileTransferItem.EntityId, Property: fileTransferItem.Property) into @group
				select @group.First()).ToArray();
			List<SyncPackageFile> packageFiles = new List<SyncPackageFile>(includedFiles.Length);
			Dictionary<string, byte[]> fileContents = new Dictionary<string, byte[]>(StringComparer.Ordinal);
			FileTransferItem[] array3 = includedFiles;
			foreach (FileTransferItem item in array3)
			{
				cancellationToken.ThrowIfCancellationRequested();
				if (!File.Exists(item.LocalPath))
				{
					await files.SetStateAsync(item.AttachmentId, "Failed", "Source file was not found.", cancellationToken);
					continue;
				}
				byte[] array4 = await File.ReadAllBytesAsync(item.LocalPath, cancellationToken);
				string text2 = Sha256(array4);
				if (array4.LongLength != item.Length || !string.Equals(text2, item.Sha256, StringComparison.Ordinal))
				{
					await files.SetStateAsync(item.AttachmentId, "Failed", "Source file changed after it was hashed.", cancellationToken);
					throw new InvalidDataException("An attachment changed after being added to the file-transfer queue.");
				}
				string text3 = Path.GetExtension(item.LocalPath).ToLowerInvariant();
				string text4 = "files/" + item.AttachmentId + text3;
				fileContents.Add(text4, array4);
				packageFiles.Add(new SyncPackageFile(item.AttachmentId, item.Entity, item.EntityId, item.Property, item.Category, text4, item.Length, text2, item.MediaType));
			}
			if (changeDescriptors.Count + packageFiles.Count + 2 > 50100)
			{
				throw new InvalidOperationException("The sync package exceeds the entry limit.");
			}
			SyncPackageManifest syncPackageManifest = new SyncPackageManifest(1, Guid.NewGuid(), DateTime.UtcNow, localDeviceId.Value, peerDeviceId, typeof(SyncPackageService).Assembly.GetName().Version?.ToString() ?? "unknown", changeDescriptors.Count, string.Empty, changeDescriptors, packageFiles.OrderBy((SyncPackageFile syncPackageFile) => syncPackageFile.Path, StringComparer.Ordinal).ToArray());
			string manifestSha = Sha256(JsonSerializer.SerializeToUtf8Bytes(syncPackageManifest, JsonOptions));
			byte[] array5 = JsonSerializer.SerializeToUtf8Bytes(syncPackageManifest with
			{
				ManifestSha256 = manifestSha
			}, JsonOptions);
			(string, long, string)[] entries = changeDescriptors.Select((SyncPackageEntry syncPackageEntry) => (Path: syncPackageEntry.Path, Length: syncPackageEntry.Length, Sha256: syncPackageEntry.Sha256)).Concat(packageFiles.Select((SyncPackageFile syncPackageFile) => (Path: syncPackageFile.Path, Length: syncPackageFile.Length, Sha256: syncPackageFile.Sha256))).OrderBy(((string Path, long Length, string Sha256) tuple) => tuple.Path, StringComparer.Ordinal)
				.ToArray();
			string signature = ComputeSignature(peerKey, array5, entries);
			string fullDestination = Path.GetFullPath(destinationPath);
			Directory.CreateDirectory(Path.GetDirectoryName(fullDestination));
			string temporary = $"{fullDestination}.{Guid.NewGuid():N}.tmp";
			try
			{
				await using (FileStream stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 131072, useAsync: true))
				{
					using ZipArchive archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);
					await WriteEntryAsync(archive, "manifest.json", array5, cancellationToken);
					foreach (KeyValuePair<string, byte[]> item2 in changesByPath.OrderBy((KeyValuePair<string, byte[]> keyValuePair) => keyValuePair.Key, StringComparer.Ordinal))
					{
						await WriteEntryAsync(archive, item2.Key, item2.Value, cancellationToken);
					}
					foreach (KeyValuePair<string, byte[]> item3 in fileContents.OrderBy((KeyValuePair<string, byte[]> keyValuePair) => keyValuePair.Key, StringComparer.Ordinal))
					{
						await WriteEntryAsync(archive, item3.Key, item3.Value, cancellationToken);
					}
					await WriteEntryAsync(archive, "signature.hmac", Encoding.ASCII.GetBytes(signature), cancellationToken);
				}
				byte[] bytes = EncryptPackage(await File.ReadAllBytesAsync(temporary, cancellationToken), peerKey, localDeviceId.Value);
				await File.WriteAllBytesAsync(temporary, bytes, cancellationToken);
				if (new FileInfo(temporary).Length > 262144000)
				{
					throw new InvalidOperationException("The finished sync package is larger than the 250 MB limit.");
				}
				File.Move(temporary, fullDestination, overwrite: true);
			}
			finally
			{
				if (File.Exists(temporary))
				{
					File.Delete(temporary);
				}
			}
			foreach (FileTransferItem item4 in includedFiles.Where((FileTransferItem fileTransferItem) => fileContents.Keys.Any((string path) => Path.GetFileNameWithoutExtension(path) == fileTransferItem.AttachmentId)))
			{
				await files.SetStateAsync(item4.AttachmentId, "Complete", null, cancellationToken);
			}
			(await context.DeviceInfos.SingleAsync((DeviceInfo deviceInfo) => deviceInfo.Id == peerDeviceId, cancellationToken)).LastSyncAtUtc = DateTime.UtcNow;
			await context.SaveChangesAsync(cancellationToken);
			return envelopes.Length;
		}
		finally
		{
			CryptographicOperations.ZeroMemory(peerKey);
		}
	}

	public async Task<SyncImportResult> ImportAsync(string packagePath, CancellationToken cancellationToken = default(CancellationToken))
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(packagePath, "packagePath");
		if (!File.Exists(packagePath))
		{
			throw new FileNotFoundException("The sync package was not found.", packagePath);
		}
		if (new FileInfo(packagePath).Length > 262144070)
		{
			throw new InvalidDataException("The sync package exceeds the 250 MB size limit.");
		}
		string staging = Path.Combine(Path.GetTempPath(), $"PharmaBill.Sync.{Guid.NewGuid():N}");
		Directory.CreateDirectory(staging);
		new List<string>();
		byte[] peerKey = null;
		try
		{
			byte[] array = await File.ReadAllBytesAsync(packagePath, cancellationToken);
			Guid? headerDeviceId = null;
			byte[] array2;
			if (((ReadOnlySpan<byte>)array.AsSpan(0, Math.Min(array.Length, EncryptionMagic.Length))).SequenceEqual((ReadOnlySpan<byte>)EncryptionMagic))
			{
				if (array.Length < 70)
				{
					throw new InvalidDataException("The encrypted sync package is incomplete.");
				}
				string text = Encoding.ASCII.GetString(array, EncryptionMagic.Length, 42 - EncryptionMagic.Length);
				if (!Guid.TryParseExact(text, "D", out var result) || text != result.ToString("D").ToLowerInvariant())
				{
					throw new InvalidDataException("The encrypted package has an invalid exporting device ID.");
				}
				headerDeviceId = result;
				peerKey = devices.GetPeerKey(result);
				array2 = DecryptPackage(array, peerKey, result);
			}
			else
			{
				if (!((ReadOnlySpan<byte>)array.AsSpan(0, Math.Min(array.Length, 2))).SequenceEqual("PK"u8))
				{
					throw new InvalidDataException("The sync package is not a supported encrypted or legacy signed package.");
				}
				if (array.LongLength > 262144000)
				{
					throw new InvalidDataException("The legacy sync package exceeds the 250 MB size limit.");
				}
				array2 = array;
			}
			if (array2.LongLength > 262144000)
			{
				throw new InvalidDataException("The decrypted sync package exceeds the 250 MB size limit.");
			}
			using MemoryStream stream = new MemoryStream(array2, writable: false);
			using ZipArchive archive = new ZipArchive(stream, ZipArchiveMode.Read);
			if (archive.Entries.Count > 50100)
			{
				throw new InvalidDataException("The sync package contains too many entries.");
			}
			HashSet<string> names = new HashSet<string>(StringComparer.Ordinal);
			foreach (ZipArchiveEntry entry in archive.Entries)
			{
				ValidateArchivePath(entry.FullName);
				if (!names.Add(entry.FullName) || entry.Length > 262144000)
				{
					throw new InvalidDataException("The sync package contains duplicate or oversized entries.");
				}
			}
			byte[] manifestBytes = await ReadEntryAsync(archive.GetEntry("manifest.json") ?? throw new InvalidDataException("The sync package is missing its manifest."), 1000000L, cancellationToken);
			SyncPackageManifest manifest = JsonSerializer.Deserialize<SyncPackageManifest>(manifestBytes, JsonOptions) ?? throw new InvalidDataException("The sync manifest is invalid.");
			ValidateManifest(manifest);
			if (manifest.IntendedDeviceId.HasValue && manifest.IntendedDeviceId.Value != localDeviceId.Value)
			{
				throw new InvalidDataException("This sync package is addressed to a different device.");
			}
			if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(Sha256(JsonSerializer.SerializeToUtf8Bytes(manifest with
			{
				ManifestSha256 = string.Empty
			}, JsonOptions))), Convert.FromHexString(manifest.ManifestSha256)))
			{
				throw new InvalidDataException("The sync manifest hash is invalid.");
			}
			HashSet<string> hashSet = new HashSet<string>(StringComparer.Ordinal) { "manifest.json", "signature.hmac" };
			hashSet.UnionWith(manifest.Changes.Select((SyncPackageEntry item) => item.Path));
			hashSet.UnionWith(manifest.Files.Select((SyncPackageFile item) => item.Path));
			if (!names.SetEquals(hashSet))
			{
				throw new InvalidDataException("The sync package contains missing or unexpected entries.");
			}
			if (headerDeviceId.HasValue && manifest.ExportingDeviceId != headerDeviceId.Value)
			{
				throw new InvalidDataException("The encrypted package header and manifest device IDs do not match.");
			}
			if (peerKey == null)
			{
				peerKey = devices.GetPeerKey(manifest.ExportingDeviceId);
			}
			(string Path, long Length, string Sha256)[] authenticatedEntries = manifest.Changes.Select((SyncPackageEntry item) => (Path: item.Path, Length: item.Length, Sha256: item.Sha256)).Concat(manifest.Files.Select((SyncPackageFile item) => (Path: item.Path, Length: item.Length, Sha256: item.Sha256))).OrderBy(((string Path, long Length, string Sha256) item) => item.Path, StringComparer.Ordinal)
				.ToArray();
			byte[] bytes = await ReadEntryAsync(archive.GetEntry("signature.hmac"), 128L, cancellationToken);
			string s = Encoding.ASCII.GetString(bytes);
			string s2 = ComputeSignature(peerKey, manifestBytes, authenticatedEntries);
			byte[] array3;
			try
			{
				array3 = Convert.FromHexString(s);
			}
			catch (FormatException inner)
			{
				throw new CryptographicException("The sync package signature is not valid hexadecimal.", inner);
			}
			if (array3.Length != 32 || !CryptographicOperations.FixedTimeEquals(array3, Convert.FromHexString(s2)))
			{
				throw new CryptographicException("The sync package signature is invalid or the paired device key does not match.");
			}
			List<SyncChangeEnvelope> changes = new List<SyncChangeEnvelope>(manifest.ChangeCount);
			foreach (SyncPackageEntry descriptor in manifest.Changes)
			{
				cancellationToken.ThrowIfCancellationRequested();
				byte[] array4 = await ReadEntryAsync(archive.GetEntry(descriptor.Path), 10000000L, cancellationToken);
				VerifyEntry(descriptor.Path, descriptor.Length, descriptor.Sha256, array4);
				SyncChangeEnvelope syncChangeEnvelope = JsonSerializer.Deserialize<SyncChangeEnvelope>(array4, JsonOptions) ?? throw new InvalidDataException("Change entry '" + descriptor.Path + "' is invalid.");
				if (syncChangeEnvelope.ChangeId != descriptor.ChangeId || !string.Equals(descriptor.Path, $"changes/{syncChangeEnvelope.ChangeId:D}.json", StringComparison.Ordinal))
				{
					throw new InvalidDataException("A change entry path does not match its change ID.");
				}
				changes.Add(syncChangeEnvelope);
			}
			if (changes.Count != manifest.ChangeCount)
			{
				throw new InvalidDataException("The sync manifest change count is incorrect.");
			}
			Dictionary<(string Entity, Guid EntityId, string Property), string> attachmentPaths = new Dictionary<(string, Guid, string), string>();
			List<string> copied = new List<string>();
			foreach (SyncPackageFile file in manifest.Files)
			{
				cancellationToken.ThrowIfCancellationRequested();
				byte[] bytes2 = await ReadEntryAsync(archive.GetEntry(file.Path), 50000000L, cancellationToken);
				VerifyEntry(file.Path, file.Length, file.Sha256, bytes2);
				bool flag = file.AttachmentId != file.Sha256;
				if (!flag)
				{
					bool flag2;
					switch (file.Category)
					{
					case "licences":
					case "prescriptions":
					case "bills":
						flag2 = true;
						break;
					default:
						flag2 = false;
						break;
					}
					flag = !flag2;
				}
				if (flag || !Path.GetFileName(file.Path).StartsWith(file.AttachmentId, StringComparison.Ordinal))
				{
					throw new InvalidDataException("A sync attachment reference is invalid.");
				}
				string text2 = ExtensionForMediaType(file.MediaType);
				string text3 = Path.Combine(storage.RootDirectory, file.Category);
				Directory.CreateDirectory(text3);
				string destination = Path.Combine(text3, file.AttachmentId + text2);
				if (!File.Exists(destination))
				{
					await File.WriteAllBytesAsync(destination, bytes2, cancellationToken);
					copied.Add(destination);
				}
				attachmentPaths[(file.Entity, file.EntityId, file.Property)] = destination;
			}
			try
			{
				SyncImportResult result2 = await applier.ApplyAsync(changes, attachmentPaths, cancellationToken);
				DeviceInfo deviceInfo = await context.DeviceInfos.SingleOrDefaultAsync((DeviceInfo item) => item.Id == manifest.ExportingDeviceId, cancellationToken);
				if (deviceInfo != null)
				{
					deviceInfo.LastSyncAtUtc = DateTime.UtcNow;
					await context.SaveChangesAsync(cancellationToken);
				}
				foreach (SyncPackageFile file in manifest.Files)
				{
					await files.QueueAsync(file.Entity, file.EntityId, file.Property, attachmentPaths[(file.Entity, file.EntityId, file.Property)], file.Category, cancellationToken);
					await files.SetStateAsync(file.AttachmentId, "Complete", null, cancellationToken);
				}
				return result2;
			}
			catch
			{
				foreach (string item in copied)
				{
					if (File.Exists(item))
					{
						File.Delete(item);
					}
				}
				throw;
			}
		}
		finally
		{
			if (peerKey != null)
			{
				CryptographicOperations.ZeroMemory(peerKey);
			}
			if (Directory.Exists(staging))
			{
				Directory.Delete(staging, recursive: true);
			}
		}
	}

	private static void ValidateManifest(SyncPackageManifest manifest)
	{
		if (manifest.ProtocolVersion != 1 || manifest.PackageId == Guid.Empty || manifest.ExportingDeviceId == Guid.Empty || manifest.ChangeCount != manifest.Changes.Count || manifest.ChangeCount > 50000 || manifest.CreatedAtUtc.Kind != DateTimeKind.Utc || manifest.ManifestSha256.Length != 64 || manifest.Files.Count + manifest.Changes.Count + 2 > 50100 || manifest.Changes.Select((SyncPackageEntry item) => item.ChangeId).Distinct().Count() != manifest.Changes.Count || manifest.Changes.Select((SyncPackageEntry item) => item.Path).Distinct(StringComparer.Ordinal).Count() != manifest.Changes.Count || manifest.Files.Select((SyncPackageFile item) => item.Path).Distinct(StringComparer.Ordinal).Count() != manifest.Files.Count)
		{
			throw new InvalidDataException("The sync manifest is invalid or uses an unsupported protocol version.");
		}
		foreach (SyncPackageEntry change in manifest.Changes)
		{
			ValidateHashDescriptor(change.Path, change.Length, change.Sha256, "changes/");
		}
		foreach (SyncPackageFile file in manifest.Files)
		{
			ValidateHashDescriptor(file.Path, file.Length, file.Sha256, "files/");
		}
	}

	private static void ValidateHashDescriptor(string path, long length, string sha256, string requiredPrefix)
	{
		ValidateArchivePath(path);
		if (!path.StartsWith(requiredPrefix, StringComparison.Ordinal) || length < 0 || length > 262144000 || sha256.Length != 64 || !sha256.All(Uri.IsHexDigit))
		{
			throw new InvalidDataException("A sync package entry descriptor is invalid.");
		}
	}

	private static void ValidateArchivePath(string path)
	{
		if (string.IsNullOrWhiteSpace(path) || path.Contains('\\') || Path.IsPathRooted(path) || path.Split('/').Any((string part) => (((part != null && part.Length == 0) || part == "." || part == "..") ? true : false) || part.Contains(':')))
		{
			throw new InvalidDataException("The sync package contains an unsafe entry path.");
		}
	}

	private static async Task WriteEntryAsync(ZipArchive archive, string path, byte[] bytes, CancellationToken cancellationToken)
	{
		ZipArchiveEntry zipArchiveEntry = archive.CreateEntry(path, CompressionLevel.Optimal);
		await using Stream output = zipArchiveEntry.Open();
		await output.WriteAsync(bytes, cancellationToken);
	}

	private static async Task<byte[]> ReadEntryAsync(ZipArchiveEntry entry, long maximumLength, CancellationToken cancellationToken)
	{
		if (entry.Length > maximumLength)
		{
			throw new InvalidDataException("Sync package entry '" + entry.FullName + "' is too large.");
		}
		byte[] result;
		await using (Stream input = entry.Open())
		{
			using MemoryStream output = new MemoryStream((int)Math.Min(entry.Length, 2147483647L));
			await input.CopyToAsync(output, cancellationToken);
			if (output.Length != entry.Length)
			{
				throw new InvalidDataException("Sync package entry '" + entry.FullName + "' has an invalid length.");
			}
			result = output.ToArray();
		}
		return result;
	}

	private static string ComputeSignature(byte[] key, byte[] manifestBytes, IReadOnlyList<(string Path, long Length, string Sha256)> entries)
	{
		using IncrementalHash incrementalHash = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA256, key);
		incrementalHash.AppendData(manifestBytes);
		foreach (var entry in entries)
		{
			byte[] bytes = Encoding.UTF8.GetBytes($"{entry.Path}\n{entry.Length}\n{entry.Sha256.ToLowerInvariant()}\n");
			incrementalHash.AppendData(bytes);
		}
		return Convert.ToHexString(incrementalHash.GetHashAndReset()).ToLowerInvariant();
	}

	private static byte[] EncryptPackage(byte[] plaintext, byte[] peerKey, Guid exportingDeviceId)
	{
		byte[] array = new byte[42];
		EncryptionMagic.CopyTo(array, 0);
		Encoding.ASCII.GetBytes(exportingDeviceId.ToString("D").ToLowerInvariant()).CopyTo(array, EncryptionMagic.Length);
		byte[] bytes = RandomNumberGenerator.GetBytes(12);
		byte[] array2 = new byte[plaintext.Length];
		byte[] array3 = new byte[16];
		byte[] array4 = DeriveEncryptionKey(peerKey, exportingDeviceId);
		try
		{
			using AesGcm aesGcm = new AesGcm(array4, 16);
			aesGcm.Encrypt(bytes, plaintext, array2, array3, array);
			List<byte> list = new List<byte>();
			list.AddRange(array);
			list.AddRange(bytes);
			list.AddRange(array3);
			list.AddRange(array2);
			return list.ToArray();
		}
		finally
		{
			CryptographicOperations.ZeroMemory(array4);
		}
	}

	private static byte[] DecryptPackage(byte[] package, byte[] peerKey, Guid exportingDeviceId)
	{
		Span<byte> span = package.AsSpan(0, 42);
		Span<byte> span2 = package.AsSpan(42, 12);
		Span<byte> span3 = package.AsSpan(54, 16);
		Span<byte> span4 = package.AsSpan(70);
		byte[] array = new byte[span4.Length];
		byte[] array2 = DeriveEncryptionKey(peerKey, exportingDeviceId);
		try
		{
			using AesGcm aesGcm = new AesGcm(array2, 16);
			aesGcm.Decrypt(span2, span4, span3, array, span);
			return array;
		}
		catch (CryptographicException inner)
		{
			throw new CryptographicException("The sync package could not be decrypted or authenticated.", inner);
		}
		finally
		{
			CryptographicOperations.ZeroMemory(array2);
		}
	}

	private static byte[] DeriveEncryptionKey(byte[] peerKey, Guid exportingDeviceId)
	{
		return HMACSHA256.HashData(peerKey, Encoding.UTF8.GetBytes($"PharmaBill-sync-package-encryption-v1:{exportingDeviceId:D}"));
	}

	private static void VerifyEntry(string path, long expectedLength, string expectedHash, byte[] bytes)
	{
		if (bytes.LongLength != expectedLength || !string.Equals(Sha256(bytes), expectedHash, StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidDataException("Sync package entry '" + path + "' failed its length or SHA-256 check.");
		}
	}

	private static string Sha256(byte[] bytes)
	{
		return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
	}

	private static string ExtensionForMediaType(string mediaType)
	{
		return mediaType switch
		{
			"application/pdf" => ".pdf", 
			"image/png" => ".png", 
			"image/jpeg" => ".jpg", 
			"image/bmp" => ".bmp", 
			_ => throw new InvalidDataException("Sync attachment media type '" + mediaType + "' is not supported."), 
		};
	}
}
