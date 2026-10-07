using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PharmaBill.Data.Persistence;

namespace PharmaBill.App.Services;

public sealed class RecoveryCodeStore(DatabaseStorageOptions storage)
{
	private sealed record RecoveryRecord(string CodeHash);

	private readonly string _path = Path.Combine(storage.RootDirectory, "owner-recovery.dat");

	public async Task<string> CreateAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		if (!OperatingSystem.IsWindows())
		{
			throw new PlatformNotSupportedException("Recovery codes are protected with Windows DPAPI.");
		}
		string code = Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).Insert(6, "-").Insert(13, "-")
			.Insert(20, "-");
		byte[] bytes = ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(new RecoveryRecord(Hash(code))), null, DataProtectionScope.CurrentUser);
		Directory.CreateDirectory(Path.GetDirectoryName(_path));
		string temporary = $"{_path}.{Guid.NewGuid():N}.tmp";
		try
		{
			await File.WriteAllBytesAsync(temporary, bytes, cancellationToken);
			File.Move(temporary, _path, overwrite: true);
			return code;
		}
		finally
		{
			if (File.Exists(temporary))
			{
				File.Delete(temporary);
			}
		}
	}

	public async Task<bool> VerifyAsync(string code, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (!OperatingSystem.IsWindows())
		{
			throw new PlatformNotSupportedException("Recovery codes are protected with Windows DPAPI.");
		}
		if (!File.Exists(_path))
		{
			return false;
		}
		try
		{
			byte[] array = Convert.FromHexString((JsonSerializer.Deserialize<RecoveryRecord>(ProtectedData.Unprotect(await File.ReadAllBytesAsync(_path, cancellationToken), null, DataProtectionScope.CurrentUser)) ?? throw new InvalidDataException("The stored recovery code is empty.")).CodeHash);
			byte[] array2 = Convert.FromHexString(Hash(Normalize(code)));
			return array.Length == array2.Length && CryptographicOperations.FixedTimeEquals(array, array2);
		}
		catch (CryptographicException innerException)
		{
			throw new InvalidOperationException("The stored recovery code could not be verified.", innerException);
		}
	}

	public Task DeleteAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		cancellationToken.ThrowIfCancellationRequested();
		if (File.Exists(_path))
		{
			File.Delete(_path);
		}
		return Task.CompletedTask;
	}

	private static string Hash(string code)
	{
		return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Normalize(code))));
	}

	private static string Normalize(string code)
	{
		return string.Concat(code.Where(char.IsAsciiLetterOrDigit)).ToUpperInvariant();
	}
}
