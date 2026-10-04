using System.Security.Cryptography;
using System.Text.Json;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Sync;

public sealed record CloudSyncSettings(
    bool Enabled,
    string ServerUrl,
    string ApiKey,
    string Status,
    string PullCursor,
    DateTime PushCursorUtc,
    DateTime? LastPushAtUtc,
    DateTime? LastPullAtUtc,
    int LastPushedChanges,
    int LastPulledChanges,
    string? LastError)
{
    public static CloudSyncSettings Default { get; } = new(
        false, string.Empty, string.Empty, "Idle", string.Empty, DateTime.MinValue, null, null, 0, 0, null);

    public bool IsConfigured =>
        Uri.TryCreate(ServerUrl, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttps || uri.IsLoopback) &&
        TryReadBranchId(out _);

    // API keys look like pbk_<branchId:N>_<secret>.
    public bool TryReadBranchId(out Guid branchId)
    {
        branchId = Guid.Empty;
        var parts = ApiKey.Split('_', 3);
        return parts.Length == 3 && parts[0] == "pbk" && Guid.TryParseExact(parts[1], "N", out branchId);
    }
}

public sealed class CloudSyncSettingsStore(DatabaseStorageOptions storage)
{
    private readonly string _path = Path.Combine(storage.RootDirectory, "cloud-sync-settings.dat");
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<CloudSyncSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!OperatingSystem.IsWindows())
                throw new PlatformNotSupportedException("Cloud sync settings are protected with Windows DPAPI.");
            if (!File.Exists(_path)) return CloudSyncSettings.Default;
            try
            {
                var bytes = await File.ReadAllBytesAsync(_path, cancellationToken);
                var json = ProtectedData.Unprotect(bytes, null, DataProtectionScope.CurrentUser);
                return JsonSerializer.Deserialize<CloudSyncSettings>(json)
                       ?? throw new InvalidDataException("The cloud sync settings are empty.");
            }
            catch (Exception e) when (e is CryptographicException or JsonException)
            {
                throw new InvalidOperationException("The protected cloud sync settings could not be read.", e);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(CloudSyncSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.Status is not ("Idle" or "Syncing" or "Error"))
            throw new ArgumentOutOfRangeException(nameof(settings), "Status must be Idle, Syncing or Error.");

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!OperatingSystem.IsWindows())
                throw new PlatformNotSupportedException("Cloud sync settings are protected with Windows DPAPI.");
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var bytes = ProtectedData.Protect(
                JsonSerializer.SerializeToUtf8Bytes(settings), null, DataProtectionScope.CurrentUser);
            var temp = $"{_path}.{Guid.NewGuid():N}.tmp";
            try
            {
                await File.WriteAllBytesAsync(temp, bytes, cancellationToken);
                if (File.Exists(_path)) File.Replace(temp, _path, null);
                else File.Move(temp, _path);
            }
            finally
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
        }
        finally
        {
            _gate.Release();
        }
    }
}
