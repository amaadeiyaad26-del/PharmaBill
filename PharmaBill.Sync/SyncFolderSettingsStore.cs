using System.Security.Cryptography;
using System.Text.Json;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Sync;

public sealed record SyncFolderSettings(
    string FolderPath,
    bool AutoSyncEnabled,
    string Status,
    DateTime? LastSyncAtUtc,
    DateTime? LastOutgoingAtUtc,
    int LastOutgoingPackages,
    int LastIncomingPackages,
    int LastAppliedChanges,
    string? LastError)
{
    public static SyncFolderSettings Default { get; } = new(
        SyncFolderSettingsStore.FindDefaultFolder(),
        false,
        "Idle",
        null,
        null,
        0,
        0,
        0,
        null);
}

public sealed class SyncFolderSettingsStore(DatabaseStorageOptions storage)
{
    private readonly string _settingsPath = Path.Combine(storage.RootDirectory, "sync-folder-settings.dat");
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<SyncFolderSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!OperatingSystem.IsWindows())
            {
                throw new PlatformNotSupportedException("Sync folder settings are protected with Windows DPAPI.");
            }

            if (!File.Exists(_settingsPath))
            {
                return SyncFolderSettings.Default;
            }

            try
            {
                var protectedBytes = await File.ReadAllBytesAsync(_settingsPath, cancellationToken);
                var json = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
                return JsonSerializer.Deserialize<SyncFolderSettings>(json)
                    ?? throw new InvalidDataException("The protected sync folder settings are empty.");
            }
            catch (Exception exception) when (exception is CryptographicException or JsonException)
            {
                throw new InvalidOperationException("The protected sync folder settings could not be read.", exception);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(SyncFolderSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrWhiteSpace(settings.FolderPath);
        if (settings.Status is not ("Idle" or "Syncing" or "Error"))
        {
            throw new ArgumentOutOfRangeException(nameof(settings), "Sync status must be Idle, Syncing or Error.");
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!OperatingSystem.IsWindows())
            {
                throw new PlatformNotSupportedException("Sync folder settings are protected with Windows DPAPI.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
            var protectedBytes = ProtectedData.Protect(
                JsonSerializer.SerializeToUtf8Bytes(settings),
                null,
                DataProtectionScope.CurrentUser);
            var temporary = $"{_settingsPath}.{Guid.NewGuid():N}.tmp";
            try
            {
                await using (var stream = new FileStream(
                                 temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                {
                    await stream.WriteAsync(protectedBytes, cancellationToken);
                    await stream.FlushAsync(cancellationToken);
                }

                if (File.Exists(_settingsPath))
                {
                    File.Replace(temporary, _settingsPath, null);
                }
                else
                {
                    File.Move(temporary, _settingsPath);
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
        finally
        {
            _gate.Release();
        }
    }

    public static string FindDefaultFolder()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var candidates = new[]
        {
            Environment.GetEnvironmentVariable("GoogleDrive"),
            Environment.GetEnvironmentVariable("GOOGLE_DRIVE"),
            Path.Combine(userProfile, "Google Drive", "My Drive"),
            Path.Combine(userProfile, "GoogleDrive", "My Drive"),
            Path.Combine(userProfile, "Google Drive"),
            Path.Combine(userProfile, "GoogleDrive")
        };

        var cloudRoot = candidates.FirstOrDefault(path => !string.IsNullOrWhiteSpace(path) && Directory.Exists(path));
        cloudRoot ??= Path.Combine(userProfile, "Google Drive");
        return Path.Combine(cloudRoot, "PharmaBill_Sync");
    }
}

