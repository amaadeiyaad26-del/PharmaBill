using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using PharmaBill.Data.Persistence;

namespace PharmaBill.App.Services;

public sealed class LoginPreferenceStore(DatabaseStorageOptions storage)
{
	private readonly string _path = Path.Combine(storage.RootDirectory, "login-username.dat");

	public string Load()
	{
		if (!OperatingSystem.IsWindows())
		{
			throw new PlatformNotSupportedException("Login preferences are protected with Windows DPAPI.");
		}
		if (!File.Exists(_path))
		{
			return string.Empty;
		}
		try
		{
			byte[] bytes = ProtectedData.Unprotect(File.ReadAllBytes(_path), null, DataProtectionScope.CurrentUser);
			return Encoding.UTF8.GetString(bytes);
		}
		catch (CryptographicException innerException)
		{
			throw new InvalidOperationException("The saved login name could not be read.", innerException);
		}
	}

	public void Save(string username)
	{
		if (!OperatingSystem.IsWindows())
		{
			throw new PlatformNotSupportedException("Login preferences are protected with Windows DPAPI.");
		}
		Directory.CreateDirectory(Path.GetDirectoryName(_path));
		byte[] bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(username), null, DataProtectionScope.CurrentUser);
		string text = $"{_path}.{Guid.NewGuid():N}.tmp";
		try
		{
			File.WriteAllBytes(text, bytes);
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
