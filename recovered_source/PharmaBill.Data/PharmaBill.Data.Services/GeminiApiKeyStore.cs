using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace PharmaBill.Data.Services;

public sealed class GeminiApiKeyStore
{
	private readonly string _path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PharmaBill", "settings", "gemini-api-key.dpapi");

	public void Save(string apiKey)
	{
		if (!OperatingSystem.IsWindows())
		{
			throw new PlatformNotSupportedException("Gemini API keys are protected using Windows DPAPI.");
		}
		ArgumentException.ThrowIfNullOrWhiteSpace(apiKey, "apiKey");
		Directory.CreateDirectory(Path.GetDirectoryName(_path) ?? throw new InvalidOperationException("The API-key settings path is invalid."));
		byte[] bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(apiKey.Trim()), null, DataProtectionScope.CurrentUser);
		File.WriteAllBytes(_path, bytes);
	}

	public string? Read()
	{
		if (!OperatingSystem.IsWindows())
		{
			throw new PlatformNotSupportedException("Gemini API keys are protected using Windows DPAPI.");
		}
		if (!File.Exists(_path))
		{
			return null;
		}
		byte[] encryptedData = File.ReadAllBytes(_path);
		return Encoding.UTF8.GetString(ProtectedData.Unprotect(encryptedData, null, DataProtectionScope.CurrentUser));
	}

	public void Delete()
	{
		if (File.Exists(_path))
		{
			File.Delete(_path);
		}
	}
}
