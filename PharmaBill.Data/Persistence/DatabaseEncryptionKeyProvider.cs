using System;
using System.IO;
using System.Security.Cryptography;

namespace PharmaBill.Data.Persistence;

public sealed class DatabaseEncryptionKeyProvider
{
	private readonly string _keyFilePath;

	public DatabaseEncryptionKeyProvider(string? keyFilePath = null)
	{
		_keyFilePath = keyFilePath ?? AppDataPaths.DatabaseKeyPath;
	}

	public byte[] GetOrCreateKey()
	{
		if (!OperatingSystem.IsWindows())
		{
			throw new PlatformNotSupportedException("The database key is protected using Windows DPAPI.");
		}
		Directory.CreateDirectory(Path.GetDirectoryName(_keyFilePath) ?? throw new InvalidOperationException("The encryption-key path has no parent directory."));
		if (File.Exists(_keyFilePath))
		{
			byte[] encryptedData = File.ReadAllBytes(_keyFilePath);
			try
			{
				byte[] array = ProtectedData.Unprotect(encryptedData, null, DataProtectionScope.CurrentUser);
				if (array.Length != 32)
				{
					throw new CryptographicException("The stored database key has an invalid length.");
				}
				return array;
			}
			catch (CryptographicException innerException)
			{
				throw new InvalidOperationException("The database key could not be decrypted for the current Windows user. The database was not opened.", innerException);
			}
		}
		byte[] bytes = RandomNumberGenerator.GetBytes(32);
		byte[] array2 = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
		try
		{
			using FileStream fileStream = new FileStream(_keyFilePath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
			fileStream.Write(array2);
			fileStream.Flush(flushToDisk: true);
			return bytes;
		}
		catch (IOException) when (File.Exists(_keyFilePath))
		{
			CryptographicOperations.ZeroMemory(bytes);
			return GetOrCreateKey();
		}
	}

	public void ReplaceKey(ReadOnlySpan<byte> key)
	{
		if (!OperatingSystem.IsWindows())
		{
			throw new PlatformNotSupportedException("The database key is protected using Windows DPAPI.");
		}
		if (key.Length != 32)
		{
			throw new ArgumentException("The database key must contain exactly 32 bytes.", "key");
		}
		Directory.CreateDirectory(Path.GetDirectoryName(_keyFilePath) ?? throw new InvalidOperationException("The encryption-key path has no parent directory."));
		string text = $"{_keyFilePath}.{Guid.NewGuid():N}.tmp";
		try
		{
			byte[] array = ProtectedData.Protect(key.ToArray(), null, DataProtectionScope.CurrentUser);
			using (FileStream fileStream = new FileStream(text, FileMode.CreateNew, FileAccess.Write, FileShare.None))
			{
				fileStream.Write(array);
				fileStream.Flush(flushToDisk: true);
			}
			if (File.Exists(_keyFilePath))
			{
				File.Replace(text, _keyFilePath, null);
			}
			else
			{
				File.Move(text, _keyFilePath);
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
