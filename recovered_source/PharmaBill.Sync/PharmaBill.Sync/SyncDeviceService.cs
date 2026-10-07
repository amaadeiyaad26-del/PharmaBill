using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PharmaBill.Core.Entities;
using PharmaBill.Core.Sync;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Sync;

public sealed class SyncDeviceService(PharmaBillDbContext context, DatabaseDeviceId localDeviceId, ProtectedPeerKeyStore keyStore)
{
	public Guid LocalDeviceId => localDeviceId.Value;

	public async Task InitializeLocalDeviceAsync(string deviceName, string? appVersion = null, CancellationToken cancellationToken = default(CancellationToken))
	{
		DeviceInfo deviceInfo = await context.DeviceInfos.IgnoreQueryFilters().SingleOrDefaultAsync((DeviceInfo item) => item.Id == localDeviceId.Value, cancellationToken);
		if (deviceInfo == null)
		{
			deviceInfo = new DeviceInfo
			{
				Id = localDeviceId.Value,
				DeviceName = deviceName,
				Platform = "Windows",
				AppVersion = appVersion,
				IsCurrentDevice = true,
				SyncState = SyncState.Synced,
				HlcStamp = new HybridLogicalClockState().Now(localDeviceId.Value, DateTime.UtcNow)
			};
			context.DeviceInfos.Add(deviceInfo);
		}
		else
		{
			deviceInfo.DeviceName = deviceName;
			deviceInfo.AppVersion = appVersion;
			deviceInfo.Platform = "Windows";
			deviceInfo.IsCurrentDevice = true;
		}
		await context.SaveChangesAsync(cancellationToken);
	}

	public PairingCode CreatePairingCode(TimeSpan? lifetime = null)
	{
		DateTime dateTime = DateTime.UtcNow.Add(lifetime ?? TimeSpan.FromMinutes(10L));
		string value = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)).TrimEnd('=').Replace('+', '-')
			.Replace('/', '_');
		return new PairingCode($"{new DateTimeOffset(dateTime).ToUnixTimeSeconds()}.{value}", dateTime);
	}

	public async Task PairDeviceAsync(Guid peerDeviceId, string peerName, string platform, string pairingCode, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (peerDeviceId == Guid.Empty || peerDeviceId == LocalDeviceId || string.IsNullOrWhiteSpace(peerName) || string.IsNullOrWhiteSpace(platform))
		{
			throw new ArgumentException("Peer ID, device name and platform are required.");
		}
		string[] array = pairingCode.Split('.', 2);
		if (array.Length != 2 || !long.TryParse(array[0], out var result))
		{
			throw new InvalidOperationException("Pairing code is invalid.");
		}
		DateTime utcDateTime = DateTimeOffset.FromUnixTimeSeconds(result).UtcDateTime;
		if (utcDateTime <= DateTime.UtcNow || utcDateTime > DateTime.UtcNow.AddMinutes(11.0))
		{
			throw new InvalidOperationException("Pairing code has expired or is not valid.");
		}
		byte[] array2;
		try
		{
			string text = array[1].Replace('-', '+').Replace('_', '/');
			array2 = Convert.FromBase64String(text.PadRight((text.Length + 3) / 4 * 4, '='));
		}
		catch (FormatException innerException)
		{
			throw new InvalidOperationException("Pairing code is invalid.", innerException);
		}
		if (array2.Length != 24)
		{
			throw new InvalidOperationException("Pairing code is invalid.");
		}
		byte[] bytes = Encoding.UTF8.GetBytes($"PharmaBill-offline-pair-v1:{result}");
		byte[] value = Rfc2898DeriveBytes.Pbkdf2(array2, bytes, 200000, HashAlgorithmName.SHA256, 32);
		CryptographicOperations.ZeroMemory(array2);
		Dictionary<Guid, byte[]> dictionary = keyStore.Load().ToDictionary((KeyValuePair<Guid, byte[]> item) => item.Key, (KeyValuePair<Guid, byte[]> item) => item.Value);
		if (dictionary.TryGetValue(peerDeviceId, out var value2))
		{
			CryptographicOperations.ZeroMemory(value2);
		}
		dictionary[peerDeviceId] = value;
		keyStore.Save(dictionary);
		DeviceInfo deviceInfo = await context.DeviceInfos.IgnoreQueryFilters().SingleOrDefaultAsync((DeviceInfo item) => item.Id == peerDeviceId, cancellationToken);
		if (deviceInfo == null)
		{
			deviceInfo = new DeviceInfo
			{
				Id = peerDeviceId,
				DeviceName = peerName.Trim(),
				Platform = platform.Trim(),
				AppVersion = string.Empty,
				IsCurrentDevice = false
			};
			context.DeviceInfos.Add(deviceInfo);
		}
		else
		{
			deviceInfo.DeviceName = peerName.Trim();
			deviceInfo.Platform = platform.Trim();
			deviceInfo.IsCurrentDevice = false;
			deviceInfo.IsDeleted = false;
		}
		await context.SaveChangesAsync(cancellationToken);
	}

	public async Task<IReadOnlyList<DeviceInfo>> GetDevicesAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		return await (from item in context.DeviceInfos.AsNoTracking()
			orderby item.IsCurrentDevice descending, item.DeviceName
			select item).ToListAsync(cancellationToken);
	}

	public async Task RevokeDeviceAsync(Guid peerDeviceId, CancellationToken cancellationToken = default(CancellationToken))
	{
		Dictionary<Guid, byte[]> dictionary = keyStore.Load().ToDictionary((KeyValuePair<Guid, byte[]> item) => item.Key, (KeyValuePair<Guid, byte[]> item) => item.Value);
		if (dictionary.Remove(peerDeviceId, out var value))
		{
			CryptographicOperations.ZeroMemory(value);
			keyStore.Save(dictionary);
		}
		DeviceInfo deviceInfo = await context.DeviceInfos.SingleOrDefaultAsync((DeviceInfo item) => item.Id == peerDeviceId && !item.IsCurrentDevice, cancellationToken);
		if (deviceInfo != null)
		{
			deviceInfo.IsDeleted = true;
			await context.SaveChangesAsync(cancellationToken);
		}
	}

	public byte[] GetPeerKey(Guid peerDeviceId)
	{
		if (!keyStore.Load().TryGetValue(peerDeviceId, out byte[] value))
		{
			throw new InvalidOperationException("The device is not paired or has been revoked.");
		}
		return value;
	}

	public bool IsPaired(Guid peerDeviceId)
	{
		return keyStore.Load().ContainsKey(peerDeviceId);
	}
}
