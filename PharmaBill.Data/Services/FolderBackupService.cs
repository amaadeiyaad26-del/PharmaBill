using PharmaBill.Core;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Data.Services;

// "Back up now": one encrypted .pbbak per run into the folder the shop chose once. Any folder works,
// including a OneDrive, Google Drive or Dropbox folder - the file is encrypted, so the sync client
// can copy it off-site without being able to read it.
public sealed class FolderBackupService(IServiceScopeFactory scopeFactory, BackupSettingsStore settingsStore, DatabaseStorageOptions storage)
{
	public const string Extension = ".pbbak";

	/// <summary>Used until the shop chooses a folder: in the user's profile, never Downloads (people empty it).</summary>
	public static string DefaultFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Pharma Remit Backups");

	public const string DownloadsRefusal = "Downloads is often emptied to free space, so it is no place for the shop's backups. Choose another folder, for example a OneDrive folder or Pharma Remit Backups.";

	/// <summary>The folder backups go to: the one chosen, else <see cref="DefaultFolder"/>.</summary>
	public static string FolderFor(BackupSettings settings) => string.IsNullOrWhiteSpace(settings.PrimaryFolder) ? DefaultFolder : settings.PrimaryFolder;

	public static bool IsInDownloads(string folder)
	{
		string? downloads = DownloadsFolder();
		if (downloads == null || string.IsNullOrWhiteSpace(folder))
		{
			return false;
		}
		string full = Path.GetFullPath(folder).TrimEnd('\\');
		string root = Path.GetFullPath(downloads).TrimEnd('\\');
		return full.Equals(root, StringComparison.OrdinalIgnoreCase) || full.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>The user's Downloads folder (where Windows actually keeps it), or null when it cannot be found.</summary>
	public static string? DownloadsFolder()
	{
		if (OperatingSystem.IsWindows() && SHGetKnownFolderPath(new Guid("374DE290-123F-4565-9164-39C4925E467B"), 0, IntPtr.Zero, out IntPtr path) == 0)
		{
			try
			{
				return System.Runtime.InteropServices.Marshal.PtrToStringUni(path);
			}
			finally
			{
				System.Runtime.InteropServices.Marshal.FreeCoTaskMem(path);
			}
		}
		string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
		return string.IsNullOrEmpty(profile) ? null : Path.Combine(profile, "Downloads");
	}

	[System.Runtime.InteropServices.DllImport("shell32.dll")]
	private static extern int SHGetKnownFolderPath([System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPStruct)] Guid id, uint flags, IntPtr token, out IntPtr path);

	public async Task<string> BackUpNowAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		BackupSettings settings = settingsStore.Load();
		string folder = FolderFor(settings);
		if (IsInDownloads(folder))
		{
			throw new InvalidOperationException(DownloadsRefusal);
		}
		string password = RequireSavedPassword(settings);
		Directory.CreateDirectory(folder);
		using IServiceScope scope = scopeFactory.CreateScope();
		string? shopName = await scope.ServiceProvider.GetRequiredService<PharmaBillDbContext>().PharmacyProfiles
			.AsNoTracking().OrderBy(p => p.CreatedAtUtc).Select(p => p.Name).FirstOrDefaultAsync(cancellationToken);
		string destination = UniquePath(folder, BackupFileName(shopName, BusinessClock.Now));
		await scope.ServiceProvider.GetRequiredService<EncryptedBackupService>().CreateBackupAsync(destination, password, cancellationToken);
		settingsStore.Save(settingsStore.Load() with
		{
			LastBackupAtUtc = BusinessClock.UtcNow
		});
		return destination;
	}

	// Tries the password typed at restore, then the one saved on this PC, then the derived passwords that
	// USB and Google Drive backups used before a backup password was required.
	public async Task<BackupRestoreResult> RestoreAsync(string backupPath, string? typedPassword, CancellationToken cancellationToken = default(CancellationToken))
	{
		List<string> candidates = new List<string>();
		void Add(string? candidate)
		{
			if (!string.IsNullOrWhiteSpace(candidate) && candidate.Length >= 10 && !candidates.Contains(candidate, StringComparer.Ordinal))
			{
				candidates.Add(candidate);
			}
		}
		Add(typedPassword);
		try
		{
			Add(settingsStore.Load().Password);
		}
		catch (InvalidOperationException)
		{
		}
		foreach (string password in LegacyDerivedPasswords(storage.RootDirectory, AppDataPaths.LocalRootDirectory))
		{
			Add(password);
		}
		using IServiceScope scope = scopeFactory.CreateScope();
		EncryptedBackupService backups = scope.ServiceProvider.GetRequiredService<EncryptedBackupService>();
		foreach (string candidate in candidates)
		{
			try
			{
				return await backups.RestoreBackupAsync(backupPath, candidate, cancellationToken);
			}
			catch (CryptographicException)
			{
			}
		}
		throw new CryptographicException(string.IsNullOrWhiteSpace(typedPassword)
			? "Type the backup password that was set when this backup was made."
			: "That backup password does not open this file. Use the password that was set when this backup was made.");
	}

	public static string RequireSavedPassword(BackupSettings settings)
	{
		if (string.IsNullOrWhiteSpace(settings.Password))
		{
			throw new InvalidOperationException("Set a backup password and press Save password first. Without it nobody can restore the backup.");
		}
		EncryptedBackupService.ValidatePassword(settings.Password);
		return settings.Password;
	}

	// "Shop Name 2026-10-21 14-30-05.pbbak": sorts by date within a shop and never repeats.
	public static string BackupFileName(string? shopName, DateTime localNow)
	{
		char[] invalid = Path.GetInvalidFileNameChars();
		StringBuilder builder = new StringBuilder();
		foreach (char c in shopName ?? string.Empty)
		{
			builder.Append(invalid.Contains(c) || char.IsControl(c) ? ' ' : c);
		}
		string name = string.Join(' ', builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries)).Trim('.', ' ');
		if (name.Length > 60)
		{
			name = name[..60].TrimEnd('.', ' ');
		}
		if (name.Length == 0)
		{
			name = "Pharma Remit";
		}
		return $"{name} {localNow:yyyy-MM-dd HH-mm-ss}{Extension}";
	}

	public static string UniquePath(string folder, string fileName)
	{
		string path = Path.Combine(folder, fileName);
		string stem = Path.GetFileNameWithoutExtension(fileName);
		for (int n = 2; File.Exists(path); n++)
		{
			path = Path.Combine(folder, $"{stem} ({n}){Extension}");
		}
		return path;
	}

	public static IEnumerable<string> LegacyDerivedPasswords(params string[] roots)
	{
		return roots.Where(root => !string.IsNullOrWhiteSpace(root))
			.Select(root => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("PharmaBill.Drive." + root))));
	}
}

public sealed record BackupReminderState(bool IsDue, DateTime? LastBackupUtc, int? DaysSinceLastBackup, string Message);

public static class BackupReminder
{
	public const int MaxAgeDays = 7;

	// The newest backup of either kind counts: a folder backup or a USB backup.
	public static BackupReminderState Evaluate(DateTime? lastFolderBackupUtc, DateTime? lastUsbBackupUtc, DateTime? snoozeUntilUtc, DateTime utcNow)
	{
		DateTime? last = (lastFolderBackupUtc, lastUsbBackupUtc) switch
		{
			(null, null) => null,
			(DateTime a, null) => a,
			(null, DateTime b) => b,
			(DateTime a, DateTime b) => a > b ? a : b
		};
		int? days = last.HasValue ? (int)Math.Floor((utcNow - last.Value).TotalDays) : null;
		bool stale = !days.HasValue || days.Value > MaxAgeDays;
		bool snoozed = snoozeUntilUtc.HasValue && snoozeUntilUtc.Value > utcNow;
		string message = days switch
		{
			null => "⚠ No backup has been made yet. Back up now so a broken or stolen PC does not take your records with it.",
			1 => "⚠ Your last backup was 1 day ago.",
			int d => $"⚠ Your last backup was {d} days ago. Back up now so a broken or stolen PC does not take your records with it."
		};
		return new BackupReminderState(stale && !snoozed, last, days, message);
	}
}
