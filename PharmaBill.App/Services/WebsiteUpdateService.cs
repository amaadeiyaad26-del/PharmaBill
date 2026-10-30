using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using Microsoft.Extensions.Logging;

namespace PharmaBill.App.Services;

/// <summary>
/// Local Market edition: asks https://pharmaremit.com/updates/latest.json whether a newer version exists. A plain GET of
/// a static file: no identifiers, cookies or headers of ours are sent. At most once a day (the time is kept in
/// update-check-preferences.json) unless the user asks; five seconds at most; offline it says nothing on screen.
/// Nothing is ever downloaded or run from here: an update opens the download page in the browser.
/// </summary>
public sealed class WebsiteUpdateService : IAppUpdateService, IDisposable
{
	public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

	public static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);

	private readonly string _currentVersion;

	private readonly UpdateCheckPreferencesStore _preferences;

	private readonly ILogger<WebsiteUpdateService> _logger;

	private readonly HttpClient _httpClient;

	private readonly Func<DateTimeOffset> _now;

	private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);

	/// <summary>Opens a link in the default browser. Tests replace it.</summary>
	internal Action<string> OpenInBrowser { get; set; } = url => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

	public AppUpdateAvailability? LastResult { get; private set; }

	public event EventHandler<AppUpdateAvailability>? UpdateStateChanged;

	public WebsiteUpdateService(AppVersionInfo versionInfo, UpdateCheckPreferencesStore preferences, ILogger<WebsiteUpdateService> logger)
		: this(versionInfo.Version, preferences, logger, null, null)
	{
	}

	internal WebsiteUpdateService(string currentVersion, UpdateCheckPreferencesStore preferences, ILogger<WebsiteUpdateService> logger, HttpMessageHandler? handler, Func<DateTimeOffset>? now)
	{
		_currentVersion = currentVersion;
		_preferences = preferences;
		_logger = logger;
		_now = now ?? (() => DateTimeOffset.UtcNow);
		// No cookies, no redirects (a redirect could lead off https), no proxy credentials of ours.
		_httpClient = new HttpClient(handler ?? new SocketsHttpHandler { UseCookies = false, AllowAutoRedirect = false, AutomaticDecompression = DecompressionMethods.None }, disposeHandler: true)
		{
			Timeout = Timeout
		};
	}

	public void Dismiss(string version)
	{
		_preferences.Save(_preferences.Load() with { DismissedVersion = version });
	}

	public bool IsDismissed(string version)
	{
		string? dismissed = _preferences.Load().DismissedVersion;
		return dismissed != null && UpdateFeed.TryParseVersion(dismissed, out _) && UpdateFeed.TryParseVersion(version, out _)
			&& UpdateFeed.CompareVersions(dismissed, version) >= 0;
	}

	public async Task<AppUpdateAvailability> CheckForUpdatesAsync(bool force = false, CancellationToken cancellationToken = default(CancellationToken))
	{
		await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			UpdateCheckPreferences prefs = _preferences.Load();
			DateTimeOffset now = _now();
			bool checkedRecently = prefs.LastCheckedUtc is DateTimeOffset last && last <= now && now - last < CheckInterval;
			AppUpdateAvailability result;
			if (!force && checkedRecently)
			{
				// Asked within the last day: no request; what the last answer said still stands.
				result = FromFeed(UpdateFeed.Parse(prefs.LastFeedJson));
			}
			else
			{
				(bool reached, string? json) = await FetchAsync(cancellationToken).ConfigureAwait(false);
				prefs = prefs with { LastCheckedUtc = now };
				if (reached)
				{
					prefs = prefs with { LastFeedJson = UpdateFeed.Parse(json) == null ? null : json };
					result = FromFeed(UpdateFeed.Parse(json));
				}
				else
				{
					result = Unreachable();
				}
				TrySave(prefs);
			}
			LastResult = result;
			UpdateStateChanged?.Invoke(this, result);
			return result;
		}
		finally
		{
			_gate.Release();
		}
	}

	public Task LaunchUpdateAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		// Only a link that passed the feed's checks (https), opened in the browser. Nothing is downloaded or started here.
		if (LastResult?.Release is PublishedRelease release && UpdateFeed.IsSafeDownloadUrl(release.DownloadUrl))
		{
			OpenInBrowser(release.DownloadUrl);
		}
		return Task.CompletedTask;
	}

	/// <summary>
	/// The file's text, or reached = false when the server could not be reached (offline, timeout, server error).
	/// A missing file (404) is reached with no text: that reads as "no update".
	/// </summary>
	private async Task<(bool Reached, string? Json)> FetchAsync(CancellationToken cancellationToken)
	{
		using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeout.CancelAfter(Timeout);
		try
		{
			using var request = new HttpRequestMessage(HttpMethod.Get, UpdateFeed.Url);
			using HttpResponseMessage response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
			if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
			{
				return (true, null);
			}
			if (!response.IsSuccessStatusCode)
			{
				_logger.LogDebug("Update server answered {Status}.", (int)response.StatusCode);
				return (false, null);
			}
			if (response.Content.Headers.ContentLength > UpdateFeed.MaxBytes)
			{
				return (true, null);
			}
			await using Stream stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
			byte[] buffer = new byte[UpdateFeed.MaxBytes + 1];
			int total = 0;
			int read;
			while (total < buffer.Length && (read = await stream.ReadAsync(buffer.AsMemory(total), timeout.Token).ConfigureAwait(false)) > 0)
			{
				total += read;
			}
			return total > UpdateFeed.MaxBytes ? (true, null) : (true, Encoding.UTF8.GetString(buffer, 0, total));
		}
		catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or OperationCanceledException or IOException)
		{
			cancellationToken.ThrowIfCancellationRequested();
			_logger.LogDebug(exception, "Update server not reached; nothing is shown.");
			return (false, null);
		}
	}

	private AppUpdateAvailability FromFeed(PublishedRelease? release)
	{
		string current = _currentVersion;
		if (release == null || !UpdateFeed.IsNewer(release.Version, current))
		{
			return new AppUpdateAvailability(IsUpdateAvailable: false, current, release?.Version ?? current, IsCritical: false, UpdateChannel.Website, null,
				$"You're on the latest version of Pharma Remit ({current}).");
		}
		return new AppUpdateAvailability(IsUpdateAvailable: true, current, release.Version, IsCritical: false, UpdateChannel.Website, release.DownloadUrl,
			$"A newer version of Pharma Remit is available: {release.Version}, released {release.ReleasedText} ({release.SizeText} download).",
			Release: release);
	}

	private AppUpdateAvailability Unreachable() =>
		new(IsUpdateAvailable: false, _currentVersion, _currentVersion, IsCritical: false, UpdateChannel.None, null,
			"Could not reach the update server. Check the internet connection and try again; Pharma Remit works fully offline.");

	private void TrySave(UpdateCheckPreferences prefs)
	{
		try
		{
			_preferences.Save(prefs);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			_logger.LogDebug(exception, "Could not save the update check time.");
		}
	}

	public void Dispose()
	{
		_gate.Dispose();
		_httpClient.Dispose();
	}
}
