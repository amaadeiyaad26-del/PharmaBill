using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Drive.v3;
using Google.Apis.Drive.v3.Data;
using Google.Apis.Services;
using Google.Apis.Upload;
using Google.Apis.Util.Store;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PharmaBill.Data.Persistence;
using PharmaBill.Data.Services;

namespace PharmaBill.Sync;

public sealed class GoogleDriveSyncService
{
	private sealed class NullDataStore : IDataStore
	{
		public Task ClearAsync()
		{
			return Task.CompletedTask;
		}

		public Task DeleteAsync<T>(string key)
		{
			return Task.CompletedTask;
		}

		public Task<T?> GetAsync<T>(string key)
		{
			return Task.FromResult<T>(default);
		}

		public Task StoreAsync<T>(string key, T value)
		{
			return Task.CompletedTask;
		}
	}

	public const string SyncFolderName = "PharmaBill_Sync";

	public const string BackupFileName = "PharmaBill-cloud-latest.pbbak";

	private static readonly string[] Scopes = new string[1] { DriveService.Scope.DriveFile };

	private readonly GoogleDriveSyncSettingsStore _settingsStore;

	private readonly CloudOAuthClientStore _oauthClients;

	private readonly DatabaseStorageOptions _storage;

	private readonly BackupSettingsStore _backupSettings;

	private readonly IServiceScopeFactory _scopeFactory;

	private readonly ILogger<GoogleDriveSyncService> _logger;

	private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);

	public event Action<GoogleDriveSyncSettings>? SettingsChanged;

	public event Action<GoogleDriveProgress>? ProgressChanged;

	public GoogleDriveSyncService(GoogleDriveSyncSettingsStore settingsStore, CloudOAuthClientStore oauthClients, DatabaseStorageOptions storage, BackupSettingsStore backupSettings, IServiceScopeFactory scopeFactory, ILogger<GoogleDriveSyncService> logger)
	{
		_settingsStore = settingsStore;
		_oauthClients = oauthClients;
		_storage = storage;
		_backupSettings = backupSettings;
		_scopeFactory = scopeFactory;
		_logger = logger;
	}

	public Task<GoogleDriveSyncSettings> GetSettingsAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		return _settingsStore.LoadAsync(cancellationToken);
	}

	public async Task ConnectAsync(string preferredEmail, CancellationToken cancellationToken = default(CancellationToken))
	{
		await _gate.WaitAsync(cancellationToken);
		try
		{
			Report(5, "Opening Google sign-in in your browser…", 0L, 0L);
			ClientSecrets clientSecrets = await ResolveClientSecretsAsync(cancellationToken);
			string user = (string.IsNullOrWhiteSpace(preferredEmail) ? "user" : preferredEmail.Trim());
			string text = Path.Combine(_storage.RootDirectory, "google-oauth-tokens");
			Directory.CreateDirectory(text);
			UserCredential credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(clientSecrets, Scopes, user, cancellationToken, new FileDataStore(text, fullPath: true));
			string email = await TryReadAccountEmailAsync(credential, preferredEmail, cancellationToken);
			string refreshToken = credential.Token.RefreshToken;
			if (string.IsNullOrWhiteSpace(refreshToken))
			{
				refreshToken = (await _settingsStore.LoadAsync(cancellationToken)).RefreshToken;
			}
			if (string.IsNullOrWhiteSpace(refreshToken))
			{
				throw new InvalidOperationException("Google did not return a refresh token. Revoke PharmaBill access at myaccount.google.com/permissions and try Connect again.");
			}
			GoogleDriveSyncSettings settings = new GoogleDriveSyncSettings(IsConnected: true, email, "Connected", null, SyncOnExit: true, SyncOnBillSave: true, null, refreshToken);
			await _settingsStore.SaveAsync(settings, cancellationToken);
			SettingsChanged?.Invoke(settings);
			Report(100, "Connected as " + email, 0L, 0L);
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception ex2)
		{
			_logger.LogWarning(ex2, "Google Drive connect failed.");
			string email = CloudOAuthClientStore.ToUserFriendlyMessage(ex2);
			GoogleDriveSyncSettings settings = await _settingsStore.LoadAsync(cancellationToken)with
			{
				IsConnected = false,
				AccountEmail = string.Empty,
				Status = "Not Connected",
				LastError = email,
				RefreshToken = string.Empty
			};
			await _settingsStore.SaveAsync(settings, cancellationToken);
			SettingsChanged?.Invoke(settings);
			if (ex2 is CloudOAuthNotConfiguredException)
			{
				throw;
			}
			throw new InvalidOperationException(email, ex2);
		}
		finally
		{
			_gate.Release();
		}
	}

	public async Task DisconnectAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		GoogleDriveSyncSettings googleDriveSyncSettings = await _settingsStore.LoadAsync(cancellationToken);
		GoogleDriveSyncSettings cleared = GoogleDriveSyncSettings.Default with
		{
			SyncOnExit = googleDriveSyncSettings.SyncOnExit,
			SyncOnBillSave = googleDriveSyncSettings.SyncOnBillSave,
			Status = "Not Connected"
		};
		await _settingsStore.SaveAsync(cleared, cancellationToken);
		TryDeleteTokenCache();
		SettingsChanged?.Invoke(cleared);
		Report(100, "Disconnected from Google Drive", 0L, 0L);
	}

	public async Task SavePreferencesAsync(bool syncOnExit, bool syncOnBillSave, CancellationToken cancellationToken = default(CancellationToken))
	{
		GoogleDriveSyncSettings updated = await _settingsStore.LoadAsync(cancellationToken)with
		{
			SyncOnExit = syncOnExit,
			SyncOnBillSave = syncOnBillSave
		};
		await _settingsStore.SaveAsync(updated, cancellationToken);
		SettingsChanged?.Invoke(updated);
	}

	public async Task<string> TestConnectionAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		await _gate.WaitAsync(cancellationToken);
		try
		{
			Report(10, "Verifying Google Drive token…", 0L, 0L);
			GoogleDriveSyncSettings settings = await _settingsStore.LoadAsync(cancellationToken);
			if (!settings.IsConnected)
			{
				throw new InvalidOperationException("Link a Google account before testing cloud sync.");
			}
			if (string.IsNullOrWhiteSpace(settings.RefreshToken))
			{
				throw new InvalidOperationException("Link a Google account before testing cloud sync.");
			}
			DriveService service = await CreateDriveServiceAsync(settings, cancellationToken);
			string folderId = await EnsureSyncFolderAsync(service, cancellationToken);
			Report(55, "Uploading connection test payload…", 0L, 0L);
			string temp = Path.Combine(Path.GetTempPath(), $"PharmaBill-gdrive-test-{Guid.NewGuid():N}.txt");
			await System.IO.File.WriteAllTextAsync(temp, $"PharmaBill Google Drive connection test at {DateTime.UtcNow:O} UTC", cancellationToken);
			try
			{
				await UploadOrReplaceAsync(service, folderId, temp, "PharmaBill-connection-test.txt", "Uploading connection test payload…", cancellationToken);
			}
			finally
			{
				TryDeleteFile(temp);
			}
			GoogleDriveSyncSettings done = settings with
			{
				Status = "Connected",
				LastError = null,
				LastSyncAtUtc = DateTime.UtcNow
			};
			await _settingsStore.SaveAsync(done, cancellationToken);
			SettingsChanged?.Invoke(done);
			Report(100, "Google Drive test OK", 0L, 0L, isError: false, isComplete: true);
			return $"Google Drive OK — connected as {done.AccountEmail}; folder {"PharmaBill_Sync"}/.";
		}
		catch (Exception ex)
		{
			Exception exception = ex;
			_logger.LogWarning(exception, "Google Drive test connection failed.");
			GoogleDriveSyncSettings done = await _settingsStore.LoadAsync(cancellationToken)with
			{
				Status = "Error",
				LastError = exception.Message
			};
			await _settingsStore.SaveAsync(done, cancellationToken);
			SettingsChanged?.Invoke(done);
			ExceptionDispatchInfo.Capture((ex as Exception) ?? throw ex).Throw();
		}
		finally
		{
			_gate.Release();
		}
		throw null;
	}

	public async Task UploadBackupAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		await _gate.WaitAsync(cancellationToken);
		try
		{
			Report(5, "Preparing encrypted backup…", 0L, 0L);
			GoogleDriveSyncSettings settings = await _settingsStore.LoadAsync(cancellationToken);
			if (!settings.IsConnected)
			{
				throw new InvalidOperationException("Connect Google Drive before uploading a backup.");
			}
			string password = ResolveBackupPassword();
			string tempBackup = Path.Combine(Path.GetTempPath(), $"PharmaBill-drive-{Guid.NewGuid():N}.pbbak");
			try
			{
				using (IServiceScope scope = _scopeFactory.CreateScope())
				{
					await scope.ServiceProvider.GetRequiredService<EncryptedBackupService>().CreateBackupAsync(tempBackup, password, cancellationToken);
				}
				if (string.IsNullOrWhiteSpace(settings.RefreshToken))
				{
					throw new InvalidOperationException("Connect Google Drive before uploading a backup.");
				}
				DriveService service = await CreateDriveServiceAsync(settings, cancellationToken);
				string folderId = await EnsureSyncFolderAsync(service, cancellationToken);
				long totalBytes = new FileInfo(tempBackup).Length;
				Report(40, "Uploading changes to Google Drive…", 0L, totalBytes);
				await UploadOrReplaceAsync(service, folderId, tempBackup, "PharmaBill-cloud-latest.pbbak", "Uploading changes to Google Drive…", cancellationToken);
				GoogleDriveSyncSettings done = settings with
				{
					Status = "Connected",
					LastSyncAtUtc = DateTime.UtcNow,
					LastError = null
				};
				await _settingsStore.SaveAsync(done, cancellationToken);
				SettingsChanged?.Invoke(done);
				Report(100, "Backup uploaded to PharmaBill_Sync", totalBytes, totalBytes, isError: false, isComplete: true);
			}
			finally
			{
				TryDeleteFile(tempBackup);
			}
		}
		catch (Exception ex)
		{
			Exception exception = ex;
			_logger.LogError(exception, "Google Drive upload failed.");
			string tempBackup = ToUploadErrorMessage(exception);
			GoogleDriveSyncSettings settings = await _settingsStore.LoadAsync(cancellationToken)with
			{
				Status = "Error",
				LastError = tempBackup
			};
			await _settingsStore.SaveAsync(settings, cancellationToken);
			SettingsChanged?.Invoke(settings);
			Report(0, tempBackup, 0L, 0L, isError: true);
			ExceptionDispatchInfo.Capture((ex as Exception) ?? throw ex).Throw();
		}
		finally
		{
			_gate.Release();
		}
	}

	public async Task DownloadAndRestoreAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		await _gate.WaitAsync(cancellationToken);
		try
		{
			Report(5, "Looking for cloud backup…", 0L, 0L);
			GoogleDriveSyncSettings settings = await _settingsStore.LoadAsync(cancellationToken);
			if (!settings.IsConnected)
			{
				throw new InvalidOperationException("Connect Google Drive before restoring a backup.");
			}
			if (string.IsNullOrWhiteSpace(settings.RefreshToken))
			{
				throw new InvalidOperationException("Connect Google Drive before restoring a backup.");
			}
			DriveService service = await CreateDriveServiceAsync(settings, cancellationToken);
			Google.Apis.Drive.v3.Data.File file = (await FindLatestBackupAsync(service, await EnsureSyncFolderAsync(service, cancellationToken), "PharmaBill-cloud-latest.pbbak", cancellationToken)) ?? throw new InvalidOperationException("No PharmaBill backup was found in Google Drive.");
			Report(35, "Downloading " + file.Name + "…", 0L, 0L);
			string tempBackup = Path.Combine(Path.GetTempPath(), $"PharmaBill-drive-restore-{Guid.NewGuid():N}.pbbak");
			await using (FileStream output = System.IO.File.Create(tempBackup))
			{
				await service.Files.Get(file.Id).DownloadAsync(output, cancellationToken);
			}
			try
			{
				Report(70, "Restoring local database…", 0L, 0L);
				string password = ResolveBackupPassword();
				using (IServiceScope scope = _scopeFactory.CreateScope())
				{
					await scope.ServiceProvider.GetRequiredService<EncryptedBackupService>().RestoreBackupAsync(tempBackup, password, cancellationToken);
				}
				GoogleDriveSyncSettings done = settings with
				{
					Status = "Connected",
					LastSyncAtUtc = DateTime.UtcNow,
					LastError = null
				};
				await _settingsStore.SaveAsync(done, cancellationToken);
				SettingsChanged?.Invoke(done);
				Report(100, "Cloud backup restored", 0L, 0L);
			}
			finally
			{
				TryDeleteFile(tempBackup);
			}
		}
		catch (Exception ex)
		{
			Exception exception = ex;
			_logger.LogError(exception, "Google Drive download/restore failed.");
			GoogleDriveSyncSettings settings = await _settingsStore.LoadAsync(cancellationToken)with
			{
				Status = "Error",
				LastError = exception.Message
			};
			await _settingsStore.SaveAsync(settings, cancellationToken);
			SettingsChanged?.Invoke(settings);
			ExceptionDispatchInfo.Capture((ex as Exception) ?? throw ex).Throw();
		}
		finally
		{
			_gate.Release();
		}
	}

	public async Task UploadLocalFileAsync(string localFilePath, string remoteFileName, CancellationToken cancellationToken = default(CancellationToken))
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(localFilePath, "localFilePath");
		ArgumentException.ThrowIfNullOrWhiteSpace(remoteFileName, "remoteFileName");
		if (!System.IO.File.Exists(localFilePath))
		{
			throw new FileNotFoundException("The local backup file was not found.", localFilePath);
		}
		await _gate.WaitAsync(cancellationToken);
		try
		{
			GoogleDriveSyncSettings settings = await _settingsStore.LoadAsync(cancellationToken);
			if (!settings.IsConnected)
			{
				throw new InvalidOperationException("Connect Google Drive before uploading a backup.");
			}
			if (string.IsNullOrWhiteSpace(settings.RefreshToken))
			{
				throw new InvalidOperationException("Connect Google Drive before uploading a backup.");
			}
			long totalBytes = new FileInfo(localFilePath).Length;
			Report(40, "Uploading changes to Google Drive…", 0L, totalBytes);
			DriveService service = await CreateDriveServiceAsync(settings, cancellationToken);
			await UploadOrReplaceAsync(service, await EnsureSyncFolderAsync(service, cancellationToken), localFilePath, remoteFileName, "Uploading changes to Google Drive…", cancellationToken);
			GoogleDriveSyncSettings done = settings with
			{
				Status = "Connected",
				LastSyncAtUtc = DateTime.UtcNow,
				LastError = null
			};
			await _settingsStore.SaveAsync(done, cancellationToken);
			SettingsChanged?.Invoke(done);
			Report(100, "Backup uploaded to PharmaBill_Sync", totalBytes, totalBytes, isError: false, isComplete: true);
		}
		catch (Exception ex)
		{
			Exception exception = ex;
			_logger.LogError(exception, "Google Drive upload failed.");
			string friendly = ToUploadErrorMessage(exception);
			GoogleDriveSyncSettings done = await _settingsStore.LoadAsync(cancellationToken)with
			{
				Status = "Error",
				LastError = friendly
			};
			await _settingsStore.SaveAsync(done, cancellationToken);
			SettingsChanged?.Invoke(done);
			Report(0, friendly, 0L, 0L, isError: true);
			ExceptionDispatchInfo.Capture((ex as Exception) ?? throw ex).Throw();
		}
		finally
		{
			_gate.Release();
		}
	}

	public async Task<string> DownloadLatestFileAsync(string localDestDirectory, string remoteFileName, CancellationToken cancellationToken = default(CancellationToken))
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(localDestDirectory, "localDestDirectory");
		Directory.CreateDirectory(localDestDirectory);
		await _gate.WaitAsync(cancellationToken);
		try
		{
			Report(5, "Looking for cloud backup…", 0L, 0L);
			GoogleDriveSyncSettings settings = await _settingsStore.LoadAsync(cancellationToken);
			if (!settings.IsConnected)
			{
				throw new InvalidOperationException("Connect Google Drive before restoring a backup.");
			}
			if (string.IsNullOrWhiteSpace(settings.RefreshToken))
			{
				throw new InvalidOperationException("Connect Google Drive before restoring a backup.");
			}
			DriveService service = await CreateDriveServiceAsync(settings, cancellationToken);
			Google.Apis.Drive.v3.Data.File file = (await FindLatestBackupAsync(service, await EnsureSyncFolderAsync(service, cancellationToken), remoteFileName, cancellationToken)) ?? throw new InvalidOperationException("No PharmaBill backup was found in Google Drive.");
			Report(35, "Downloading " + file.Name + "…", 0L, 0L);
			string liveDestination = Path.Combine(localDestDirectory, remoteFileName);
			await using (FileStream output = System.IO.File.Create(liveDestination))
			{
				await service.Files.Get(file.Id).DownloadAsync(output, cancellationToken);
			}
			GoogleDriveSyncSettings done = settings with
			{
				Status = "Connected",
				LastSyncAtUtc = DateTime.UtcNow,
				LastError = null
			};
			await _settingsStore.SaveAsync(done, cancellationToken);
			SettingsChanged?.Invoke(done);
			Report(100, "Cloud backup downloaded", 0L, 0L);
			return liveDestination;
		}
		catch (Exception ex)
		{
			Exception exception = ex;
			_logger.LogError(exception, "Google Drive download failed.");
			GoogleDriveSyncSettings done = await _settingsStore.LoadAsync(cancellationToken)with
			{
				Status = "Error",
				LastError = exception.Message
			};
			await _settingsStore.SaveAsync(done, cancellationToken);
			SettingsChanged?.Invoke(done);
			ExceptionDispatchInfo.Capture((ex as Exception) ?? throw ex).Throw();
		}
		finally
		{
			_gate.Release();
		}
		throw null;
	}

	private void Report(int percent, string message, long bytesSent = 0L, long totalBytes = 0L, bool isError = false, bool isComplete = false)
	{
		ProgressChanged?.Invoke(new GoogleDriveProgress(percent, message, bytesSent, totalBytes, isError, isComplete));
	}

	private static string ToUploadErrorMessage(Exception exception)
	{
		string text = exception.Message ?? "Google Drive upload failed.";
		if (text.Contains("rateLimitExceeded", StringComparison.OrdinalIgnoreCase) || text.Contains("userRateLimitExceeded", StringComparison.OrdinalIgnoreCase) || (text.Contains("403", StringComparison.OrdinalIgnoreCase) && text.Contains("rate", StringComparison.OrdinalIgnoreCase)))
		{
			return "Google Drive rate limit reached. Wait a moment, then tap Retry Now.";
		}
		if (text.Contains("401", StringComparison.OrdinalIgnoreCase) || text.Contains("invalid_grant", StringComparison.OrdinalIgnoreCase))
		{
			return "Google Drive session expired. Disconnect and Link Google Account again.";
		}
		if (text.Length <= 280)
		{
			return text;
		}
		return text.Substring(0, 280) + "…";
	}

	private string ResolveBackupPassword()
	{
		string password = _backupSettings.Load().Password;
		if (!string.IsNullOrWhiteSpace(password) && password.Length >= 12)
		{
			return password;
		}
		return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("PharmaBill.Drive." + _storage.RootDirectory)));
	}

	private async Task<ClientSecrets> ResolveClientSecretsAsync(CancellationToken cancellationToken)
	{
		GoogleOAuthClientConfig googleOAuthClientConfig = await _oauthClients.ResolveGoogleAsync(requireConfigured: true, cancellationToken);
		return new ClientSecrets
		{
			ClientId = googleOAuthClientConfig.ClientId,
			ClientSecret = googleOAuthClientConfig.ClientSecret
		};
	}

	private async Task<UserCredential> CreateCredentialAsync(GoogleDriveSyncSettings settings, CancellationToken cancellationToken)
	{
		ClientSecrets clientSecrets = await ResolveClientSecretsAsync(cancellationToken);
		GoogleAuthorizationCodeFlow flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
		{
			ClientSecrets = clientSecrets,
			Scopes = Scopes
		});
		TokenResponse token = new TokenResponse
		{
			RefreshToken = settings.RefreshToken,
			Scope = string.Join(' ', Scopes)
		};
		UserCredential credential = new UserCredential(flow, settings.AccountEmail, token);
		if (!(await credential.RefreshTokenAsync(cancellationToken)))
		{
			throw new InvalidOperationException("Could not refresh the Google Drive token. Connect again.");
		}
		return credential;
	}

	private async Task<DriveService> CreateDriveServiceAsync(GoogleDriveSyncSettings settings, CancellationToken cancellationToken)
	{
		UserCredential httpClientInitializer = await CreateCredentialAsync(settings, cancellationToken);
		return new DriveService(new BaseClientService.Initializer
		{
			HttpClientInitializer = httpClientInitializer,
			ApplicationName = "PharmaBill"
		});
	}

	private static async Task<string> EnsureSyncFolderAsync(DriveService service, CancellationToken cancellationToken)
	{
		FilesResource.ListRequest listRequest = service.Files.List();
		listRequest.Q = "mimeType = 'application/vnd.google-apps.folder' and name = 'PharmaBill_Sync' and trashed = false";
		listRequest.Spaces = "drive";
		listRequest.Fields = "files(id, name)";
		listRequest.PageSize = 5;
		Google.Apis.Drive.v3.Data.File file = (await listRequest.ExecuteAsync(cancellationToken)).Files?.FirstOrDefault();
		if (file?.Id != null)
		{
			return file.Id;
		}
		Google.Apis.Drive.v3.Data.File body = new Google.Apis.Drive.v3.Data.File
		{
			Name = "PharmaBill_Sync",
			MimeType = "application/vnd.google-apps.folder"
		};
		FilesResource.CreateRequest createRequest = service.Files.Create(body);
		createRequest.Fields = "id";
		return (await createRequest.ExecuteAsync(cancellationToken)).Id ?? throw new InvalidOperationException("Google Drive did not return a folder id.");
	}

	public async Task UploadNestedFileAsync(string localFilePath, string remoteRelativePath, CancellationToken cancellationToken = default(CancellationToken))
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(localFilePath, "localFilePath");
		ArgumentException.ThrowIfNullOrWhiteSpace(remoteRelativePath, "remoteRelativePath");
		if (!System.IO.File.Exists(localFilePath))
		{
			throw new FileNotFoundException("Local file was not found.", localFilePath);
		}
		await _gate.WaitAsync(cancellationToken);
		try
		{
			GoogleDriveSyncSettings googleDriveSyncSettings = await _settingsStore.LoadAsync(cancellationToken);
			if (!googleDriveSyncSettings.IsConnected || string.IsNullOrWhiteSpace(googleDriveSyncSettings.RefreshToken))
			{
				throw new InvalidOperationException("Connect Google Drive before uploading branch files.");
			}
			DriveService service = await CreateDriveServiceAsync(googleDriveSyncSettings, cancellationToken);
			string text = await EnsureSyncFolderAsync(service, cancellationToken);
			string[] segments = remoteRelativePath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
			if (segments.Length == 0)
			{
				throw new ArgumentException("Remote path is empty.", "remoteRelativePath");
			}
			string text2 = text;
			for (int i = 0; i < segments.Length - 1; i++)
			{
				text2 = await EnsureChildFolderAsync(service, text2, segments[i], cancellationToken);
			}
			await UploadOrReplaceAsync(service, text2, localFilePath, segments[^1], "Uploading changes to Google Drive…", cancellationToken);
		}
		finally
		{
			_gate.Release();
		}
	}

	public async Task<string?> DownloadNestedFileAsync(string localDestDirectory, string remoteRelativePath, CancellationToken cancellationToken = default(CancellationToken))
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(localDestDirectory, "localDestDirectory");
		ArgumentException.ThrowIfNullOrWhiteSpace(remoteRelativePath, "remoteRelativePath");
		Directory.CreateDirectory(localDestDirectory);
		await _gate.WaitAsync(cancellationToken);
		try
		{
			GoogleDriveSyncSettings googleDriveSyncSettings = await _settingsStore.LoadAsync(cancellationToken);
			if (!googleDriveSyncSettings.IsConnected || string.IsNullOrWhiteSpace(googleDriveSyncSettings.RefreshToken))
			{
				return null;
			}
			DriveService service = await CreateDriveServiceAsync(googleDriveSyncSettings, cancellationToken);
			string text = await EnsureSyncFolderAsync(service, cancellationToken);
			string[] segments = remoteRelativePath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
			if (segments.Length == 0)
			{
				return null;
			}
			string text2 = text;
			for (int i = 0; i < segments.Length - 1; i++)
			{
				string text3 = await FindChildFolderAsync(service, text2, segments[i], cancellationToken);
				if (text3 == null)
				{
					return null;
				}
				text2 = text3;
			}
			Google.Apis.Drive.v3.Data.File file = await FindLatestBackupAsync(service, text2, segments[^1], cancellationToken);
			if (file?.Id == null)
			{
				return null;
			}
			string destination = Path.Combine(localDestDirectory, segments[^1]);
			await using (FileStream output = System.IO.File.Create(destination))
			{
				await service.Files.Get(file.Id).DownloadAsync(output, cancellationToken);
			}
			return destination;
		}
		finally
		{
			_gate.Release();
		}
	}

	public async Task<IReadOnlyList<string>> ListBranchCodesAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		await _gate.WaitAsync(cancellationToken);
		try
		{
			GoogleDriveSyncSettings googleDriveSyncSettings = await _settingsStore.LoadAsync(cancellationToken);
			if (!googleDriveSyncSettings.IsConnected || string.IsNullOrWhiteSpace(googleDriveSyncSettings.RefreshToken))
			{
				return Array.Empty<string>();
			}
			DriveService service = await CreateDriveServiceAsync(googleDriveSyncSettings, cancellationToken);
			string text = await FindChildFolderAsync(service, await EnsureSyncFolderAsync(service, cancellationToken), "Branches", cancellationToken);
			if (text == null)
			{
				return Array.Empty<string>();
			}
			FilesResource.ListRequest listRequest = service.Files.List();
			listRequest.Q = "'" + text + "' in parents and mimeType = 'application/vnd.google-apps.folder' and trashed = false";
			listRequest.Spaces = "drive";
			listRequest.Fields = "files(id, name)";
			listRequest.PageSize = 100;
			return (await listRequest.ExecuteAsync(cancellationToken)).Files?.Select((Google.Apis.Drive.v3.Data.File file) => file.Name).Where((string name) => !string.IsNullOrWhiteSpace(name)).Cast<string>()
				.OrderBy((string name) => name, StringComparer.OrdinalIgnoreCase)
				.ToArray() ?? Array.Empty<string>();
		}
		finally
		{
			_gate.Release();
		}
	}

	private static async Task<string> EnsureChildFolderAsync(DriveService service, string parentId, string folderName, CancellationToken cancellationToken)
	{
		string text = await FindChildFolderAsync(service, parentId, folderName, cancellationToken);
		if (text != null)
		{
			return text;
		}
		Google.Apis.Drive.v3.Data.File body = new Google.Apis.Drive.v3.Data.File
		{
			Name = folderName,
			MimeType = "application/vnd.google-apps.folder",
			Parents = new List<string>(1) { parentId }
		};
		FilesResource.CreateRequest createRequest = service.Files.Create(body);
		createRequest.Fields = "id";
		return (await createRequest.ExecuteAsync(cancellationToken)).Id ?? throw new InvalidOperationException("Could not create Drive folder '" + folderName + "'.");
	}

	private static async Task<string?> FindChildFolderAsync(DriveService service, string parentId, string folderName, CancellationToken cancellationToken)
	{
		string value = folderName.Replace("'", "\\'", StringComparison.Ordinal);
		FilesResource.ListRequest listRequest = service.Files.List();
		listRequest.Q = $"'{parentId}' in parents and mimeType = 'application/vnd.google-apps.folder' and name = '{value}' and trashed = false";
		listRequest.Spaces = "drive";
		listRequest.Fields = "files(id, name)";
		listRequest.PageSize = 5;
		return (await listRequest.ExecuteAsync(cancellationToken)).Files?.FirstOrDefault()?.Id;
	}

	private async Task UploadOrReplaceAsync(DriveService service, string folderId, string localPath, string remoteFileName, string progressMessage, CancellationToken cancellationToken)
	{
		long totalBytes = Math.Max(0L, new FileInfo(localPath).Length);
		Google.Apis.Drive.v3.Data.File file = await FindLatestBackupAsync(service, folderId, remoteFileName, cancellationToken);
		await using (FileStream stream = System.IO.File.OpenRead(localPath))
		{
			if (file?.Id != null)
			{
				Google.Apis.Drive.v3.Data.File body = new Google.Apis.Drive.v3.Data.File
				{
					Name = remoteFileName
				};
				FilesResource.UpdateMediaUpload updateMediaUpload = service.Files.Update(body, file.Id, stream, "application/octet-stream");
				updateMediaUpload.Fields = "id, modifiedTime";
				updateMediaUpload.ProgressChanged += OnUploadProgress;
				IUploadProgress uploadProgress = await updateMediaUpload.UploadAsync(cancellationToken);
				if (uploadProgress.Status != UploadStatus.Completed)
				{
					throw new InvalidOperationException(uploadProgress.Exception?.Message ?? "Google Drive upload failed.");
				}
				Report(99, progressMessage, totalBytes, totalBytes);
				return;
			}
			Google.Apis.Drive.v3.Data.File body2 = new Google.Apis.Drive.v3.Data.File
			{
				Name = remoteFileName,
				Parents = new List<string>(1) { folderId }
			};
			FilesResource.CreateMediaUpload createMediaUpload = service.Files.Create(body2, stream, "application/octet-stream");
			createMediaUpload.Fields = "id, modifiedTime";
			createMediaUpload.ProgressChanged += OnUploadProgress;
			IUploadProgress uploadProgress2 = await createMediaUpload.UploadAsync(cancellationToken);
			if (uploadProgress2.Status != UploadStatus.Completed)
			{
				throw new InvalidOperationException(uploadProgress2.Exception?.Message ?? "Google Drive upload failed.");
			}
			Report(99, progressMessage, totalBytes, totalBytes);
		}
		void OnUploadProgress(IUploadProgress progress)
		{
			switch (progress.Status)
			{
			case UploadStatus.Starting:
				Report(Math.Max(40, PercentOf(progress.BytesSent, totalBytes)), progressMessage, progress.BytesSent, totalBytes);
				break;
			case UploadStatus.Uploading:
			{
				int percent = ((totalBytes <= 0) ? 50 : (40 + (int)Math.Clamp((double)progress.BytesSent * 59.0 / (double)totalBytes, 0.0, 59.0)));
				Report(percent, progressMessage, progress.BytesSent, totalBytes);
				break;
			}
			case UploadStatus.Failed:
			{
				string message = ToUploadErrorMessage(progress.Exception ?? new InvalidOperationException("Google Drive upload failed."));
				Report(PercentOf(progress.BytesSent, totalBytes), message, progress.BytesSent, totalBytes, isError: true);
				break;
			}
			case UploadStatus.Completed:
				break;
			}
		}
	}

	private static int PercentOf(long bytesSent, long totalBytes)
	{
		if (totalBytes <= 0)
		{
			return 0;
		}
		return (int)Math.Clamp((double)bytesSent * 100.0 / (double)totalBytes, 0.0, 100.0);
	}

	private static async Task<Google.Apis.Drive.v3.Data.File?> FindLatestBackupAsync(DriveService service, string folderId, string remoteFileName, CancellationToken cancellationToken)
	{
		string value = remoteFileName.Replace("'", "\\'", StringComparison.Ordinal);
		FilesResource.ListRequest listRequest = service.Files.List();
		listRequest.Q = $"'{folderId}' in parents and name = '{value}' and trashed = false";
		listRequest.Spaces = "drive";
		listRequest.Fields = "files(id, name, modifiedTime, size)";
		listRequest.OrderBy = "modifiedTime desc";
		listRequest.PageSize = 1;
		return (await listRequest.ExecuteAsync(cancellationToken)).Files?.FirstOrDefault();
	}

	private static async Task<string> TryReadAccountEmailAsync(UserCredential credential, string preferredEmail, CancellationToken cancellationToken)
	{
		if (!string.IsNullOrWhiteSpace(preferredEmail) && preferredEmail.Contains('@'))
		{
			return preferredEmail.Trim();
		}
		try
		{
			using HttpClient http = new HttpClient();
			http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", credential.Token.AccessToken);
			using JsonDocument jsonDocument = JsonDocument.Parse(await http.GetStringAsync("https://www.googleapis.com/oauth2/v2/userinfo", cancellationToken));
			if (jsonDocument.RootElement.TryGetProperty("email", out var value))
			{
				return value.GetString() ?? preferredEmail;
			}
		}
		catch
		{
		}
		return string.IsNullOrWhiteSpace(preferredEmail) ? "Google account" : preferredEmail.Trim();
	}

	private void TryDeleteTokenCache()
	{
		try
		{
			string path = Path.Combine(_storage.RootDirectory, "Google.Apis.Auth");
			if (Directory.Exists(path))
			{
				Directory.Delete(path, recursive: true);
			}
		}
		catch
		{
		}
	}

	private static void TryDeleteFile(string path)
	{
		try
		{
			if (System.IO.File.Exists(path))
			{
				System.IO.File.Delete(path);
			}
		}
		catch
		{
		}
	}
}
