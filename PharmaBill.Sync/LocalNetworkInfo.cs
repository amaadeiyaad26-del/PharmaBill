using System;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace PharmaBill.Sync;

public static class LocalNetworkInfo
{
	private static readonly string[] VirtualHints =
	[
		"virtual", "vethernet", "vmware", "virtualbox", "vbox", "hyper-v", "hyperv",
		"wsl", "docker", "vpn", "tap", "tun", "wireguard", "nordlynx", "zerotier",
		"hamachi", "radmin", "npcap", "loopback", "pseudo"
	];

	public static string? GetLocalIPv4()
	{
		try
		{
			return (from item in (from nic in NetworkInterface.GetAllNetworkInterfaces().Where(IsCandidateNic)
					select new
					{
						Nic = nic,
						Properties = nic.GetIPProperties()
					}).SelectMany(item => from address in item.Properties.UnicastAddresses
					where address.Address.AddressFamily == AddressFamily.InterNetwork
						&& !IPAddress.IsLoopback(address.Address)
						&& !IsLinkLocal(address.Address)
					select new
					{
						Address = address.Address,
						Score = Score(item.Nic, item.Properties, address.Address)
					})
				where item.Score > 0
				orderby item.Score descending
				select item.Address.ToString()).FirstOrDefault();
		}
		catch (NetworkInformationException)
		{
			return null;
		}
	}

	// The address the phone listeners bind to: the advertised LAN address, only if it is a private one
	// (10/8, 172.16/12, 192.168/16). A PC plugged straight into the internet gets no LAN listener.
	public static IPAddress? GetLanIPv4Address()
	{
		return IPAddress.TryParse(GetLocalIPv4(), out IPAddress? address) && IsPrivate(address) ? address : null;
	}

	public static bool IsPrivate(IPAddress address)
	{
		byte[] bytes = address.GetAddressBytes();
		if (address.AddressFamily != AddressFamily.InterNetwork || bytes.Length != 4)
		{
			return false;
		}
		return bytes[0] == 10
			|| (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
			|| (bytes[0] == 192 && bytes[1] == 168);
	}

	/// <summary>True when Windows has marked a network this PC is connected to as Public. Phone and counter
	/// listeners are allowed through the firewall on Private networks only, so on a Public one they are blocked.
	/// Null when Windows cannot say.</summary>
	public static bool? IsOnPublicNetwork()
	{
		if (!OperatingSystem.IsWindows())
		{
			return null;
		}
		try
		{
			// Network List Manager: category 0 = Public, 1 = Private, 2 = Domain.
			Type? type = Type.GetTypeFromCLSID(new Guid("DCB00C01-570F-4A9B-8D69-199FDBA5723B"));
			if (type == null)
			{
				return null;
			}
			dynamic manager = Activator.CreateInstance(type)!;
			bool anyConnected = false;
			foreach (dynamic network in manager.GetNetworks(1))
			{
				anyConnected = true;
				if ((int)network.GetCategory() == 0)
				{
					return true;
				}
			}
			return anyConnected ? false : null;
		}
		catch
		{
			return null;
		}
	}

	private static bool IsCandidateNic(NetworkInterface nic)
	{
		if (nic.OperationalStatus != OperationalStatus.Up)
		{
			return false;
		}

		NetworkInterfaceType type = nic.NetworkInterfaceType;
		if (type is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
		{
			return false;
		}

		return !LooksVirtual(nic);
	}

	private static bool LooksVirtual(NetworkInterface nic)
	{
		string haystack = (nic.Name + " " + nic.Description).ToLowerInvariant();
		return VirtualHints.Any(hint => haystack.Contains(hint, StringComparison.Ordinal));
	}

	private static bool IsLinkLocal(IPAddress address)
	{
		byte[] addressBytes = address.GetAddressBytes();
		return addressBytes.Length >= 2 && addressBytes[0] == 169 && addressBytes[1] == 254;
	}

	private static int Score(NetworkInterface nic, IPInterfaceProperties properties, IPAddress address)
	{
		if (LooksVirtual(nic))
		{
			return -1000;
		}

		int score = 0;
		bool hasGateway = properties.GatewayAddresses.Any(gateway =>
			gateway.Address.AddressFamily == AddressFamily.InterNetwork
			&& !gateway.Address.Equals(IPAddress.Any)
			&& !IPAddress.IsLoopback(gateway.Address));
		if (!hasGateway)
		{
			// Prefer real LAN adapters that can route; host-only / VM NICs often have no gateway.
			score -= 40;
		}
		else
		{
			score += 100;
		}

		score += nic.NetworkInterfaceType switch
		{
			NetworkInterfaceType.Wireless80211 => 40,
			NetworkInterfaceType.Ethernet => 30,
			NetworkInterfaceType.GigabitEthernet => 30,
			_ => 0
		};

		byte[] bytes = address.GetAddressBytes();
		if (bytes.Length >= 2 && bytes[0] == 192 && bytes[1] == 168)
		{
			score += 15;
		}
		else if (bytes.Length >= 1 && bytes[0] == 10)
		{
			score += 8;
		}
		else if (bytes.Length >= 2 && bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
		{
			score += 8;
		}

		return score;
	}
}
