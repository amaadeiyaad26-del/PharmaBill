using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PharmaBill.Core.Entities;
using PharmaBill.Data.Persistence;
using PharmaBill.Data.Services;

namespace PharmaBill.Sync;

public sealed class BranchNetworkSyncService(IServiceScopeFactory scopeFactory, GoogleDriveSyncService googleDrive, BackupSettingsStore backupSettings, DatabaseStorageOptions storage, ILogger<BranchNetworkSyncService> logger)
{
	public const string SnapshotRemoteName = "snapshot.db.enc";

	public const string StockIndexRemoteName = "stock-index.json";

	public const string SalesSummaryRemoteName = "sales-summary.json";

	public async Task PublishCurrentBranchAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		using IServiceScope scope = scopeFactory.CreateScope();
		BranchService branchService = scope.ServiceProvider.GetRequiredService<BranchService>();
		EncryptedBackupService backups = scope.ServiceProvider.GetRequiredService<EncryptedBackupService>();
		Branch branch = await branchService.EnsureCurrentBranchAsync(cancellationToken);
		BranchStockIndexDocument stockIndex = await branchService.BuildLocalStockIndexAsync(cancellationToken);
		BranchSalesSummaryDocument salesSummary = await branchService.BuildLocalSalesSummaryAsync(null, null, cancellationToken);
		string workDir = Path.Combine(Path.GetTempPath(), $"PharmaBill-branch-{Guid.NewGuid():N}");
		Directory.CreateDirectory(workDir);
		try
		{
			string snapshotPath = Path.Combine(workDir, "snapshot.db.enc");
			string stockPath = Path.Combine(workDir, "stock-index.json");
			string salesPath = Path.Combine(workDir, "sales-summary.json");
			await backups.CreateBackupAsync(snapshotPath, ResolveBackupPassword(), cancellationToken);
			await File.WriteAllTextAsync(stockPath, JsonSerializer.Serialize(stockIndex, new JsonSerializerOptions
			{
				WriteIndented = true
			}), cancellationToken);
			await File.WriteAllTextAsync(salesPath, JsonSerializer.Serialize(salesSummary, new JsonSerializerOptions
			{
				WriteIndented = true
			}), cancellationToken);
			string relativeFolder = "Branches/" + Sanitize(branch.Code);
			await UploadIfConnectedAsync(snapshotPath, relativeFolder + "/snapshot.db.enc", cancellationToken);
			await UploadIfConnectedAsync(stockPath, relativeFolder + "/stock-index.json", cancellationToken);
			await UploadIfConnectedAsync(salesPath, relativeFolder + "/sales-summary.json", cancellationToken);
			logger.LogInformation("Published branch snapshot for {BranchCode}", branch.Code);
		}
		finally
		{
			TryDeleteDirectory(workDir);
		}
	}

	public async Task AggregatePeerBranchIndexesAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		using IServiceScope scope = scopeFactory.CreateScope();
		BranchService branchService = scope.ServiceProvider.GetRequiredService<BranchService>();
		BranchSettings currentSettings = branchService.CurrentSettings;
		if (!currentSettings.IsHeadOffice)
		{
			throw new InvalidOperationException("Only the head-office desktop can aggregate peer branch snapshots.");
		}
		string currentCode = currentSettings.CurrentBranchCode;
		foreach (string code in (await googleDrive.ListBranchCodesAsync(cancellationToken)).Where((string a) => !string.Equals(a, currentCode, StringComparison.OrdinalIgnoreCase)))
		{
			try
			{
				await DownloadPeerIndexesAsync(branchService, code, cancellationToken);
			}
			catch (Exception exception)
			{
				logger.LogWarning(exception, "Failed to download peer indexes for {BranchCode}", code);
			}
		}
	}

	private async Task DownloadPeerIndexesAsync(BranchService branchService, string branchCode, CancellationToken cancellationToken)
	{
		string tempDir = Path.Combine(Path.GetTempPath(), $"PharmaBill-peer-{Guid.NewGuid():N}");
		Directory.CreateDirectory(tempDir);
		try
		{
			string remoteRelativePath = "Branches/" + Sanitize(branchCode) + "/stock-index.json";
			string salesRemote = "Branches/" + Sanitize(branchCode) + "/sales-summary.json";
			string text = await googleDrive.DownloadNestedFileAsync(tempDir, remoteRelativePath, cancellationToken);
			if (text != null && File.Exists(text))
			{
				BranchStockIndexDocument branchStockIndexDocument = JsonSerializer.Deserialize<BranchStockIndexDocument>(await File.ReadAllTextAsync(text, cancellationToken));
				if (branchStockIndexDocument != null)
				{
					branchService.SavePeerStockIndex(branchStockIndexDocument);
				}
			}
			string text2 = await googleDrive.DownloadNestedFileAsync(tempDir, salesRemote, cancellationToken);
			if (text2 != null && File.Exists(text2))
			{
				BranchSalesSummaryDocument branchSalesSummaryDocument = JsonSerializer.Deserialize<BranchSalesSummaryDocument>(await File.ReadAllTextAsync(text2, cancellationToken));
				if (branchSalesSummaryDocument != null)
				{
					branchService.SavePeerSalesSummary(branchSalesSummaryDocument);
				}
			}
		}
		finally
		{
			TryDeleteDirectory(tempDir);
		}
	}

	private async Task UploadIfConnectedAsync(string localPath, string remoteRelativePath, CancellationToken cancellationToken)
	{
		if ((await googleDrive.GetSettingsAsync(cancellationToken)).IsConnected)
		{
			await googleDrive.UploadNestedFileAsync(localPath, remoteRelativePath, cancellationToken);
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

	private static string Sanitize(string value)
	{
		char[] invalid = Path.GetInvalidFileNameChars();
		return new string((from ch in value.Trim()
			select (!invalid.Contains(ch)) ? ch : '_').ToArray());
	}

	private static void TryDeleteDirectory(string path)
	{
		try
		{
			if (Directory.Exists(path))
			{
				Directory.Delete(path, recursive: true);
			}
		}
		catch
		{
		}
	}
}
