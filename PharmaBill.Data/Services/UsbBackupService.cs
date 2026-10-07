using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Data.Services;

public sealed class UsbBackupService(IServiceScopeFactory scopeFactory, UsbBackupSettingsStore usbSettings, BackupSettingsStore backupSettings, DatabaseStorageOptions storage)
{
	public const string BackupFolderName = "PharmaBill_Backups";

	public IReadOnlyList<RemovableDriveOption> ListRemovableDrives()
	{
		List<RemovableDriveOption> list = new List<RemovableDriveOption>();
		DriveInfo[] drives = DriveInfo.GetDrives();
		foreach (DriveInfo driveInfo in drives)
		{
			try
			{
				if (driveInfo.DriveType == DriveType.Removable && driveInfo.IsReady)
				{
					list.Add(new RemovableDriveOption(driveInfo.RootDirectory.FullName, driveInfo.VolumeLabel, driveInfo.AvailableFreeSpace, driveInfo.TotalSize));
				}
			}
			catch
			{
			}
		}
		return list.OrderBy((RemovableDriveOption item) => item.Root, StringComparer.OrdinalIgnoreCase).ToArray();
	}

	public async Task<string> BackupToUsbAsync(string driveRoot, CancellationToken cancellationToken = default(CancellationToken))
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(driveRoot, "driveRoot");
		string root = Path.GetFullPath(driveRoot);
		if (!root.EndsWith(Path.DirectorySeparatorChar) && !root.EndsWith(Path.AltDirectorySeparatorChar))
		{
			root += Path.DirectorySeparatorChar;
		}
		DriveInfo driveInfo = new DriveInfo(root);
		if (driveInfo.DriveType != DriveType.Removable)
		{
			throw new InvalidOperationException("Select a removable USB drive.");
		}
		if (!driveInfo.IsReady)
		{
			throw new InvalidOperationException("The selected USB drive is not ready. Re-insert it and click Refresh Drives.");
		}
		EnsureWritable(root);
		string databasePath = storage.DatabasePath;
		long num = (File.Exists(databasePath) ? Math.Max(new FileInfo(databasePath).Length * 2, 52428800L) : 104857600);
		if (driveInfo.AvailableFreeSpace < num)
		{
			throw new InvalidOperationException($"Not enough free space on {root}. Need about {(double)num / 1048576.0:0} MB free.");
		}
		string text = Path.Combine(root, "PharmaBill_Backups");
		Directory.CreateDirectory(text);
		string path = $"PharmaBill-{DateTime.Now:yyyyMMdd-HHmmss}.pbbak";
		string destination = Path.Combine(text, path);
		string password = ResolveBackupPassword();
		using (IServiceScope scope = scopeFactory.CreateScope())
		{
			await scope.ServiceProvider.GetRequiredService<EncryptedBackupService>().CreateBackupAsync(destination, password, cancellationToken);
		}
		if (!File.Exists(destination) || new FileInfo(destination).Length == 0L)
		{
			throw new InvalidOperationException("USB backup file was not created.");
		}
		UsbBackupSettings usbBackupSettings = usbSettings.Load();
		usbSettings.Save(usbBackupSettings with
		{
			LastUsbBackupUtc = DateTime.UtcNow,
			LastUsbDriveRoot = root,
			ReminderSnoozeUntilUtc = null
		});
		return destination;
	}

	private static void EnsureWritable(string root)
	{
		string path = Path.Combine(root, $".pharmabill-write-{Guid.NewGuid():N}.tmp");
		try
		{
			File.WriteAllText(path, "ok");
		}
		catch (Exception ex) when ((ex is UnauthorizedAccessException || ex is IOException) ? true : false)
		{
			throw new InvalidOperationException("Cannot write to " + root + ". Check that the USB drive is not write-protected.", ex);
		}
		finally
		{
			try
			{
				if (File.Exists(path))
				{
					File.Delete(path);
				}
			}
			catch
			{
			}
		}
	}

	private string ResolveBackupPassword()
	{
		string password = backupSettings.Load().Password;
		if (!string.IsNullOrWhiteSpace(password) && password.Length >= 12)
		{
			return password;
		}
		return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("PharmaBill.Drive." + storage.RootDirectory)));
	}
}
