using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PharmaBill.Core;

namespace PharmaBill.Sync.Terminals;

public sealed record TerminalDevice(Guid Id, string Name, string KeyHash, DateTime PairedAtUtc, DateTime? RevokedAtUtc, DateTime? LastSeenAtUtc)
{
	public bool IsActive => RevokedAtUtc == null;
}

// The terminals paired with this server PC, each with its own key. Kept with this PC (not in the shop database,
// which syncs to phones) and protected with Windows DPAPI. Only a SHA-256 of each key is stored: the key itself
// exists only on its terminal.
public sealed class TerminalDeviceStore
{
	private static readonly TimeSpan LastSeenWriteInterval = TimeSpan.FromMinutes(5);

	private readonly string _path;

	private readonly object _gate = new object();

	private List<TerminalDevice>? _devices;

	private DateTime _lastSeenSavedUtc = DateTime.MinValue;

	public TerminalDeviceStore(string directory)
	{
		_path = Path.Combine(directory, "terminal-devices.dat");
	}

	public event Action? Changed;

	public IReadOnlyList<TerminalDevice> List()
	{
		lock (_gate)
		{
			return Load().OrderBy(device => device.PairedAtUtc).ToArray();
		}
	}

	/// <summary>Adds a terminal and returns its key. The key is shown to the terminal once and never stored here.</summary>
	public (TerminalDevice Device, string Secret) Register(string name)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(name, nameof(name));
		string secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
		TerminalDevice device = new TerminalDevice(Guid.NewGuid(), name.Trim(), Hash(secret), BusinessClock.UtcNow, null, null);
		lock (_gate)
		{
			Load().Add(device);
			Save();
		}
		Changed?.Invoke();
		return (device, secret);
	}

	/// <summary>The active terminal with this ID and key, or null. Unknown, revoked and wrong-key all look the same.</summary>
	public TerminalDevice? Authenticate(Guid deviceId, string? secret)
	{
		byte[] presented = Convert.FromHexString(Hash(secret ?? string.Empty));
		lock (_gate)
		{
			List<TerminalDevice> devices = Load();
			int index = devices.FindIndex(device => device.Id == deviceId);
			TerminalDevice? device = index >= 0 ? devices[index] : null;
			byte[] expected = device == null ? new byte[32] : Convert.FromHexString(device.KeyHash);
			bool keyMatches = CryptographicOperations.FixedTimeEquals(presented, expected);
			if (device == null || !device.IsActive || !keyMatches)
			{
				return null;
			}
			DateTime now = BusinessClock.UtcNow;
			devices[index] = device with { LastSeenAtUtc = now };
			if (now - _lastSeenSavedUtc > LastSeenWriteInterval)
			{
				_lastSeenSavedUtc = now;
				Save();
			}
			return devices[index];
		}
	}

	public bool Revoke(Guid deviceId)
	{
		lock (_gate)
		{
			List<TerminalDevice> devices = Load();
			int index = devices.FindIndex(device => device.Id == deviceId && device.IsActive);
			if (index < 0)
			{
				return false;
			}
			devices[index] = devices[index] with { RevokedAtUtc = BusinessClock.UtcNow };
			Save();
		}
		Changed?.Invoke();
		return true;
	}

	private static string Hash(string secret) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));

	private List<TerminalDevice> Load()
	{
		if (_devices != null)
		{
			return _devices;
		}
		if (!File.Exists(_path))
		{
			return _devices = new List<TerminalDevice>();
		}
		byte[] plain = ProtectedData.Unprotect(File.ReadAllBytes(_path), null, DataProtectionScope.CurrentUser);
		return _devices = JsonSerializer.Deserialize<List<TerminalDevice>>(plain) ?? new List<TerminalDevice>();
	}

	private void Save()
	{
		Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
		byte[] protectedBytes = ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(_devices ?? new List<TerminalDevice>()), null, DataProtectionScope.CurrentUser);
		string temp = $"{_path}.{Guid.NewGuid():N}.tmp";
		File.WriteAllBytes(temp, protectedBytes);
		File.Move(temp, _path, overwrite: true);
	}
}
