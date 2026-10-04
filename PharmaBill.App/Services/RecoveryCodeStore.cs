using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PharmaBill.Data.Persistence;

namespace PharmaBill.App.Services;

public sealed class RecoveryCodeStore(DatabaseStorageOptions storage)
{
    private readonly string _path = Path.Combine(storage.RootDirectory, "owner-recovery.dat");

    public async Task<string> CreateAsync(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Recovery codes are protected with Windows DPAPI.");
        }

        var code = Convert.ToHexString(RandomNumberGenerator.GetBytes(12))
            .Insert(6, "-")
            .Insert(13, "-")
            .Insert(20, "-");
        var protectedBytes = ProtectedData.Protect(
            JsonSerializer.SerializeToUtf8Bytes(new RecoveryRecord(Hash(code))),
            null,
            DataProtectionScope.CurrentUser);
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporary = $"{_path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllBytesAsync(temporary, protectedBytes, cancellationToken);
            File.Move(temporary, _path, true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }

        return code;
    }

    public async Task<bool> VerifyAsync(string code, CancellationToken cancellationToken = default)
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
            var bytes = await File.ReadAllBytesAsync(_path, cancellationToken);
            var plain = ProtectedData.Unprotect(bytes, null, DataProtectionScope.CurrentUser);
            var record = JsonSerializer.Deserialize<RecoveryRecord>(plain)
                         ?? throw new InvalidDataException("The stored recovery code is empty.");
            var expected = Convert.FromHexString(record.CodeHash);
            var supplied = Convert.FromHexString(Hash(Normalize(code)));
            return expected.Length == supplied.Length &&
                   CryptographicOperations.FixedTimeEquals(expected, supplied);
        }
        catch (CryptographicException exception)
        {
            throw new InvalidOperationException("The stored recovery code could not be verified.", exception);
        }
    }

    public Task DeleteAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }

        return Task.CompletedTask;
    }

    private static string Hash(string code) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Normalize(code))));

    private static string Normalize(string code) =>
        string.Concat(code.Where(char.IsAsciiLetterOrDigit)).ToUpperInvariant();

    private sealed record RecoveryRecord(string CodeHash);
}
