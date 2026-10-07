using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Windows.ApplicationModel;
using Windows.Services.Store;

namespace PharmaBill.App.Services;

public sealed class AppUpdateService : IAppUpdateService, IDisposable
{
	private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
	{
		PropertyNameCaseInsensitive = true
	};

	private readonly AppVersionInfo _versionInfo;

	private readonly UpdateCheckPreferencesStore _preferences;

	private readonly ILogger<AppUpdateService> _logger;

	private readonly HttpClient _httpClient;

	private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);

	private StoreContext? _storeContext;

	private IReadOnlyList<StorePackageUpdate>? _pendingStoreUpdates;

	public AppUpdateAvailability? LastResult { get; private set; }

	public event EventHandler<AppUpdateAvailability>? UpdateStateChanged;

	public AppUpdateService(AppVersionInfo versionInfo, UpdateCheckPreferencesStore preferences, ILogger<AppUpdateService> logger)
	{
		_versionInfo = versionInfo;
		_preferences = preferences;
		_logger = logger;
		_httpClient = new HttpClient
		{
			Timeout = TimeSpan.FromSeconds(12L)
		};
	}

	public bool IsSnoozed()
	{
		DateTimeOffset? remindAfterUtc = _preferences.Load().RemindAfterUtc;
		if (remindAfterUtc.HasValue)
		{
			DateTimeOffset valueOrDefault = remindAfterUtc.GetValueOrDefault();
			return valueOrDefault > DateTimeOffset.UtcNow;
		}
		return false;
	}

	public void RemindTomorrow()
	{
		UpdateCheckPreferences updateCheckPreferences = _preferences.Load();
		_preferences.Save(updateCheckPreferences with
		{
			RemindAfterUtc = DateTimeOffset.UtcNow.AddHours(24.0)
		});
	}

	public async Task<AppUpdateAvailability> CheckForUpdatesAsync(bool force = false, CancellationToken cancellationToken = default(CancellationToken))
	{
		await _gate.WaitAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		try
		{
			UpdateCheckPreferences prefs = _preferences.Load();
			if (!force)
			{
				DateTimeOffset? lastCheckedUtc = prefs.LastCheckedUtc;
				if (lastCheckedUtc.HasValue)
				{
					DateTimeOffset valueOrDefault = lastCheckedUtc.GetValueOrDefault();
					if (DateTimeOffset.UtcNow - valueOrDefault < TimeSpan.FromHours(24) && (object)LastResult != null)
					{
						return LastResult;
					}
				}
			}
			AppUpdateAvailability appUpdateAvailability2;
			try
			{
				AppUpdateAvailability appUpdateAvailability = await TryStoreChannelAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
				if ((object)appUpdateAvailability == null)
				{
					appUpdateAvailability = await TryManifestChannelAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
				}
				appUpdateAvailability2 = appUpdateAvailability;
			}
			catch (Exception ex)
			{
				_logger.LogDebug(ex, "Update check failed; app continues offline.");
				appUpdateAvailability2 = new AppUpdateAvailability(IsUpdateAvailable: false, _versionInfo.Version, _versionInfo.Version, IsCritical: false, UpdateChannel.None, null, "Could not check for updates (offline or unreachable). You can keep using PharmaBill.", ex.Message);
			}
			_preferences.Save(prefs with
			{
				LastCheckedUtc = DateTimeOffset.UtcNow,
				LastKnownLatestVersion = appUpdateAvailability2.LatestVersion
			});
			LastResult = appUpdateAvailability2;
			UpdateStateChanged?.Invoke(this, appUpdateAvailability2);
			return appUpdateAvailability2;
		}
		finally
		{
			_gate.Release();
		}
	}

	public async Task LaunchUpdateAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		AppUpdateAvailability appUpdateAvailability = LastResult;
		if ((object)appUpdateAvailability == null)
		{
			appUpdateAvailability = await CheckForUpdatesAsync(force: true, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		}
		AppUpdateAvailability result = appUpdateAvailability;
		if (!result.IsUpdateAvailable)
		{
			return;
		}
		if (result.Channel == UpdateChannel.MicrosoftStore)
		{
			IReadOnlyList<StorePackageUpdate> pendingStoreUpdates = _pendingStoreUpdates;
			if (pendingStoreUpdates != null && pendingStoreUpdates.Count > 0 && (object)_storeContext != null)
			{
				try
				{
					await _storeContext.RequestDownloadAndInstallStorePackageUpdatesAsync(_pendingStoreUpdates).AsTask(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
					return;
				}
				catch (Exception exception)
				{
					_logger.LogInformation(exception, "Store install request failed; opening Store PDP.");
				}
			}
		}
		string text = result.DownloadOrStoreUrl;
		if (string.IsNullOrWhiteSpace(text))
		{
			text = "https://github.com/SAER-PharmaBill/PharmaBill/releases/latest";
		}
		Process.Start(new ProcessStartInfo(text)
		{
			UseShellExecute = true
		});
	}

	private async Task<AppUpdateAvailability?> TryStoreChannelAsync(CancellationToken cancellationToken)
	{
		try
		{
			if ((object)_storeContext == null)
			{
				_storeContext = StoreContext.GetDefault();
			}
			IReadOnlyList<StorePackageUpdate> readOnlyList = await _storeContext.GetAppAndOptionalStorePackageUpdatesAsync().AsTask(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
			if (readOnlyList == null || readOnlyList.Count == 0)
			{
				_pendingStoreUpdates = null;
				return null;
			}
			_pendingStoreUpdates = readOnlyList.ToList();
			string text = (from item in readOnlyList
				select item.Package?.Id?.Version into version
				where version.HasValue
				select new Version(version.Value.Major, version.Value.Minor, version.Value.Build, version.Value.Revision)).DefaultIfEmpty(ParseVersion(_versionInfo.Version)).Max().ToString(3);
			string text2 = (string.IsNullOrWhiteSpace("") ? null : "");
			string downloadOrStoreUrl = (string.IsNullOrWhiteSpace(text2) ? null : ("ms-windows-store://pdp/?productid=" + text2));
			return new AppUpdateAvailability(IsUpdateAvailable: true, _versionInfo.Version, text, IsCritical: false, UpdateChannel.MicrosoftStore, downloadOrStoreUrl, "A new version of PharmaBill (v" + text + ") is available from the Microsoft Store.", "Install from the Store to keep billing and sync compatible.");
		}
		catch (Exception exception)
		{
			_logger.LogDebug(exception, "Microsoft Store update query unavailable; using remote manifest.");
			_pendingStoreUpdates = null;
			return null;
		}
	}

	private async Task<AppUpdateAvailability> TryManifestChannelAsync(CancellationToken cancellationToken)
	{
		ReleaseManifest releaseManifest = await LoadManifestAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		if (releaseManifest == null)
		{
			return new AppUpdateAvailability(IsUpdateAvailable: false, _versionInfo.Version, _versionInfo.Version, IsCritical: false, UpdateChannel.None, null, "You have " + _versionInfo.Display + ". No update feed responded; PharmaBill stays fully usable offline.");
		}
		Version version = ParseVersion(_versionInfo.Version);
		Version version2 = ParseVersion(releaseManifest.LatestVersion);
		Version version3 = ParseVersion(releaseManifest.MinSupportedVersion ?? "0.0.0");
		bool flag = version2 > version;
		bool flag2 = version < version3;
		bool isCritical = releaseManifest.IsCritical | flag2;
		string text = FirstNonEmpty(releaseManifest.MicrosoftStoreProductId, "");
		string text2 = (string.IsNullOrWhiteSpace(text) ? null : ("ms-windows-store://pdp/?productid=" + text));
		string downloadOrStoreUrl = FirstNonEmpty(releaseManifest.DownloadUrl, "https://github.com/SAER-PharmaBill/PharmaBill/releases/latest", text2);
		if (!flag)
		{
			return new AppUpdateAvailability(IsUpdateAvailable: false, _versionInfo.Version, NormalizeVersionLabel(releaseManifest.LatestVersion), IsCritical: false, UpdateChannel.RemoteManifest, downloadOrStoreUrl, "Up to date ✓", releaseManifest.Message);
		}
		return new AppUpdateAvailability(IsUpdateAvailable: true, _versionInfo.Version, NormalizeVersionLabel(releaseManifest.LatestVersion), isCritical, UpdateChannel.RemoteManifest, downloadOrStoreUrl, "A new version of PharmaBill (v" + NormalizeVersionLabel(releaseManifest.LatestVersion) + ") is available with the latest features and bug fixes.", releaseManifest.Message);
	}

	private async Task<ReleaseManifest?> LoadManifestAsync(CancellationToken cancellationToken)
	{
		foreach (string url in EnumerateManifestUrls())
		{
			try
			{
				using (HttpResponseMessage response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(continueOnCapturedContext: false))
				{
					if (!response.IsSuccessStatusCode)
					{
						goto end_IL_00e9;
					}
					await using (Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false))
					{
						ReleaseManifest releaseManifest = await JsonSerializer.DeserializeAsync<ReleaseManifest>(stream, JsonOptions, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
						if (releaseManifest != null && !string.IsNullOrWhiteSpace(releaseManifest.LatestVersion))
						{
							return releaseManifest;
						}
					}
					ReleaseManifest releaseManifest2 = null;
					goto end_IL_0051;
					end_IL_00e9:;
				}
				end_IL_0051:;
			}
			catch (Exception exception)
			{
				_logger.LogDebug(exception, "Release manifest URL failed: {Url}", url);
			}
		}
		foreach (string url in EnumerateLocalManifestPaths())
		{
			try
			{
				if (File.Exists(url))
				{
					ReleaseManifest releaseManifest3 = JsonSerializer.Deserialize<ReleaseManifest>(await File.ReadAllTextAsync(url, cancellationToken).ConfigureAwait(continueOnCapturedContext: false), JsonOptions);
					if (releaseManifest3 != null && !string.IsNullOrWhiteSpace(releaseManifest3.LatestVersion))
					{
						return releaseManifest3;
					}
				}
			}
			catch (Exception exception2)
			{
				_logger.LogDebug(exception2, "Local release manifest failed: {Path}", url);
			}
		}
		return null;
	}

	private static IEnumerable<string> EnumerateManifestUrls()
	{
		if (!string.IsNullOrWhiteSpace("https://raw.githubusercontent.com/SAER-PharmaBill/PharmaBill/main/docs/release-manifest.json"))
		{
			yield return "https://raw.githubusercontent.com/SAER-PharmaBill/PharmaBill/main/docs/release-manifest.json";
		}
	}

	private static IEnumerable<string> EnumerateLocalManifestPaths()
	{
		string baseDir = AppContext.BaseDirectory;
		yield return Path.Combine(baseDir, "docs", "release-manifest.json");
		yield return Path.Combine(baseDir, "release-manifest.json");
	}

	public static Version ParseVersion(string? raw)
	{
		string text = (raw ?? "0.0.0").Trim().TrimStart(new char[2] { 'v', 'V' });
		if (Version.TryParse(text, out Version result))
		{
			return result;
		}
		int[] array = (from part in text.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
			select int.TryParse(part, out var result2) ? result2 : 0).Take(4).ToArray();
		return array.Length switch
		{
			0 => new Version(0, 0, 0), 
			1 => new Version(array[0], 0, 0), 
			2 => new Version(array[0], array[1], 0), 
			3 => new Version(array[0], array[1], array[2]), 
			_ => new Version(array[0], array[1], array[2], array[3]), 
		};
	}

	private static string NormalizeVersionLabel(string raw)
	{
		return ParseVersion(raw).ToString(3);
	}

	private static string? FirstNonEmpty(params string?[] values)
	{
		return values.FirstOrDefault((string value) => !string.IsNullOrWhiteSpace(value));
	}

	public void Dispose()
	{
		_gate.Dispose();
		_httpClient.Dispose();
	}
}
