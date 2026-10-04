using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using PharmaBill.Core.Entities;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Sync;

public sealed record PairingCode(string Code, DateTime ExpiresAtUtc);

public sealed class SyncDeviceService(
    PharmaBillDbContext context,
    DatabaseDeviceId localDeviceId,
    ProtectedPeerKeyStore keyStore)
{
    public Guid LocalDeviceId => localDeviceId.Value;

    public async Task InitializeLocalDeviceAsync(
        string deviceName,
        string? appVersion = null,
        CancellationToken cancellationToken = default)
    {
        var device = await context.DeviceInfos.IgnoreQueryFilters()
            .SingleOrDefaultAsync(item => item.Id == localDeviceId.Value, cancellationToken);
        if (device is null)
        {
            device = new DeviceInfo
            {
                Id = localDeviceId.Value,
                DeviceName = deviceName,
                Platform = "Windows",
                AppVersion = appVersion,
                IsCurrentDevice = true,
                SyncState = SyncState.Synced,
                HlcStamp = new PharmaBill.Core.Sync.HybridLogicalClockState()
                    .Now(localDeviceId.Value, DateTime.UtcNow)
            };
            context.DeviceInfos.Add(device);
        }
        else
        {
            device.DeviceName = deviceName;
            device.AppVersion = appVersion;
            device.Platform = "Windows";
            device.IsCurrentDevice = true;
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    public PairingCode CreatePairingCode(TimeSpan? lifetime = null)
    {
        var expiresAt = DateTime.UtcNow.Add(lifetime ?? TimeSpan.FromMinutes(10));
        var secret = RandomNumberGenerator.GetBytes(24);
        var encoded = Convert.ToBase64String(secret).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return new PairingCode($"{new DateTimeOffset(expiresAt).ToUnixTimeSeconds()}.{encoded}", expiresAt);
    }

    public async Task PairDeviceAsync(
        Guid peerDeviceId,
        string peerName,
        string platform,
        string pairingCode,
        CancellationToken cancellationToken = default)
    {
        if (peerDeviceId == Guid.Empty || peerDeviceId == LocalDeviceId ||
            string.IsNullOrWhiteSpace(peerName) || string.IsNullOrWhiteSpace(platform))
        {
            throw new ArgumentException("Peer ID, device name and platform are required.");
        }

        var parts = pairingCode.Split('.', 2);
        if (parts.Length != 2 || !long.TryParse(parts[0], out var expirySeconds))
        {
            throw new InvalidOperationException("Pairing code is invalid.");
        }

        var expiry = DateTimeOffset.FromUnixTimeSeconds(expirySeconds).UtcDateTime;
        if (expiry <= DateTime.UtcNow || expiry > DateTime.UtcNow.AddMinutes(11))
        {
            throw new InvalidOperationException("Pairing code has expired or is not valid.");
        }

        byte[] secret;
        try
        {
            var encoded = parts[1].Replace('-', '+').Replace('_', '/');
            encoded = encoded.PadRight((encoded.Length + 3) / 4 * 4, '=');
            secret = Convert.FromBase64String(encoded);
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException("Pairing code is invalid.", exception);
        }

        if (secret.Length != 24)
        {
            throw new InvalidOperationException("Pairing code is invalid.");
        }

        var salt = System.Text.Encoding.UTF8.GetBytes($"PharmaBill-offline-pair-v1:{expirySeconds}");
        var peerKey = Rfc2898DeriveBytes.Pbkdf2(secret, salt, 200_000, HashAlgorithmName.SHA256, 32);
        CryptographicOperations.ZeroMemory(secret);
        var keys = keyStore.Load().ToDictionary(item => item.Key, item => item.Value);
        if (keys.TryGetValue(peerDeviceId, out var oldKey))
        {
            CryptographicOperations.ZeroMemory(oldKey);
        }
        keys[peerDeviceId] = peerKey;
        keyStore.Save(keys);

        var device = await context.DeviceInfos.IgnoreQueryFilters()
            .SingleOrDefaultAsync(item => item.Id == peerDeviceId, cancellationToken);
        if (device is null)
        {
            device = new DeviceInfo
            {
                Id = peerDeviceId,
                DeviceName = peerName.Trim(),
                Platform = platform.Trim(),
                AppVersion = string.Empty,
                IsCurrentDevice = false
            };
            context.DeviceInfos.Add(device);
        }
        else
        {
            device.DeviceName = peerName.Trim();
            device.Platform = platform.Trim();
            device.IsCurrentDevice = false;
            device.IsDeleted = false;
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DeviceInfo>> GetDevicesAsync(CancellationToken cancellationToken = default) =>
        await context.DeviceInfos.AsNoTracking()
            .OrderByDescending(item => item.IsCurrentDevice)
            .ThenBy(item => item.DeviceName)
            .ToListAsync(cancellationToken);

    public async Task RevokeDeviceAsync(Guid peerDeviceId, CancellationToken cancellationToken = default)
    {
        var keys = keyStore.Load().ToDictionary(item => item.Key, item => item.Value);
        if (keys.Remove(peerDeviceId, out var key))
        {
            CryptographicOperations.ZeroMemory(key);
            keyStore.Save(keys);
        }

        var device = await context.DeviceInfos.SingleOrDefaultAsync(
            item => item.Id == peerDeviceId && !item.IsCurrentDevice,
            cancellationToken);
        if (device is not null)
        {
            device.IsDeleted = true;
            await context.SaveChangesAsync(cancellationToken);
        }
    }

    public byte[] GetPeerKey(Guid peerDeviceId)
    {
        var keys = keyStore.Load();
        return keys.TryGetValue(peerDeviceId, out var key)
            ? key
            : throw new InvalidOperationException("The device is not paired or has been revoked.");
    }

    public bool IsPaired(Guid peerDeviceId) => keyStore.Load().ContainsKey(peerDeviceId);
}
