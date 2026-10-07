using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PharmaBill.Core.Entities;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Sync;

public sealed class FileTransferQueue(DatabaseStorageOptions storage, DatabaseDeviceId deviceId)
{
	private readonly string _path = Path.Combine(storage.RootDirectory, "sync-file-queue.dat");

	public async Task<FileTransferItem> QueueAsync(string entity, Guid entityId, string property, string localPath, string category, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (!File.Exists(localPath))
		{
			throw new FileNotFoundException("The attachment does not exist.", localPath);
		}
		string fullPath = Path.GetFullPath(localPath);
		FileInfo info = new FileInfo(fullPath);
		FileTransferItem result;
		await using (FileStream stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true))
		{
			string text = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
			string text2;
			switch (Path.GetExtension(fullPath).ToLowerInvariant())
			{
			case ".pdf":
				text2 = "application/pdf";
				break;
			case ".png":
				text2 = "image/png";
				break;
			case ".jpg":
			case ".jpeg":
				text2 = "image/jpeg";
				break;
			case ".bmp":
				text2 = "image/bmp";
				break;
			default:
				text2 = "application/octet-stream";
				break;
			}
			string mediaType = text2;
			FileTransferItem item = new FileTransferItem(text, entity, entityId, property, fullPath, category, info.Length, text, mediaType, deviceId.Value, "Pending", 0, null);
			List<FileTransferItem> list = await LoadAsync(cancellationToken);
			if (!list.Any((FileTransferItem current) => current.AttachmentId == item.AttachmentId && current.Entity == item.Entity && current.EntityId == item.EntityId && current.Property == item.Property))
			{
				list.Add(item);
				await SaveAsync(list, cancellationToken);
			}
			result = item;
		}
		return result;
	}

	public async Task<IReadOnlyList<FileTransferItem>> GetAllAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		return await LoadAsync(cancellationToken);
	}

	public async Task SetStateAsync(string attachmentId, string state, string? error = null, CancellationToken cancellationToken = default(CancellationToken))
	{
		bool flag;
		switch (state)
		{
		case "Pending":
		case "InProgress":
		case "Complete":
		case "Failed":
			flag = true;
			break;
		default:
			flag = false;
			break;
		}
		if (!flag)
		{
			throw new ArgumentOutOfRangeException("state");
		}
		List<FileTransferItem> list = await LoadAsync(cancellationToken);
		for (int i = 0; i < list.Count; i++)
		{
			if (list[i].AttachmentId == attachmentId)
			{
				FileTransferItem fileTransferItem = list[i];
				list[i] = fileTransferItem with
				{
					State = state,
					RetryCount = ((state == "Failed") ? (fileTransferItem.RetryCount + 1) : fileTransferItem.RetryCount),
					LastError = error
				};
			}
		}
		await SaveAsync(list, cancellationToken);
	}

	public async Task<IReadOnlyList<FileTransferItem>> EnsureAttachmentQueueAsync(PharmaBillDbContext context, IEnumerable<Guid> includedChangeIds, CancellationToken cancellationToken = default(CancellationToken))
	{
		List<ChangeLog> changes = await (from changeLog in context.ChangeLogs.AsNoTracking()
			where includedChangeIds.Contains(changeLog.Id)
			select changeLog).ToListAsync(cancellationToken);
		foreach (ChangeLog change in changes)
		{
			cancellationToken.ThrowIfCancellationRequested();
			try
			{
				using JsonDocument payload = JsonDocument.Parse(change.Payload);
				JsonElement rootElement = payload.RootElement;
				string entityName = change.EntityName;
				if ((entityName == "LicenceRecord" || entityName == "CustomerLicence") ? true : false)
				{
					if (TryReadString(rootElement, "DocumentPath", out string value))
					{
						await QueueAsync(change.EntityName, change.EntityId, "DocumentPath", value, "licences", cancellationToken);
					}
					continue;
				}
				if (change.EntityName == "Prescription" && TryReadString(rootElement, "Notes", out string value2) && value2.StartsWith("DocumentPath=", StringComparison.Ordinal))
				{
					await QueueAsync("Prescription", change.EntityId, "Notes", value2.Substring("DocumentPath=".Length), "prescriptions", cancellationToken);
					continue;
				}
				entityName = change.EntityName;
				bool flag = ((entityName == "PurchaseInvoice" || entityName == "WholesaleInvoice") ? true : false);
				if (flag && TryReadString(rootElement, "Notes", out string value3) && value3.StartsWith("DocumentPath=", StringComparison.Ordinal))
				{
					await QueueAsync(change.EntityName, change.EntityId, "Notes", value3.Substring("DocumentPath=".Length), "bills", cancellationToken);
				}
			}
			catch (JsonException)
			{
				throw new InvalidDataException($"Change {change.Id:D} contains an invalid attachment reference payload.");
			}
		}
		return (await LoadAsync(cancellationToken)).Where((FileTransferItem item) =>
		{
			bool flag2 = changes.Any((ChangeLog changeLog) => changeLog.EntityName == item.Entity && changeLog.EntityId == item.EntityId);
			if (flag2)
			{
				string state = item.State;
				bool flag3 = ((state == "Pending" || state == "Failed") ? true : false);
				flag2 = flag3;
			}
			return flag2;
		}).ToArray();
	}

	private async Task<List<FileTransferItem>> LoadAsync(CancellationToken cancellationToken)
	{
		if (!OperatingSystem.IsWindows())
		{
			throw new PlatformNotSupportedException("The file transfer queue is protected with Windows DPAPI.");
		}
		if (!File.Exists(_path))
		{
			return new List<FileTransferItem>();
		}
		try
		{
			return JsonSerializer.Deserialize<List<FileTransferItem>>(ProtectedData.Unprotect(await File.ReadAllBytesAsync(_path, cancellationToken), null, DataProtectionScope.CurrentUser)) ?? new List<FileTransferItem>();
		}
		catch (CryptographicException innerException)
		{
			throw new InvalidOperationException("The protected file transfer queue could not be read.", innerException);
		}
	}

	private async Task SaveAsync(List<FileTransferItem> items, CancellationToken cancellationToken)
	{
		if (!OperatingSystem.IsWindows())
		{
			throw new PlatformNotSupportedException("The file transfer queue is protected with Windows DPAPI.");
		}
		Directory.CreateDirectory(Path.GetDirectoryName(_path));
		byte[] array = ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(items), null, DataProtectionScope.CurrentUser);
		string temporary = $"{_path}.{Guid.NewGuid():N}.tmp";
		try
		{
			await using (FileStream stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
			{
				await stream.WriteAsync(array, cancellationToken);
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
		if (payload.TryGetProperty(name, out var value2) && value2.ValueKind == JsonValueKind.String)
		{
			value = value2.GetString();
			return !string.IsNullOrWhiteSpace(value);
		}
		value = string.Empty;
		return false;
	}
}
