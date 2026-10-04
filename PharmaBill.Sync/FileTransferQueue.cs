using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Sync;

public sealed record FileTransferItem(
    string AttachmentId,
    string Entity,
    Guid EntityId,
    string Property,
    string LocalPath,
    string Category,
    long Length,
    string Sha256,
    string MediaType,
    Guid SourceDeviceId,
    string State,
    int RetryCount,
    string? LastError);

public sealed class FileTransferQueue(DatabaseStorageOptions storage, DatabaseDeviceId deviceId)
{
    private readonly string _path = Path.Combine(storage.RootDirectory, "sync-file-queue.dat");

    public async Task<FileTransferItem> QueueAsync(
        string entity,
        Guid entityId,
        string property,
        string localPath,
        string category,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(localPath))
        {
            throw new FileNotFoundException("The attachment does not exist.", localPath);
        }

        var fullPath = Path.GetFullPath(localPath);
        var info = new FileInfo(fullPath);
        await using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
        var mediaType = Path.GetExtension(fullPath).ToLowerInvariant() switch
        {
            ".pdf" => "application/pdf",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".bmp" => "image/bmp",
            _ => "application/octet-stream"
        };
        var item = new FileTransferItem(
            hash,
            entity,
            entityId,
            property,
            fullPath,
            category,
            info.Length,
            hash,
            mediaType,
            deviceId.Value,
            "Pending",
            0,
            null);
        var items = await LoadAsync(cancellationToken);
        if (!items.Any(current => current.AttachmentId == item.AttachmentId &&
                                  current.Entity == item.Entity &&
                                  current.EntityId == item.EntityId &&
                                  current.Property == item.Property))
        {
            items.Add(item);
            await SaveAsync(items, cancellationToken);
        }

        return item;
    }

    public async Task<IReadOnlyList<FileTransferItem>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await LoadAsync(cancellationToken);

    public async Task SetStateAsync(
        string attachmentId,
        string state,
        string? error = null,
        CancellationToken cancellationToken = default)
    {
        if (state is not ("Pending" or "InProgress" or "Complete" or "Failed"))
        {
            throw new ArgumentOutOfRangeException(nameof(state));
        }

        var items = await LoadAsync(cancellationToken);
        for (var index = 0; index < items.Count; index++)
        {
            if (items[index].AttachmentId == attachmentId)
            {
                var item = items[index];
                items[index] = item with
                {
                    State = state,
                    RetryCount = state == "Failed" ? item.RetryCount + 1 : item.RetryCount,
                    LastError = error
                };
            }
        }

        await SaveAsync(items, cancellationToken);
    }

    public async Task<IReadOnlyList<FileTransferItem>> EnsureAttachmentQueueAsync(
        PharmaBillDbContext context,
        IEnumerable<Guid> includedChangeIds,
        CancellationToken cancellationToken = default)
    {
        var changes = await context.ChangeLogs.AsNoTracking()
            .Where(change => includedChangeIds.Contains(change.Id))
            .ToListAsync(cancellationToken);
        foreach (var change in changes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var payload = JsonDocument.Parse(change.Payload);
                var root = payload.RootElement;
                if (change.EntityName is "LicenceRecord" or "CustomerLicence")
                {
                    if (TryReadString(root, "DocumentPath", out var documentPath))
                    {
                        await QueueAsync(change.EntityName, change.EntityId, "DocumentPath", documentPath,
                            "licences", cancellationToken);
                    }
                }
                else if (change.EntityName == "Prescription" &&
                         TryReadString(root, "Notes", out var notes) &&
                         notes.StartsWith("DocumentPath=", StringComparison.Ordinal))
                {
                    await QueueAsync("Prescription", change.EntityId, "Notes",
                        notes["DocumentPath=".Length..], "prescriptions", cancellationToken);
                }
                else if (change.EntityName is "PurchaseInvoice" or "WholesaleInvoice" &&
                         TryReadString(root, "Notes", out var invoiceNotes) &&
                         invoiceNotes.StartsWith("DocumentPath=", StringComparison.Ordinal))
                {
                    await QueueAsync(change.EntityName, change.EntityId, "Notes",
                        invoiceNotes["DocumentPath=".Length..], "bills", cancellationToken);
                }
            }
            catch (JsonException)
            {
                throw new InvalidDataException($"Change {change.Id:D} contains an invalid attachment reference payload.");
            }
        }

        var queued = await LoadAsync(cancellationToken);
        return queued.Where(item => changes.Any(change =>
                change.EntityName == item.Entity && change.EntityId == item.EntityId) &&
            (item.State is "Pending" or "Failed"))
            .ToArray();
    }

    private async Task<List<FileTransferItem>> LoadAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("The file transfer queue is protected with Windows DPAPI.");
        }

        if (!File.Exists(_path))
        {
            return [];
        }

        try
        {
            var bytes = await File.ReadAllBytesAsync(_path, cancellationToken);
            var json = ProtectedData.Unprotect(bytes, null, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<List<FileTransferItem>>(json) ?? [];
        }
        catch (CryptographicException exception)
        {
            throw new InvalidOperationException("The protected file transfer queue could not be read.", exception);
        }
    }

    private async Task SaveAsync(List<FileTransferItem> items, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("The file transfer queue is protected with Windows DPAPI.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var bytes = ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(items), null, DataProtectionScope.CurrentUser);
        var temporary = $"{_path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await stream.WriteAsync(bytes, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            if (File.Exists(_path))
            {
                File.Replace(temporary, _path, null);
            }
            else
            {
                File.Move(temporary, _path);
            }
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static bool TryReadString(JsonElement payload, string name, out string value)
    {
        if (payload.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String)
        {
            value = property.GetString()!;
            return !string.IsNullOrWhiteSpace(value);
        }

        value = string.Empty;
        return false;
    }
}
