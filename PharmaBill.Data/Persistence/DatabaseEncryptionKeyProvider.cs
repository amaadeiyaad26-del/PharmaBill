using System.Security.Cryptography;

namespace PharmaBill.Data.Persistence;

public sealed class DatabaseEncryptionKeyProvider
{
    private readonly string _keyFilePath;

    public DatabaseEncryptionKeyProvider(string? keyFilePath = null)
    {
        _keyFilePath = keyFilePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PharmaBill",
            "database.key");
    }

    public byte[] GetOrCreateKey()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("The database key is protected using Windows DPAPI.");
        }

        var directory = Path.GetDirectoryName(_keyFilePath)
            ?? throw new InvalidOperationException("The encryption-key path has no parent directory.");
        Directory.CreateDirectory(directory);

        if (File.Exists(_keyFilePath))
        {
            var protectedKey = File.ReadAllBytes(_keyFilePath);
            try
            {
                var key = ProtectedData.Unprotect(protectedKey, null, DataProtectionScope.CurrentUser);
                if (key.Length != 32)
                {
                    throw new CryptographicException("The stored database key has an invalid length.");
                }

                return key;
            }
            catch (CryptographicException exception)
            {
                throw new InvalidOperationException(
                    "The database key could not be decrypted for the current Windows user. The database was not opened.",
                    exception);
            }
        }

        var generatedKey = RandomNumberGenerator.GetBytes(32);
        var encryptedKey = ProtectedData.Protect(generatedKey, null, DataProtectionScope.CurrentUser);
        try
        {
            using var stream = new FileStream(_keyFilePath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            stream.Write(encryptedKey);
            stream.Flush(flushToDisk: true);
        }
        catch (IOException) when (File.Exists(_keyFilePath))
        {
            CryptographicOperations.ZeroMemory(generatedKey);
            return GetOrCreateKey();
        }

        return generatedKey;
    }

    public void ReplaceKey(ReadOnlySpan<byte> key)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("The database key is protected using Windows DPAPI.");
        }

        if (key.Length != 32)
        {
            throw new ArgumentException("The database key must contain exactly 32 bytes.", nameof(key));
        }

        var directory = Path.GetDirectoryName(_keyFilePath)
            ?? throw new InvalidOperationException("The encryption-key path has no parent directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = $"{_keyFilePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            var protectedKey = ProtectedData.Protect(key.ToArray(), null, DataProtectionScope.CurrentUser);
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(protectedKey);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(_keyFilePath))
            {
                File.Replace(temporaryPath, _keyFilePath, null);
            }
            else
            {
                File.Move(temporaryPath, _keyFilePath);
            }
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
