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
            var json = ProtectedData.Unprotect(File.ReadAllBytes(_path), null, DataProtectionScope.CurrentUser);
            var stored = JsonSerializer.Deserialize<Dictionary<Guid, string>>(json)
                ?? throw new InvalidDataException("The protected peer-key registry is invalid.");
            return stored.ToDictionary(item => item.Key, item => Convert.FromBase64String(item.Value));
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException or FormatException)
        {
            throw new InvalidOperationException("The protected peer-key registry could not be read.", exception);
        }
    }

    public void Save(IReadOnlyDictionary<Guid, byte[]> keys)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Peer keys are protected using Windows DPAPI.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var serialized = keys.ToDictionary(item => item.Key, item => Convert.ToBase64String(item.Value));
        var protectedBytes = ProtectedData.Protect(
            JsonSerializer.SerializeToUtf8Bytes(serialized),
            null,
            DataProtectionScope.CurrentUser);
        var temporary = $"{_path}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(protectedBytes);
                stream.Flush(flushToDisk: true);
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
}
