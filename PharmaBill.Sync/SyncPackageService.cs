using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PharmaBill.Core.Entities;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Sync;

public sealed class SyncPackageService(
    PharmaBillDbContext context,
    DatabaseDeviceId localDeviceId,
    SyncDeviceService devices,
    FileTransferQueue files,
    SyncPayloadSerializer serializer,
    ChangeApplier applier,
    DatabaseStorageOptions storage)
{
    private const int ProtocolVersion = 1;
    private const long MaximumPackageSize = 250L * 1024 * 1024;
    private const int EncryptionHeaderLength = 42;
    private const int EncryptionNonceLength = 12;
    private const int EncryptionTagLength = 16;
    private static readonly byte[] EncryptionMagic = Encoding.ASCII.GetBytes("PBENC1");
    private const int MaximumChanges = 50_000;
    private const int MaximumEntries = 50_100;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<int> ExportAsync(
        Guid peerDeviceId,
        string destinationPath,
        CancellationToken cancellationToken = default) =>
        await ExportCoreAsync(peerDeviceId, destinationPath, null, cancellationToken);

    public async Task<int> ExportAsync(
        Guid peerDeviceId,
        string destinationPath,
        IReadOnlyCollection<Guid> changeIds,
        CancellationToken cancellationToken = default) =>
        await ExportCoreAsync(peerDeviceId, destinationPath, changeIds, cancellationToken);

    private async Task<int> ExportCoreAsync(
        Guid peerDeviceId,
        string destinationPath,
        IReadOnlyCollection<Guid>? changeIds,
        CancellationToken cancellationToken)
    {
        if (peerDeviceId == Guid.Empty || peerDeviceId == localDeviceId.Value)
        {
            throw new ArgumentException("Choose a paired device other than this device.", nameof(peerDeviceId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        var peerKey = devices.GetPeerKey(peerDeviceId);
        try
        {
            var selectedIds = changeIds?.ToArray();
            var pendingChanges = context.ChangeLogs.AsNoTracking()
                .Where(change => change.SyncState == SyncState.Pending);
            if (selectedIds is not null)
            {
                pendingChanges = pendingChanges.Where(change => selectedIds.Contains(change.Id));
            }

            var changeLogs = await pendingChanges
                .OrderBy(change => change.ChangedAtUtc)
                .Take(MaximumChanges + 1)
                .ToListAsync(cancellationToken);
            if (changeLogs.Count > MaximumChanges)
            {
                throw new InvalidOperationException($"Export is limited to {MaximumChanges} changes per package.");
            }

            var envelopes = changeLogs.Select(serializer.FromChangeLog)
                .OrderBy(item => item, EnvelopeComparer.Instance)
                .ToArray();
            var changesByPath = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            var changeDescriptors = new List<SyncPackageEntry>(envelopes.Length);
            foreach (var envelope in envelopes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var path = $"changes/{envelope.ChangeId:D}.json";
                var bytes = JsonSerializer.SerializeToUtf8Bytes(envelope, JsonOptions);
                changesByPath.Add(path, bytes);
                changeDescriptors.Add(new SyncPackageEntry(
                    envelope.ChangeId, path, bytes.LongLength, Sha256(bytes)));
            }

            var fileItems = await files.EnsureAttachmentQueueAsync(
                context,
                changeLogs.Select(item => item.Id),
                cancellationToken);
            var includedFiles = fileItems
                .GroupBy(item => (item.AttachmentId, item.Entity, item.EntityId, item.Property))
                .Select(group => group.First())
                .ToArray();
            var packageFiles = new List<SyncPackageFile>(includedFiles.Length);
            var fileContents = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (var item in includedFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!File.Exists(item.LocalPath))
                {
                    await files.SetStateAsync(item.AttachmentId, "Failed", "Source file was not found.", cancellationToken);
                    continue;
                }

                var bytes = await File.ReadAllBytesAsync(item.LocalPath, cancellationToken);
                var hash = Sha256(bytes);
                if (bytes.LongLength != item.Length || !string.Equals(hash, item.Sha256, StringComparison.Ordinal))
                {
                    await files.SetStateAsync(item.AttachmentId, "Failed", "Source file changed after it was hashed.", cancellationToken);
                    throw new InvalidDataException("An attachment changed after being added to the file-transfer queue.");
                }

                var extension = Path.GetExtension(item.LocalPath).ToLowerInvariant();
                var path = $"files/{item.AttachmentId}{extension}";
                fileContents.Add(path, bytes);
                packageFiles.Add(new SyncPackageFile(
                    item.AttachmentId,
                    item.Entity,
                    item.EntityId,
                    item.Property,
                    item.Category,
                    path,
                    item.Length,
                    hash,
                    item.MediaType));
            }

            if (changeDescriptors.Count + packageFiles.Count + 2 > MaximumEntries)
            {
                throw new InvalidOperationException("The sync package exceeds the entry limit.");
            }

            var unsignedManifest = new SyncPackageManifest(
                ProtocolVersion,
                Guid.NewGuid(),
                DateTime.UtcNow,
                localDeviceId.Value,
                peerDeviceId,
                typeof(SyncPackageService).Assembly.GetName().Version?.ToString() ?? "unknown",
                changeDescriptors.Count,
                string.Empty,
                changeDescriptors,
                packageFiles.OrderBy(item => item.Path, StringComparer.Ordinal).ToArray());
            var manifestHash = Sha256(JsonSerializer.SerializeToUtf8Bytes(unsignedManifest, JsonOptions));
            var manifest = unsignedManifest with { ManifestSha256 = manifestHash };
            var manifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOptions);
            var authenticatedEntries = changeDescriptors
                .Select(item => (item.Path, item.Length, item.Sha256))
                .Concat(packageFiles.Select(item => (item.Path, item.Length, item.Sha256)))
                .OrderBy(item => item.Path, StringComparer.Ordinal)
                .ToArray();
            var signature = ComputeSignature(peerKey, manifestBytes, authenticatedEntries);
            var fullDestination = Path.GetFullPath(destinationPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullDestination)!);
            var temporary = $"{fullDestination}.{Guid.NewGuid():N}.tmp";
            try
            {
                await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 131072, true))
                {
                    using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
                    {
                        await WriteEntryAsync(archive, "manifest.json", manifestBytes, cancellationToken);
                        foreach (var content in changesByPath.OrderBy(item => item.Key, StringComparer.Ordinal))
                        {
                            await WriteEntryAsync(archive, content.Key, content.Value, cancellationToken);
                        }

                        foreach (var content in fileContents.OrderBy(item => item.Key, StringComparer.Ordinal))
                        {
                            await WriteEntryAsync(archive, content.Key, content.Value, cancellationToken);
                        }

                        await WriteEntryAsync(archive, "signature.hmac", Encoding.ASCII.GetBytes(signature), cancellationToken);
                    }
                }

                var zipBytes = await File.ReadAllBytesAsync(temporary, cancellationToken);
                var encryptedBytes = EncryptPackage(zipBytes, peerKey, localDeviceId.Value);
                await File.WriteAllBytesAsync(temporary, encryptedBytes, cancellationToken);
                if (new FileInfo(temporary).Length > MaximumPackageSize)
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

            foreach (var item in includedFiles.Where(item => fileContents.Keys.Any(path =>
                         Path.GetFileNameWithoutExtension(path) == item.AttachmentId)))
            {
                await files.SetStateAsync(item.AttachmentId, "Complete", cancellationToken: cancellationToken);
            }

            var peer = await context.DeviceInfos.SingleAsync(item => item.Id == peerDeviceId, cancellationToken);
            peer.LastSyncAtUtc = DateTime.UtcNow;
            await context.SaveChangesAsync(cancellationToken);
            return envelopes.Length;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(peerKey);
        }
    }

    public async Task<SyncImportResult> ImportAsync(
        string packagePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
        if (!File.Exists(packagePath))
        {
            throw new FileNotFoundException("The sync package was not found.", packagePath);
        }

        var encryptedLength = new FileInfo(packagePath).Length;
        if (encryptedLength > MaximumPackageSize + EncryptionHeaderLength + EncryptionNonceLength + EncryptionTagLength)
        {
            throw new InvalidDataException("The sync package exceeds the 250 MB size limit.");
        }

        var staging = Path.Combine(Path.GetTempPath(), $"PharmaBill.Sync.{Guid.NewGuid():N}");
        Directory.CreateDirectory(staging);
        var installedFiles = new List<string>();
        byte[]? peerKey = null;
        try
        {
            var encryptedBytes = await File.ReadAllBytesAsync(packagePath, cancellationToken);
            Guid? headerDeviceId = null;
            byte[] zipBytes;
            if (encryptedBytes.AsSpan(0, Math.Min(encryptedBytes.Length, EncryptionMagic.Length))
                .SequenceEqual(EncryptionMagic))
            {
                if (encryptedBytes.Length < EncryptionHeaderLength + EncryptionNonceLength + EncryptionTagLength)
                {
                    throw new InvalidDataException("The encrypted sync package is incomplete.");
                }

                var exportingDeviceText = Encoding.ASCII.GetString(
                    encryptedBytes,
                    EncryptionMagic.Length,
                    EncryptionHeaderLength - EncryptionMagic.Length);
                if (!Guid.TryParseExact(exportingDeviceText, "D", out var parsedDeviceId) ||
                    exportingDeviceText != parsedDeviceId.ToString("D").ToLowerInvariant())
                {
                    throw new InvalidDataException("The encrypted package has an invalid exporting device ID.");
                }

                headerDeviceId = parsedDeviceId;
                peerKey = devices.GetPeerKey(parsedDeviceId);
                zipBytes = DecryptPackage(encryptedBytes, peerKey, parsedDeviceId);
            }
            else if (encryptedBytes.AsSpan(0, Math.Min(encryptedBytes.Length, 2))
                         .SequenceEqual("PK"u8))
            {
                if (encryptedBytes.LongLength > MaximumPackageSize)
                {
                    throw new InvalidDataException("The legacy sync package exceeds the 250 MB size limit.");
                }

                zipBytes = encryptedBytes;
            }
            else
            {
                throw new InvalidDataException("The sync package is not a supported encrypted or legacy signed package.");
            }

            if (zipBytes.LongLength > MaximumPackageSize)
            {
                throw new InvalidDataException("The decrypted sync package exceeds the 250 MB size limit.");
            }

            using var stream = new MemoryStream(zipBytes, writable: false);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            if (archive.Entries.Count > MaximumEntries)
            {
                throw new InvalidDataException("The sync package contains too many entries.");
            }

            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in archive.Entries)
            {
                ValidateArchivePath(entry.FullName);
                if (!names.Add(entry.FullName) || entry.Length > MaximumPackageSize)
                {
                    throw new InvalidDataException("The sync package contains duplicate or oversized entries.");
                }
            }

            var manifestEntry = archive.GetEntry("manifest.json")
                ?? throw new InvalidDataException("The sync package is missing its manifest.");
            var manifestBytes = await ReadEntryAsync(manifestEntry, 1_000_000, cancellationToken);
            var manifest = JsonSerializer.Deserialize<SyncPackageManifest>(manifestBytes, JsonOptions)
                ?? throw new InvalidDataException("The sync manifest is invalid.");
            ValidateManifest(manifest);
            if (manifest.IntendedDeviceId.HasValue && manifest.IntendedDeviceId.Value != localDeviceId.Value)
            {
                throw new InvalidDataException("This sync package is addressed to a different device.");
            }

            var unsignedManifest = manifest with { ManifestSha256 = string.Empty };
            var expectedManifestHash = Sha256(JsonSerializer.SerializeToUtf8Bytes(unsignedManifest, JsonOptions));
            if (!CryptographicOperations.FixedTimeEquals(
                    Convert.FromHexString(expectedManifestHash),
                    Convert.FromHexString(manifest.ManifestSha256)))
            {
                throw new InvalidDataException("The sync manifest hash is invalid.");
            }

            var allowed = new HashSet<string>(StringComparer.Ordinal) { "manifest.json", "signature.hmac" };
            allowed.UnionWith(manifest.Changes.Select(item => item.Path));
            allowed.UnionWith(manifest.Files.Select(item => item.Path));
            if (!names.SetEquals(allowed))
            {
                throw new InvalidDataException("The sync package contains missing or unexpected entries.");
            }

            if (headerDeviceId.HasValue && manifest.ExportingDeviceId != headerDeviceId.Value)
            {
                throw new InvalidDataException("The encrypted package header and manifest device IDs do not match.");
            }

            peerKey ??= devices.GetPeerKey(manifest.ExportingDeviceId);
            var authenticatedEntries = manifest.Changes
                .Select(item => (item.Path, item.Length, item.Sha256))
                .Concat(manifest.Files.Select(item => (item.Path, item.Length, item.Sha256)))
                .OrderBy(item => item.Path, StringComparer.Ordinal)
                .ToArray();
            var signatureEntry = archive.GetEntry("signature.hmac")!;
            var signatureBytes = await ReadEntryAsync(signatureEntry, 128, cancellationToken);
            var actualSignature = Encoding.ASCII.GetString(signatureBytes);
            var expectedSignature = ComputeSignature(peerKey, manifestBytes, authenticatedEntries);
            byte[] actualSignatureBytes;
            try
            {
                actualSignatureBytes = Convert.FromHexString(actualSignature);
            }
            catch (FormatException exception)
            {
                throw new CryptographicException("The sync package signature is not valid hexadecimal.", exception);
            }

            if (actualSignatureBytes.Length != 32 ||
                !CryptographicOperations.FixedTimeEquals(
                    actualSignatureBytes,
                    Convert.FromHexString(expectedSignature)))
            {
                throw new CryptographicException("The sync package signature is invalid or the paired device key does not match.");
            }

            var changes = new List<SyncChangeEnvelope>(manifest.ChangeCount);
            foreach (var descriptor in manifest.Changes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var entry = archive.GetEntry(descriptor.Path)!;
                var bytes = await ReadEntryAsync(entry, 10_000_000, cancellationToken);
                VerifyEntry(descriptor.Path, descriptor.Length, descriptor.Sha256, bytes);
                var change = JsonSerializer.Deserialize<SyncChangeEnvelope>(bytes, JsonOptions)
                    ?? throw new InvalidDataException($"Change entry '{descriptor.Path}' is invalid.");
                if (change.ChangeId != descriptor.ChangeId ||
                    !string.Equals(descriptor.Path, $"changes/{change.ChangeId:D}.json", StringComparison.Ordinal))
                {
                    throw new InvalidDataException("A change entry path does not match its change ID.");
                }

                changes.Add(change);
            }

            if (changes.Count != manifest.ChangeCount)
            {
                throw new InvalidDataException("The sync manifest change count is incorrect.");
            }

            var attachmentPaths = new Dictionary<(string Entity, Guid EntityId, string Property), string>();
            var copied = new List<string>();
            foreach (var file in manifest.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var entry = archive.GetEntry(file.Path)!;
                var bytes = await ReadEntryAsync(entry, 50_000_000, cancellationToken);
                VerifyEntry(file.Path, file.Length, file.Sha256, bytes);
                if (file.AttachmentId != file.Sha256 ||
                    file.Category is not ("licences" or "prescriptions" or "bills") ||
                    !Path.GetFileName(file.Path).StartsWith(file.AttachmentId, StringComparison.Ordinal))
                {
                    throw new InvalidDataException("A sync attachment reference is invalid.");
                }

                var extension = ExtensionForMediaType(file.MediaType);
                var destinationDirectory = Path.Combine(storage.RootDirectory, file.Category);
                Directory.CreateDirectory(destinationDirectory);
                var destination = Path.Combine(destinationDirectory, $"{file.AttachmentId}{extension}");
                if (!File.Exists(destination))
                {
                    await File.WriteAllBytesAsync(destination, bytes, cancellationToken);
                    copied.Add(destination);
                }

                attachmentPaths[(file.Entity, file.EntityId, file.Property)] = destination;
            }

            try
            {
                var result = await applier.ApplyAsync(changes, attachmentPaths, cancellationToken);
                var exportingDevice = await context.DeviceInfos.SingleOrDefaultAsync(
                    item => item.Id == manifest.ExportingDeviceId,
                    cancellationToken);
                if (exportingDevice is not null)
                {
                    exportingDevice.LastSyncAtUtc = DateTime.UtcNow;
                    await context.SaveChangesAsync(cancellationToken);
                }

                foreach (var file in manifest.Files)
                {
                    await files.QueueAsync(
                        file.Entity,
                        file.EntityId,
                        file.Property,
                        attachmentPaths[(file.Entity, file.EntityId, file.Property)],
                        file.Category,
                        cancellationToken);
                    await files.SetStateAsync(file.AttachmentId, "Complete", cancellationToken: cancellationToken);
                }

                return result;
            }
            catch
            {
                foreach (var path in copied)
                {
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                    }
                }

                throw;
            }
        }
        finally
        {
            if (peerKey is not null)
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
        if (manifest.ProtocolVersion != ProtocolVersion ||
            manifest.PackageId == Guid.Empty ||
            manifest.ExportingDeviceId == Guid.Empty ||
            manifest.ChangeCount != manifest.Changes.Count ||
            manifest.ChangeCount > MaximumChanges ||
            manifest.CreatedAtUtc.Kind != DateTimeKind.Utc ||
            manifest.ManifestSha256.Length != 64 ||
            manifest.Files.Count + manifest.Changes.Count + 2 > MaximumEntries ||
            manifest.Changes.Select(item => item.ChangeId).Distinct().Count() != manifest.Changes.Count ||
            manifest.Changes.Select(item => item.Path).Distinct(StringComparer.Ordinal).Count() != manifest.Changes.Count ||
            manifest.Files.Select(item => item.Path).Distinct(StringComparer.Ordinal).Count() != manifest.Files.Count)
        {
            throw new InvalidDataException("The sync manifest is invalid or uses an unsupported protocol version.");
        }

        foreach (var descriptor in manifest.Changes)
        {
            ValidateHashDescriptor(descriptor.Path, descriptor.Length, descriptor.Sha256, "changes/");
        }

        foreach (var file in manifest.Files)
        {
            ValidateHashDescriptor(file.Path, file.Length, file.Sha256, "files/");
        }
    }

    private static void ValidateHashDescriptor(string path, long length, string sha256, string requiredPrefix)
    {
        ValidateArchivePath(path);
        if (!path.StartsWith(requiredPrefix, StringComparison.Ordinal) ||
            length < 0 || length > MaximumPackageSize ||
            sha256.Length != 64 || !sha256.All(Uri.IsHexDigit))
        {
            throw new InvalidDataException("A sync package entry descriptor is invalid.");
        }
    }

    private static void ValidateArchivePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) ||
            path.Contains('\\') ||
            Path.IsPathRooted(path) ||
            path.Split('/').Any(part => part is "" or "." or ".." || part.Contains(':')))
        {
            throw new InvalidDataException("The sync package contains an unsafe entry path.");
        }
    }

    private static async Task WriteEntryAsync(
        ZipArchive archive,
        string path,
        byte[] bytes,
        CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        await using var output = entry.Open();
        await output.WriteAsync(bytes, cancellationToken);
    }

    private static async Task<byte[]> ReadEntryAsync(
        ZipArchiveEntry entry,
        long maximumLength,
        CancellationToken cancellationToken)
    {
        if (entry.Length > maximumLength)
        {
            throw new InvalidDataException($"Sync package entry '{entry.FullName}' is too large.");
        }

        await using var input = entry.Open();
        using var output = new MemoryStream((int)Math.Min(entry.Length, int.MaxValue));
        await input.CopyToAsync(output, cancellationToken);
        if (output.Length != entry.Length)
        {
            throw new InvalidDataException($"Sync package entry '{entry.FullName}' has an invalid length.");
        }

        return output.ToArray();
    }

    private static string ComputeSignature(
        byte[] key,
        byte[] manifestBytes,
        IReadOnlyList<(string Path, long Length, string Sha256)> entries)
    {
        using var hmac = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA256, key);
        hmac.AppendData(manifestBytes);
        foreach (var entry in entries)
        {
            var descriptor = Encoding.UTF8.GetBytes($"{entry.Path}\n{entry.Length}\n{entry.Sha256.ToLowerInvariant()}\n");
            hmac.AppendData(descriptor);
        }

        return Convert.ToHexString(hmac.GetHashAndReset()).ToLowerInvariant();
    }

    private static byte[] EncryptPackage(byte[] plaintext, byte[] peerKey, Guid exportingDeviceId)
    {
        var header = new byte[EncryptionHeaderLength];
        EncryptionMagic.CopyTo(header, 0);
        Encoding.ASCII.GetBytes(exportingDeviceId.ToString("D").ToLowerInvariant())
            .CopyTo(header, EncryptionMagic.Length);
        var nonce = RandomNumberGenerator.GetBytes(EncryptionNonceLength);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[EncryptionTagLength];
        var encryptionKey = DeriveEncryptionKey(peerKey, exportingDeviceId);
        try
        {
            using var aes = new AesGcm(encryptionKey, EncryptionTagLength);
            aes.Encrypt(nonce, plaintext, ciphertext, tag, header);
            return [.. header, .. nonce, .. tag, .. ciphertext];
        }
        finally
        {
            CryptographicOperations.ZeroMemory(encryptionKey);
        }
    }

    private static byte[] DecryptPackage(byte[] package, byte[] peerKey, Guid exportingDeviceId)
    {
        var header = package.AsSpan(0, EncryptionHeaderLength);
        var nonce = package.AsSpan(EncryptionHeaderLength, EncryptionNonceLength);
        var tag = package.AsSpan(EncryptionHeaderLength + EncryptionNonceLength, EncryptionTagLength);
        var ciphertext = package.AsSpan(EncryptionHeaderLength + EncryptionNonceLength + EncryptionTagLength);
        var plaintext = new byte[ciphertext.Length];
        var encryptionKey = DeriveEncryptionKey(peerKey, exportingDeviceId);
        try
        {
            using var aes = new AesGcm(encryptionKey, EncryptionTagLength);
            aes.Decrypt(nonce, ciphertext, tag, plaintext, header);
            return plaintext;
        }
        catch (CryptographicException exception)
        {
            throw new CryptographicException("The sync package could not be decrypted or authenticated.", exception);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(encryptionKey);
        }
    }

    private static byte[] DeriveEncryptionKey(byte[] peerKey, Guid exportingDeviceId) =>
        HMACSHA256.HashData(
            peerKey,
            Encoding.UTF8.GetBytes($"PharmaBill-sync-package-encryption-v1:{exportingDeviceId:D}"));

    private static void VerifyEntry(string path, long expectedLength, string expectedHash, byte[] bytes)
    {
        if (bytes.LongLength != expectedLength ||
            !string.Equals(Sha256(bytes), expectedHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Sync package entry '{path}' failed its length or SHA-256 check.");
        }
    }

    private static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static string ExtensionForMediaType(string mediaType) => mediaType switch
    {
        "application/pdf" => ".pdf",
        "image/png" => ".png",
        "image/jpeg" => ".jpg",
        "image/bmp" => ".bmp",
        _ => throw new InvalidDataException($"Sync attachment media type '{mediaType}' is not supported.")
    };

    private sealed class EnvelopeComparer : IComparer<SyncChangeEnvelope>
    {
        public static readonly EnvelopeComparer Instance = new();

        public int Compare(SyncChangeEnvelope? x, SyncChangeEnvelope? y)
        {
            if (ReferenceEquals(x, y))
            {
                return 0;
            }

            if (x is null)
            {
                return -1;
            }

            if (y is null)
            {
                return 1;
            }

            var clock = PharmaBill.Core.Sync.HybridLogicalClockState.Compare(x.HlcStamp, y.HlcStamp);
            return clock != 0 ? clock : string.Compare(
                x.ChangeId.ToString("D"), y.ChangeId.ToString("D"), StringComparison.Ordinal);
        }
    }
}
