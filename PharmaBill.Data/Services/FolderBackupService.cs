using PharmaBill.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
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

	public async Task<string> BackUpNowAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		BackupSettings settings = settingsStore.Load();
		if (string.IsNullOrWhiteSpace(settings.PrimaryFolder))
		{
			throw new InvalidOperationException("Choose a backup folder first.");
		}
		string password = RequireSavedPassword(settings);
		Directory.CreateDirectory(settings.PrimaryFolder);
		using IServiceScope scope = scopeFactory.CreateScope();
		string? shopName = await scope.ServiceProvider.GetRequiredService<PharmaBillDbContext>().PharmacyProfiles
			.AsNoTracking().OrderBy(p => p.CreatedAtUtc).Select(p => p.Name).FirstOrDefaultAsync(cancellationToken);
		string destination = UniquePath(settings.PrimaryFolder, BackupFileName(shopName, BusinessClock.Now));
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
			throw new InvalidOperationException("Set a backup password on the Backup page first. Without it nobody can restore the backup.");
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
