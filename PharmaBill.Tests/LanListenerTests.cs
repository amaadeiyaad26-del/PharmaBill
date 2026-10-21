using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PharmaBill.Core.Sync;
using PharmaBill.Data.Persistence;
using PharmaBill.Sync;
using Xunit;

namespace PharmaBill.Tests;

public sealed class LanListenerTests
{
    [Fact]
    public async Task SyncStation_RefusesBrowsers_AndSendsNoCorsHeaders()
    {
        int port = FreePort();
        var deviceId = new DatabaseDeviceId(Guid.NewGuid());
        await using var server = new LocalSyncServer(new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            new HybridLogicalClock(new HybridLogicalClockState(), deviceId), new SyncPayloadSerializer(), deviceId, NullLogger<LocalSyncServer>.Instance);
        await server.StartAsync(port);
        using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };

        // The phone app: plain request, no browser headers.
        HttpResponseMessage app = await http.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, app.StatusCode);
        Assert.False(app.Headers.Contains("Access-Control-Allow-Origin"));

        // A web page calling cross-site, a navigation or <img> request, and a CORS preflight.
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(http, HttpMethod.Get, "/health", ("Origin", "https://evil.example"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(http, HttpMethod.Get, "/api/sync/ping", ("Sec-Fetch-Site", "cross-site"))).StatusCode);
        HttpResponseMessage preflight = await SendAsync(http, HttpMethod.Options, "/api/sync/exchange",
            ("Origin", "https://evil.example"), ("Access-Control-Request-Method", "POST"), ("Access-Control-Request-Headers", "x-sync-token"));
        Assert.Equal(HttpStatusCode.Forbidden, preflight.StatusCode);
        Assert.False(preflight.Headers.Contains("Access-Control-Allow-Origin"));

        // A request with a wrong token still gets 401, not data.
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(http, HttpMethod.Get, "/api/sync/handshake", ("X-Sync-Token", "guess"))).StatusCode);
    }

    [Fact]
    public void Binds_LoopbackAndOnlyAPrivateShopAddress()
    {
        IReadOnlyList<IPAddress> addresses = LanListener.Addresses();
        Assert.Contains(IPAddress.Loopback, addresses);
        Assert.DoesNotContain(IPAddress.Any, addresses);
        Assert.All(addresses.Where(a => !IPAddress.IsLoopback(a)), a => Assert.True(LocalNetworkInfo.IsPrivate(a)));
        Assert.True(addresses.Count <= 2);
    }

    [Theory]
    [InlineData("192.168.1.20", true)]
    [InlineData("10.0.0.5", true)]
    [InlineData("172.16.4.1", true)]
    [InlineData("172.32.0.1", false)]
    [InlineData("49.36.12.8", false)]
    [InlineData("169.254.3.3", false)]
    public void PrivateAddresses_AreTheOnlyLanAddresses(string address, bool isPrivate)
    {
        Assert.Equal(isPrivate, LocalNetworkInfo.IsPrivate(IPAddress.Parse(address)));
    }

    private static Task<HttpResponseMessage> SendAsync(HttpClient http, HttpMethod method, string path, params (string Name, string Value)[] headers)
    {
        var request = new HttpRequestMessage(method, path);
        foreach ((string name, string value) in headers)
        {
            request.Headers.TryAddWithoutValidation(name, value);
        }
        return http.SendAsync(request);
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
