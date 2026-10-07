using System;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using Makaretu.Dns;
using Microsoft.Extensions.Logging;

namespace PharmaBill.Sync;

public sealed class LanSyncMdnsAdvertiser(ILogger<LanSyncMdnsAdvertiser> logger) : IAsyncDisposable
{
	public const string ServiceType = "_pharmabill-sync._tcp";

	private readonly object _gate = new object();

	private MulticastService? _mdns;

	private ServiceDiscovery? _discovery;

	private ServiceProfile? _profile;

	private bool _running;

	public bool IsAdvertising
	{
		get
		{
			lock (_gate)
			{
				return _running;
			}
		}
	}

	public string? InstanceName { get; private set; }

	public void Start(string deviceDisplayName, int port, Guid deviceId)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(port, "port");
		string text = SanitizeInstanceName(deviceDisplayName);
		lock (_gate)
		{
			StopCore();
			try
			{
				_mdns = new MulticastService();
				_discovery = new ServiceDiscovery(_mdns);
				_profile = new ServiceProfile(text, "_pharmabill-sync._tcp", (ushort)port);
				_profile.AddProperty("txtvers", "1");
				_profile.AddProperty("deviceId", deviceId.ToString("D"));
				_profile.AddProperty("path", "/api/sync");
				_profile.AddProperty("ping", "/api/sync/ping");
				_mdns.Start();
				_discovery.Advertise(_profile);
				_discovery.Announce(_profile);
				_running = true;
				InstanceName = text;
				logger.LogInformation("Advertising LAN sync as {Instance}.{Service} on port {Port}.", text, "_pharmabill-sync._tcp", port);
			}
			catch (Exception exception)
			{
				StopCore();
				logger.LogWarning(exception, "mDNS advertisement failed; QR / IP pairing still works.");
			}
		}
	}

	public void Stop()
	{
		lock (_gate)
		{
			StopCore();
		}
	}

	public ValueTask DisposeAsync()
	{
		Stop();
		return ValueTask.CompletedTask;
	}

	private void StopCore()
	{
		try
		{
			if (_discovery != null && _profile != null)
			{
				try
				{
					_discovery.Unadvertise(_profile);
				}
				catch
				{
				}
			}
			_discovery?.Dispose();
			_mdns?.Stop();
			_mdns?.Dispose();
		}
		catch (Exception exception)
		{
			logger.LogDebug(exception, "mDNS stop ignored.");
		}
		finally
		{
			_discovery = null;
			_mdns = null;
			_profile = null;
			_running = false;
			InstanceName = null;
		}
	}

	private static string SanitizeInstanceName(string? preferred)
	{
		string text = (string.IsNullOrWhiteSpace(preferred) ? Environment.MachineName : preferred.Trim());
		try
		{
			if (string.IsNullOrWhiteSpace(text))
			{
				text = IPGlobalProperties.GetIPGlobalProperties().HostName;
			}
		}
		catch
		{
		}
		if (string.IsNullOrWhiteSpace(text))
		{
			text = "PharmaBill";
		}
		Span<char> span = stackalloc char[Math.Min(text.Length, 48)];
		int num = 0;
		string text2 = text;
		foreach (char c in text2)
		{
			if (num >= span.Length)
			{
				break;
			}
			bool flag = char.IsLetterOrDigit(c);
			if (!flag)
			{
				bool flag2 = ((c == '-' || c == '_') ? true : false);
				flag = flag2;
			}
			if (flag)
			{
				span[num++] = c;
			}
			else if ((char.IsWhiteSpace(c) || c == '.') && num > 0 && span[num - 1] != '-')
			{
				span[num++] = '-';
			}
		}
		string text3 = ((num == 0) ? "PharmaBill" : new string(span.Slice(0, num)).Trim('-'));
		if (!string.IsNullOrWhiteSpace(text3))
		{
			return text3;
		}
		return "PharmaBill";
	}
}
