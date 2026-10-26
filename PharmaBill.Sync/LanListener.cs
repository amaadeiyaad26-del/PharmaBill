using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;

namespace PharmaBill.Sync;

// Shared rules for the phone listeners (sync station on 5055, purchase-bill scan on 5184).
// They serve the Pharma Remit phone app only, over plain HTTP on the shop's own network, until the encrypted
// per-device transport arrives with PC terminals.
public static class LanListener
{
	// Loopback plus the single shop-network address the QR code advertises - never every adapter, and never
	// a public address. With no private network address only this PC can reach the listener.
	public static IReadOnlyList<IPAddress> Addresses()
	{
		List<IPAddress> addresses = new List<IPAddress> { IPAddress.Loopback };
		IPAddress? lan = LocalNetworkInfo.GetLanIPv4Address();
		if (lan != null)
		{
			addresses.Add(lan);
		}
		return addresses;
	}

	public static void Listen(KestrelServerOptions options, IReadOnlyList<IPAddress> addresses, int port)
	{
		foreach (IPAddress address in addresses)
		{
			options.Listen(address, port);
		}
	}

	public static bool SameAddresses(IReadOnlyList<IPAddress> a, IReadOnlyList<IPAddress> b)
	{
		return a.Select(item => item.ToString()).OrderBy(item => item, StringComparer.Ordinal)
			.SequenceEqual(b.Select(item => item.ToString()).OrderBy(item => item, StringComparer.Ordinal));
	}

	public static string Describe(IReadOnlyList<IPAddress> addresses)
	{
		IPAddress? lan = addresses.FirstOrDefault(address => !IPAddress.IsLoopback(address));
		return lan == null ? "this PC only - no shop network found" : lan.ToString();
	}

	// A web page open in any browser on this PC or the network can send requests here. Browsers always mark
	// them - Origin on cross-site calls and WebSocket upgrades, Sec-Fetch-Site on every request - and the
	// phone app never sends either, so anything carrying them is refused before it reaches an endpoint.
	public static bool IsFromBrowser(HttpRequest request)
	{
		return request.Headers.ContainsKey("Origin") || request.Headers.ContainsKey("Sec-Fetch-Site");
	}

	public static async Task RefuseBrowsersAsync(HttpContext context, Func<Task> next)
	{
		if (IsFromBrowser(context.Request))
		{
			context.Response.StatusCode = StatusCodes.Status403Forbidden;
			context.Response.ContentType = "application/json";
			await context.Response.WriteAsync("{\"error\":\"Browser requests are not accepted. Use the Pharma Remit phone app.\"}");
			return;
		}
		await next();
	}
}
