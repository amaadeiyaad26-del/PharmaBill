using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Sync;

public sealed class ProtectedPeerKeyStore(DatabaseStorageOptions storage)
{
	private readonly string _path = Path.Combine(storage.RootDirectory, "sync-peer-keys.dat");

	public IReadOnlyDictionary<Guid, byte[]> Load()
	{
		if (!OperatingSystem.IsWindows())
		{
			throw new PlatformNotSupportedException("Peer keys are protected using Windows DPAPI.");
		}
		if (!File.Exists(_path))
		{
			return new Dictionary<Guid, byte[]>();
		}
		try
		{
			return (JsonSerializer.Deserialize<Dictionary<Guid, string>>(ProtectedData.Unprotect(File.ReadAllBytes(_path), null, DataProtectionScope.CurrentUser)) ?? throw new InvalidDataException("The protected peer-key registry is invalid.")).ToDictionary((KeyValuePair<Guid, string> item) => item.Key, (KeyValuePair<Guid, string> item) => Convert.FromBase64String(item.Value));
		}
		catch (Exception ex) when ((ex is CryptographicException || ex is JsonException || ex is FormatException) ? true : false)
		{
			throw new InvalidOperationException("The protected peer-key registry could not be read.", ex);
		}
	}

	public void Save(IReadOnlyDictionary<Guid, byte[]> keys)
	{
		if (!OperatingSystem.IsWindows())
		{
			throw new PlatformNotSupportedException("Peer keys are protected using Windows DPAPI.");
		}
		Directory.CreateDirectory(Path.GetDirectoryName(_path));
		byte[] array = ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(keys.ToDictionary((KeyValuePair<Guid, byte[]> item) => item.Key, (KeyValuePair<Guid, byte[]> item) => Convert.ToBase64String(item.Value))), null, DataProtectionScope.CurrentUser);
		string text = $"{_path}.{Guid.NewGuid():N}.tmp";
		try
		{
			using (FileStream fileStream = new FileStream(text, FileMode.CreateNew, FileAccess.Write, FileShare.None))
			{
				fileStream.Write(array);
				fileStream.Flush(flushToDisk: true);
			}
			if (File.Exists(_path))
			{
				File.Replace(text, _path, null);
			}
			else
			{
				File.Move(text, _path);
			}
		}
		finally
		{
			if (File.Exists(text))
			{
				File.Delete(text);
			}
		}
	}
}
