using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Data.Services;

public sealed class UsbBackupSettingsStore(DatabaseStorageOptions storage)
{
	private readonly string _path = Path.Combine(storage.RootDirectory, "usb-backup-settings.dat");

	public event Action<UsbBackupSettings>? SettingsChanged;

	public UsbBackupSettings Load()
	{
		if (!OperatingSystem.IsWindows() || !File.Exists(_path))
		{
			return new UsbBackupSettings(null, null, null);
		}
		try
		{
			return JsonSerializer.Deserialize<UsbBackupSettings>(ProtectedData.Unprotect(File.ReadAllBytes(_path), null, DataProtectionScope.CurrentUser)) ?? new UsbBackupSettings(null, null, null);
		}
		catch
		{
			return new UsbBackupSettings(null, null, null);
		}
	}

	public void Save(UsbBackupSettings settings)
	{
		ArgumentNullException.ThrowIfNull(settings, "settings");
		if (!OperatingSystem.IsWindows())
		{
			throw new PlatformNotSupportedException("USB backup settings use Windows DPAPI.");
		}
		Directory.CreateDirectory(storage.RootDirectory);
		byte[] bytes = ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(settings), null, DataProtectionScope.CurrentUser);
		string text = $"{_path}.{Guid.NewGuid():N}.tmp";
		File.WriteAllBytes(text, bytes);
		File.Move(text, _path, overwrite: true);
		SettingsChanged?.Invoke(settings);
	}

	public bool IsReminderDue(DateTime utcNow)
	{
		UsbBackupSettings usbBackupSettings = Load();
		DateTime? reminderSnoozeUntilUtc = usbBackupSettings.ReminderSnoozeUntilUtc;
		if (reminderSnoozeUntilUtc.HasValue)
		{
			DateTime valueOrDefault = reminderSnoozeUntilUtc.GetValueOrDefault();
			if (valueOrDefault > utcNow)
			{
				return false;
			}
		}
		if (!usbBackupSettings.LastUsbBackupUtc.HasValue)
		{
			return true;
		}
		return usbBackupSettings.LastUsbBackupUtc.Value < utcNow.AddDays(-30.0);
	}
}
