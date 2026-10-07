using System;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace PharmaBill.Sync;

public static class LocalNetworkInfo
{
	public static string? GetLocalIPv4()
	{
		try
		{
			return (from item in (from nic in NetworkInterface.GetAllNetworkInterfaces().Where((NetworkInterface nic) =>
					{
						bool flag = nic.OperationalStatus == OperationalStatus.Up;
						if (flag)
						{
							NetworkInterfaceType networkInterfaceType = nic.NetworkInterfaceType;
							bool flag2 = ((networkInterfaceType == NetworkInterfaceType.Loopback || networkInterfaceType == NetworkInterfaceType.Tunnel) ? true : false);
							flag = !flag2;
						}
						return flag;
					})
					select new
					{
						Nic = nic,
						Properties = nic.GetIPProperties()
					}).SelectMany(item => from address in item.Properties.UnicastAddresses
					where address.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address.Address) && !IsLinkLocal(address.Address)
					select new
					{
						Address = address.Address,
						Score = Score(item.Nic, item.Properties, address.Address)
					})
				orderby item.Score descending
				select item.Address.ToString()).FirstOrDefault();
		}
		catch (NetworkInformationException)
		{
			return null;
		}
	}

	private static bool IsLinkLocal(IPAddress address)
	{
		byte[] addressBytes = address.GetAddressBytes();
		if (addressBytes[0] == 169)
		{
			return addressBytes[1] == 254;
		}
		return false;
	}

	private static int Score(NetworkInterface nic, IPInterfaceProperties properties, IPAddress address)
	{
		int num = 0;
		if (properties.GatewayAddresses.Any((GatewayIPAddressInformation gateway) => gateway.Address.AddressFamily == AddressFamily.InterNetwork && !gateway.Address.Equals(IPAddress.Any)))
		{
			num += 100;
		}
		int num2 = num;
		num = num2 + nic.NetworkInterfaceType switch
		{
			NetworkInterfaceType.Wireless80211 => 30, 
			NetworkInterfaceType.Ethernet => 20, 
			_ => 0, 
		};
		byte[] addressBytes = address.GetAddressBytes();
		if (addressBytes[0] == 192 && addressBytes[1] == 168)
		{
			num += 10;
		}
		else
		{
			if (addressBytes[0] == 10)
			{
				goto IL_00a0;
			}
			if (addressBytes[0] == 172)
			{
				byte b = addressBytes[1];
				if (b >= 16 && b <= 31)
				{
					goto IL_00a0;
				}
			}
		}
		goto IL_00a4;
		IL_00a4:
		string description = nic.Description;
		if (description.Contains("virtual", StringComparison.OrdinalIgnoreCase) || description.Contains("vethernet", StringComparison.OrdinalIgnoreCase) || description.Contains("vmware", StringComparison.OrdinalIgnoreCase) || description.Contains("virtualbox", StringComparison.OrdinalIgnoreCase))
		{
			num -= 50;
		}
		return num;
		IL_00a0:
		num += 5;
		goto IL_00a4;
	}
}
