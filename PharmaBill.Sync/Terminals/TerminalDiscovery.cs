using System.Collections.Concurrent;
using System.Net;
using Makaretu.Dns;
using Microsoft.Extensions.Logging;

namespace PharmaBill.Sync.Terminals;

/// <summary>A server PC found on the shop network. Discovery only suggests where to connect: trust comes from
/// pairing, which checks the server's certificate against the code on its screen.</summary>
public sealed record DiscoveredTerminalServer(string Name, string Host, int Port, string? ShopName, string? FingerprintPrefix);

/// <summary>Advertises this PC's billing-counter server on the shop network (mDNS / DNS-SD).</summary>
internal sealed class TerminalAdvertiser(ILogger logger)
{
	public const string ServiceType = "_pharmaremit-pos._tcp";

	private readonly object _gate = new object();

	private MulticastService? _mdns;

	private ServiceDiscovery? _discovery;

	private ServiceProfile? _profile;

	public void Start(int port, string fingerprint)
	{
		lock (_gate)
		{
			StopLocked();
			IPAddress[] lan = LanListener.Addresses().Where(address => !IPAddress.IsLoopback(address)).ToArray();
			if (lan.Length == 0)
			{
				return;
			}
			try
			{
				_mdns = new MulticastService();
				_discovery = new ServiceDiscovery(_mdns);
				_profile = new ServiceProfile(Environment.MachineName, ServiceType, (ushort)port, lan);
				_profile.AddProperty("txtvers", "1");
				_profile.AddProperty("protocol", TerminalProtocol.Version.ToString());
				_profile.AddProperty("fp", fingerprint[..16]);
				_mdns.Start();
				_discovery.Advertise(_profile);
				_discovery.Announce(_profile);
			}
			catch (Exception exception)
			{
				StopLocked();
				logger.LogWarning(exception, "Billing-counter discovery could not be advertised; terminals can still connect by address.");
			}
		}
	}

	public void Stop()
	{
		lock (_gate)
		{
			StopLocked();
		}
	}

	private void StopLocked()
	{
		try
		{
			if (_discovery != null && _profile != null)
			{
				_discovery.Unadvertise(_profile);
			}
			_discovery?.Dispose();
			_mdns?.Stop();
			_mdns?.Dispose();
		}
		catch
		{
			// Shutting down.
		}
		finally
		{
			_discovery = null;
			_mdns = null;
			_profile = null;
		}
	}
}

/// <summary>Finds billing-counter servers on the shop network.</summary>
public static class TerminalDiscovery
{
	public static async Task<IReadOnlyList<DiscoveredTerminalServer>> FindAsync(TimeSpan listenFor, CancellationToken cancellationToken = default)
	{
		var found = new ConcurrentDictionary<string, DiscoveredTerminalServer>(StringComparer.OrdinalIgnoreCase);
		var ports = new ConcurrentDictionary<string, (string Target, int Port)>(StringComparer.OrdinalIgnoreCase);
		var hosts = new ConcurrentDictionary<string, IPAddress>(StringComparer.OrdinalIgnoreCase);
		var texts = new ConcurrentDictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
		using var mdns = new MulticastService();
		using var discovery = new ServiceDiscovery(mdns);

		void Absorb(Message message, IPEndPoint? from)
		{
			foreach (ResourceRecord record in message.Answers.Concat(message.AdditionalRecords))
			{
				switch (record)
				{
					case SRVRecord srv when srv.Name.ToString().Contains(TerminalAdvertiser.ServiceType, StringComparison.OrdinalIgnoreCase):
						ports[srv.Name.ToString()] = (srv.Target.ToString(), srv.Port);
						break;
					case TXTRecord txt when txt.Name.ToString().Contains(TerminalAdvertiser.ServiceType, StringComparison.OrdinalIgnoreCase):
						texts[txt.Name.ToString()] = txt.Strings.Select(item => item.Split('=', 2)).Where(pair => pair.Length == 2)
							.GroupBy(pair => pair[0]).ToDictionary(group => group.Key, group => group.First()[1]);
						break;
					case ARecord a:
						hosts[a.Name.ToString()] = a.Address;
						break;
				}
			}
			foreach ((string instance, (string target, int port)) in ports)
			{
				IPAddress? address = hosts.TryGetValue(target, out IPAddress? known) ? known : from?.Address;
				if (address == null || !LocalNetworkInfo.IsPrivate(address))
				{
					continue;
				}
				texts.TryGetValue(instance, out Dictionary<string, string>? properties);
				string name = instance.Split('.')[0];
				found[instance] = new DiscoveredTerminalServer(name, address.ToString(), port, null, properties?.GetValueOrDefault("fp"));
			}
		}

		mdns.AnswerReceived += (_, e) => Absorb(e.Message, e.RemoteEndPoint);
		discovery.ServiceInstanceDiscovered += (_, e) =>
		{
			Absorb(e.Message, e.RemoteEndPoint);
			mdns.SendQuery(e.ServiceInstanceName, type: DnsType.SRV);
			mdns.SendQuery(e.ServiceInstanceName, type: DnsType.TXT);
		};
		mdns.Start();
		discovery.QueryServiceInstances(TerminalAdvertiser.ServiceType);
		try
		{
			await Task.Delay(listenFor, cancellationToken);
		}
		finally
		{
			mdns.Stop();
		}
		return found.Values.OrderBy(server => server.Name, StringComparer.OrdinalIgnoreCase).ToArray();
	}
}
