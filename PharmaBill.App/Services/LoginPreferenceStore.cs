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
            var bytes = ProtectedData.Unprotect(File.ReadAllBytes(_path), null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch (CryptographicException exception)
        {
            throw new InvalidOperationException("The saved login name could not be read.", exception);
        }
    }

    public void Save(string username)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Login preferences are protected with Windows DPAPI.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(username), null, DataProtectionScope.CurrentUser);
        var temporary = $"{_path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllBytes(temporary, bytes);
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
}
