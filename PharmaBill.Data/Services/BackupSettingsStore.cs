using System.Security.Cryptography;
using System.Text.Json;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Data.Services;

public sealed record BackupSettings(
    string? PrimaryFolder,
    string? SecondaryFolder,
    string? Password,
    bool AutomaticEnabled,
    DateTime? LastBackupAtUtc);

public sealed class BackupSettingsStore(DatabaseStorageOptions storage)
{
    private readonly string _settingsPath = Path.Combine(storage.RootDirectory, "backup-settings.dat");

    public BackupSettings Load()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Backup settings are protected with Windows DPAPI.");
        }

        if (!File.Exists(_settingsPath))
        {
            return new BackupSettings(null, null, null, false, null);
        }

        var encrypted = File.ReadAllBytes(_settingsPath);
        try
        {
            var json = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<BackupSettings>(json)
                ?? throw new InvalidDataException("The backup settings are invalid.");
        }
        catch (CryptographicException exception)
        {
            throw new InvalidOperationException("Backup settings could not be decrypted for this Windows user.", exception);
        }
    }

    public void Save(BackupSettings settings)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Backup settings are protected with Windows DPAPI.");
        }

        ArgumentNullException.ThrowIfNull(settings);
        Directory.CreateDirectory(storage.RootDirectory);
        var bytes = ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(settings), null, DataProtectionScope.CurrentUser);
        var temporaryPath = $"{_settingsPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(_settingsPath))
            {
                File.Replace(temporaryPath, _settingsPath, null);
            }
            else
            {
                File.Move(temporaryPath, _settingsPath);
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
