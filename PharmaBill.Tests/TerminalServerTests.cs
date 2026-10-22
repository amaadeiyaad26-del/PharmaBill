using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PharmaBill.Core;
using PharmaBill.Core.Entities;
using PharmaBill.Core.Security;
using PharmaBill.Data.Persistence;
using PharmaBill.Data.Services;
using PharmaBill.Sync.Terminals;
using Xunit;

namespace PharmaBill.Tests;

// A real HTTPS terminal server on loopback, over a real encrypted shop database, driven the way a terminal will.
public sealed class TerminalServerTests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"PharmaBill.Terminal.{Guid.NewGuid():N}");
    private ServiceProvider _services = null!;
    private TerminalServer _server = null!;
    private TerminalDeviceStore _devices = null!;
    private TerminalPairing Pairing { get; set; } = null!;
    private Guid _paracetamolDrug, _paracetamolBatch, _alprazolamDrug, _alprazolamBatch;

    public async Task InitializeAsync()
    {
        var storage = new DatabaseStorageOptions(Path.Combine(_root, "data-root"));
        Directory.CreateDirectory(storage.DataDirectory);
        _services = BuildServices(storage);
        await using (AsyncServiceScope scope = _services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<PharmaBillDbContext>();
            await new DbInitializer(context, new StorageLocationService(context), scope.ServiceProvider.GetRequiredService<BranchService>(), storage).InitializeAsync();
            await SeedAsync(scope.ServiceProvider);
        }
        _devices = new TerminalDeviceStore(Path.Combine(_root, "terminals"));
        var certificate = new TerminalServerCertificate(Path.Combine(_root, "terminals"));
        Pairing = new TerminalPairing(_devices, certificate);
        _server = new TerminalServer(_services.GetRequiredService<IServiceScopeFactory>(), _devices, new TerminalSessions(Path.Combine(_root, "terminals")),
            certificate, Pairing, NullLogger<TerminalServer>.Instance);
        await _server.StartAsync(0, [IPAddress.Loopback]);
    }

    public async Task DisposeAsync()
    {
        await _server.StopAsync();
        await _services.DisposeAsync();
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task OnlyPairedTerminalsWithTheirOwnKeyGetIn()
    {
        var (counter, secret) = _devices.Register("Counter 1");
        using HttpClient good = Client(counter.Id, secret);
        Assert.Equal(HttpStatusCode.OK, (await good.GetAsync("hello")).StatusCode);

        using HttpClient wrongKey = Client(counter.Id, "not-the-key");
        Assert.Equal(HttpStatusCode.Unauthorized, (await wrongKey.GetAsync("hello")).StatusCode);
        using HttpClient unknown = Client(Guid.NewGuid(), secret);
        Assert.Equal(HttpStatusCode.Unauthorized, (await unknown.GetAsync("hello")).StatusCode);

        // A browser on the LAN is refused before any key check.
        var browser = new HttpRequestMessage(HttpMethod.Get, "hello");
        browser.Headers.TryAddWithoutValidation("Origin", "https://evil.example");
        Assert.Equal(HttpStatusCode.Forbidden, (await good.SendAsync(browser)).StatusCode);

        // Revoking the key shuts the terminal out on its next call.
        Assert.True(_server.Revoke(counter.Id));
        Assert.Equal(HttpStatusCode.Unauthorized, (await good.GetAsync("hello")).StatusCode);
    }

    [Fact]
    public async Task Connection_IsEncrypted_AndPinnedToThisServer()
    {
        var (counter, secret) = _devices.Register("Counter 1");
        TerminalHello hello = (await Client(counter.Id, secret).GetFromJsonAsync<TerminalHello>("hello", Json))!;
        Assert.Equal("Terminal Test Pharmacy", hello.ShopName);
        Assert.True(hello.BillingAllowed);

        // A terminal pinned to a different server certificate refuses to talk at all.
        using HttpClient impostor = Client(counter.Id, secret, fingerprint: new string('A', 64));
        await Assert.ThrowsAsync<HttpRequestException>(() => impostor.GetAsync("hello"));

        // Plain HTTP to the same port gets no answer from the billing API.
        using var plain = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{_server.Port}{TerminalProtocol.BasePath}/") };
        HttpResponseMessage? plainResponse = null;
        try
        {
            plainResponse = await plain.GetAsync("hello");
        }
        catch (HttpRequestException)
        {
            // The TLS listener drops a plain-HTTP connection: the expected outcome.
        }
        Assert.True(plainResponse == null || plainResponse.StatusCode != HttpStatusCode.OK, "the billing API answered over plain HTTP");
    }

    [Fact]
    public async Task SignIn_UsesTheServersUsers_AndSessionsStayOnTheirTerminal()
    {
        var (counter, secret) = _devices.Register("Counter 1");
        var (other, otherSecret) = _devices.Register("Counter 2");
        using HttpClient client = Client(counter.Id, secret);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("session", new TerminalSignInRequest("clerk", "9999"), Json)).StatusCode);
        string token = await SignInAsync(client, "clerk", "2222");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("shop")).StatusCode);

        // The same session token from another terminal is refused.
        using HttpClient stolen = Client(other.Id, otherSecret);
        stolen.DefaultRequestHeaders.Add(TerminalProtocol.SessionHeader, token);
        Assert.Equal(HttpStatusCode.Unauthorized, (await stolen.GetAsync("shop")).StatusCode);

        // Five wrong PINs lock sign-in on that terminal.
        using HttpClient guesser = Client(other.Id, otherSecret);
        for (int i = 0; i < 5; i++)
        {
            await guesser.PostAsJsonAsync("session", new TerminalSignInRequest("owner", $"000{i}"), Json);
        }
        Assert.Equal(HttpStatusCode.TooManyRequests, (await guesser.PostAsJsonAsync("session", new TerminalSignInRequest("owner", "1111"), Json)).StatusCode);
    }

    [Fact]
    public async Task CounterReads_Work()
    {
        using HttpClient client = await SignedInClientAsync("clerk", "2222");
        TerminalShop shop = (await client.GetFromJsonAsync<TerminalShop>("shop", Json))!;
        Assert.Equal("Terminal Test Pharmacy", shop.Name);
        Assert.EndsWith("000001", shop.NextInvoiceNo);
        RetailStockChoice[] stock = (await client.GetFromJsonAsync<RetailStockChoice[]>("stock?q=Paracetamol", Json))!;
        Assert.Contains(stock, choice => choice.BatchNo == "PCM-01" && choice.AvailableQuantity == 6m);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("catalog?q=Para")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("patients?phone=98765")).StatusCode);
    }

    [Fact]
    public async Task Save_RetryWithSameRequestId_BillsOnce()
    {
        using HttpClient client = await SignedInClientAsync("clerk", "2222");
        Guid requestId = Guid.NewGuid();
        TerminalSaleResponse first = await SaveAsync(client, Paracetamol(2m), requestId);
        TerminalSaleResponse retry = await SaveAsync(client, Paracetamol(2m), requestId);

        Assert.Equal(first.InvoiceNo, retry.InvoiceNo);
        Assert.Equal(first.SaleId, retry.SaleId);
        Assert.Equal(1, await CountAsync(db => db.Sales.CountAsync()));
        Assert.Equal(4m, await OnHandAsync(_paracetamolBatch));

        // Reusing a request ID for a different bill is refused, not silently merged.
        HttpResponseMessage reused = await PostSaleAsync(client, Paracetamol(1m) with { Items = [Line(_paracetamolDrug, _paracetamolBatch, 1m, 30m), Line(_paracetamolDrug, _paracetamolBatch, 1m, 30m)] }, requestId);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, reused.StatusCode);

        HttpRequestMessage noId = new(HttpMethod.Post, "sales") { Content = JsonContent.Create(Paracetamol(1m), options: Json) };
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(noId)).StatusCode);
    }

    [Fact]
    public async Task TwoCountersAtOnce_NeverOversell_OrRepeatAnInvoiceNumber()
    {
        using HttpClient counterA = await SignedInClientAsync("clerk", "2222", "Counter A");
        using HttpClient counterB = await SignedInClientAsync("owner", "1111", "Counter B");
        // Ten bills of one strip race for the last six strips.
        HttpResponseMessage[] responses = await Task.WhenAll(Enumerable.Range(0, 10).Select(i =>
            PostSaleAsync(i % 2 == 0 ? counterA : counterB, Paracetamol(1m), Guid.NewGuid())));

        HttpResponseMessage[] saved = responses.Where(r => r.StatusCode == HttpStatusCode.OK).ToArray();
        Assert.Equal(6, saved.Length);
        Assert.All(responses.Where(r => r.StatusCode != HttpStatusCode.OK), r => Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode));
        string[] invoices = await Task.WhenAll(saved.Select(async r => (await r.Content.ReadFromJsonAsync<TerminalSaleResponse>(Json))!.InvoiceNo));
        Assert.Equal(6, invoices.Distinct().Count());
        Assert.Equal(Enumerable.Range(1, 6).Select(n => n.ToString("D6")).ToArray(), invoices.Select(n => n[^6..]).OrderBy(n => n).ToArray());
        Assert.Equal(0m, await OnHandAsync(_paracetamolBatch));
    }

    [Fact]
    public async Task Permissions_ComeFromTheServerSideUser()
    {
        using HttpClient accountant = await SignedInClientAsync("accounts", "3333");
        Assert.Equal(HttpStatusCode.Forbidden, (await PostSaleAsync(accountant, Paracetamol(1m), Guid.NewGuid())).StatusCode);
        using HttpClient clerk = await SignedInClientAsync("clerk", "2222");
        Assert.Equal(HttpStatusCode.Forbidden, (await clerk.PutAsJsonAsync($"batches/{_paracetamolBatch}/mrp", new TerminalBatchMrpRequest(32m), Json)).StatusCode);
        Assert.Equal(0, await CountAsync(db => db.Sales.CountAsync()));
    }

    [Fact]
    public async Task ScheduleH1_NeedsAPrescriptionOrAVerifiedAdminOverride()
    {
        using HttpClient clerk = await SignedInClientAsync("clerk", "2222");
        TerminalSaleRequest controlled = Alprazolam(null);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await PostSaleAsync(clerk, controlled, Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await PostSaleAsync(clerk, Alprazolam(new TerminalAdminOverride("owner", "0000")), Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await PostSaleAsync(clerk, Alprazolam(new TerminalAdminOverride("clerk", "2222")), Guid.NewGuid())).StatusCode);

        TerminalSaleResponse overridden = (await (await PostSaleAsync(clerk, Alprazolam(new TerminalAdminOverride("owner", "1111")), Guid.NewGuid())).Content.ReadFromJsonAsync<TerminalSaleResponse>(Json))!;
        Assert.Equal(1, await CountAsync(db => db.AuditLogs.CountAsync(a => a.Action == "AdminOverrideGranted" && a.Details!.Contains("Counter 1"))));

        // With the prescription uploaded alongside the bill.
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(JsonSerializer.Serialize(Alprazolam(null), Json)), "sale");
        form.Add(new ByteArrayContent([0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3]), "prescription", "rx.jpg");
        var request = new HttpRequestMessage(HttpMethod.Post, "sales") { Content = form };
        request.Headers.Add(TerminalProtocol.RequestIdHeader, Guid.NewGuid().ToString());
        HttpResponseMessage withScan = await clerk.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, withScan.StatusCode);
        TerminalSaleResponse sale = (await withScan.Content.ReadFromJsonAsync<TerminalSaleResponse>(Json))!;
        string? stored = await CountAsync(db => db.Sales.Where(s => s.Id == sale.SaleId).Select(s => s.PrescriptionFilePath).SingleAsync());
        Assert.True(File.Exists(stored), "prescription was not stored on the server: " + stored);
        Assert.Equal(2, await CountAsync(db => db.ScheduleRegisterEntries.CountAsync(e => e.SaleId != null)));
        Assert.NotEqual(overridden.InvoiceNo, sale.InvoiceNo);
    }

    [Fact]
    public async Task Return_AtTheCounter_CreditsOnce_EvenWhenRetried()
    {
        using HttpClient counter = await SignedInClientAsync("pharmacist", "5555");
        TerminalSaleResponse sale = await SaveAsync(counter, Paracetamol(3m), Guid.NewGuid());
        var lines = (await counter.GetFromJsonAsync<RetailSaleReturnableLine[]>($"sales/{sale.SaleId}/returnable", Json))!;
        var request = new TerminalReturnRequest([new TerminalReturnLine(lines.Single().SaleItemId, 1m)], "Customer brought one strip back", Restock: true);
        Guid requestId = Guid.NewGuid();

        TerminalReturnResponse first = await ReturnAsync(counter, sale.SaleId, request, requestId);
        TerminalReturnResponse retry = await ReturnAsync(counter, sale.SaleId, request, requestId);

        Assert.Equal(first.ReturnNo, retry.ReturnNo);
        Assert.Equal(30m, first.CreditAmount);
        Assert.Equal(1, await CountAsync(db => db.ReturnNotes.CountAsync()));
        Assert.Equal(4m, await OnHandAsync(_paracetamolBatch));

        // Cashiers and field sales have no return permission in the shop's roles; the server enforces it.
        using HttpClient cashier = await SignedInClientAsync("clerk", "2222");
        Assert.Equal(HttpStatusCode.Forbidden, (await PostReturnAsync(cashier, sale.SaleId, request, Guid.NewGuid())).StatusCode);
        using HttpClient salesman = await SignedInClientAsync("sales", "4444");
        Assert.Equal(HttpStatusCode.Forbidden, (await PostReturnAsync(salesman, sale.SaleId, request, Guid.NewGuid())).StatusCode);
    }

    [Fact]
    public async Task Void_NeedsAnAdminPin_ReturnsTheStock_AndIsSafeToRetry()
    {
        using HttpClient clerk = await SignedInClientAsync("clerk", "2222");
        TerminalSaleResponse sale = await SaveAsync(clerk, Paracetamol(2m), Guid.NewGuid());
        Assert.Equal(4m, await OnHandAsync(_paracetamolBatch));

        Assert.Equal(HttpStatusCode.Forbidden, (await clerk.PostAsJsonAsync($"sales/{sale.SaleId}/void", new TerminalVoidRequest("Wrong medicine", new TerminalAdminOverride("clerk", "2222")), Json)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await clerk.PostAsJsonAsync($"sales/{sale.SaleId}/void", new TerminalVoidRequest("Wrong medicine", new TerminalAdminOverride("owner", "0000")), Json)).StatusCode);

        HttpResponseMessage voided = await clerk.PostAsJsonAsync($"sales/{sale.SaleId}/void", new TerminalVoidRequest("Wrong medicine", new TerminalAdminOverride("owner", "1111")), Json);
        Assert.Equal(HttpStatusCode.OK, voided.StatusCode);
        Assert.False((await voided.Content.ReadFromJsonAsync<TerminalVoidResponse>(Json))!.AlreadyVoided);
        Assert.Equal(6m, await OnHandAsync(_paracetamolBatch));

        TerminalVoidResponse again = (await (await clerk.PostAsJsonAsync($"sales/{sale.SaleId}/void", new TerminalVoidRequest("Wrong medicine", new TerminalAdminOverride("owner", "1111")), Json)).Content.ReadFromJsonAsync<TerminalVoidResponse>(Json))!;
        Assert.True(again.AlreadyVoided);
        Assert.Equal(6m, await OnHandAsync(_paracetamolBatch));
        Assert.Equal(1, await CountAsync(db => db.AuditLogs.CountAsync(a => a.Action == "AdminOverrideGranted" && a.Details!.StartsWith("void bill"))));
    }

    [Fact]
    public async Task ReturnOnABillPastTheLockPeriod_NeedsAnAdminUnlock()
    {
        using HttpClient counter = await SignedInClientAsync("pharmacist", "5555");
        TerminalSaleResponse sale = await SaveAsync(counter, Paracetamol(2m), Guid.NewGuid());
        await CountAsync(db => db.Database.ExecuteSqlRawAsync("UPDATE Sales SET SaleAtUtc = {0} WHERE Id = {1}", BusinessClock.UtcNow.AddDays(-10), sale.SaleId));
        var line = (await counter.GetFromJsonAsync<RetailSaleReturnableLine[]>($"sales/{sale.SaleId}/returnable", Json))!.Single();
        var request = new TerminalReturnRequest([new TerminalReturnLine(line.SaleItemId, 1m)], "Returned after a week", Restock: true);

        Assert.Equal((HttpStatusCode)423, (await PostReturnAsync(counter, sale.SaleId, request, Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await PostReturnAsync(counter, sale.SaleId, request with { Unlock = new TerminalAdminOverride("owner", "0000") }, Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostReturnAsync(counter, sale.SaleId, request with { Unlock = new TerminalAdminOverride("owner", "1111") }, Guid.NewGuid())).StatusCode);
        Assert.Equal(1, await CountAsync(db => db.ReturnNotes.CountAsync()));
    }

    [Fact]
    public async Task ServerRestart_KeepsCountersSignedIn()
    {
        using HttpClient clerk = await SignedInClientAsync("clerk", "2222");
        Assert.Equal(HttpStatusCode.OK, (await clerk.GetAsync("shop")).StatusCode);

        // The server PC reboots (a Windows update): a new server process over the same saved state.
        await _server.StopAsync();
        string terminals = Path.Combine(_root, "terminals");
        var reloadedDevices = new TerminalDeviceStore(terminals);
        var reloadedCertificate = new TerminalServerCertificate(terminals);
        _server = new TerminalServer(_services.GetRequiredService<IServiceScopeFactory>(), reloadedDevices, new TerminalSessions(terminals),
            reloadedCertificate, new TerminalPairing(reloadedDevices, reloadedCertificate), NullLogger<TerminalServer>.Instance);
        await _server.StartAsync(0, [IPAddress.Loopback]);
        using HttpClient sameTerminal = new(new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, c, _, _) => c != null && TerminalServerCertificate.Thumbprint(c) == _server.Fingerprint })
        {
            BaseAddress = new Uri($"https://127.0.0.1:{_server.Port}{TerminalProtocol.BasePath}/")
        };
        foreach (var header in clerk.DefaultRequestHeaders)
        {
            sameTerminal.DefaultRequestHeaders.TryAddWithoutValidation(header.Key, header.Value);
        }
        Assert.Equal(HttpStatusCode.OK, (await sameTerminal.GetAsync("shop")).StatusCode);
    }

    [Fact]
    public async Task DeactivatedOrDemotedUsers_TakeEffectOnTheirNextCall()
    {
        using HttpClient pharmacist = await SignedInClientAsync("pharmacist", "5555");
        Assert.Equal(HttpStatusCode.OK, (await pharmacist.PutAsJsonAsync($"batches/{_paracetamolBatch}/mrp", new TerminalBatchMrpRequest(31m), Json)).StatusCode);

        await CountAsync(db => db.Database.ExecuteSqlRawAsync("UPDATE AppUsers SET Role = {0} WHERE UserName = 'pharmacist'", (int)UserRole.BillingClerk));
        Assert.Equal(HttpStatusCode.Forbidden, (await pharmacist.PutAsJsonAsync($"batches/{_paracetamolBatch}/mrp", new TerminalBatchMrpRequest(32m), Json)).StatusCode);

        await CountAsync(db => db.Database.ExecuteSqlRawAsync("UPDATE AppUsers SET IsActive = 0 WHERE UserName = 'pharmacist'"));
        Assert.Equal(HttpStatusCode.Unauthorized, (await pharmacist.GetAsync("shop")).StatusCode);
    }

    [Fact]
    public async Task Pairing_WithTheCodeOnTheServerScreen_GivesThisPcItsOwnWorkingKey()
    {
        TerminalPairingCode code = Pairing.Open();
        TerminalClientSettings paired = await TerminalPairingClient.PairAsync("127.0.0.1", _server.Port, code.Code.ToLowerInvariant().Insert(3, " "), "Front counter");

        Assert.Equal(_server.Fingerprint, paired.ServerFingerprint);
        Assert.Null(Pairing.Current);
        using HttpClient client = Client(paired.DeviceId, paired.Secret, paired.ServerFingerprint);
        TerminalHello hello = (await client.GetFromJsonAsync<TerminalHello>("hello", Json))!;
        Assert.Equal("Front counter", hello.DeviceName);
        Assert.Equal("Terminal Test Pharmacy", paired.ShopName);

        // The code was used once; a second PC cannot reuse it.
        await Assert.ThrowsAsync<TerminalPairingException>(() => TerminalPairingClient.PairAsync("127.0.0.1", _server.Port, code.Code, "Second counter"));
        Assert.Single(_devices.List());
    }

    [Fact]
    public async Task ThreeWrongCodes_CancelTheCode()
    {
        TerminalPairingCode code = Pairing.Open();
        string wrong = code.Code[0] == 'A' ? "BBBBBB" : "AAAAAA";
        for (int attempt = 0; attempt < 3; attempt++)
        {
            await Assert.ThrowsAsync<TerminalPairingException>(() => TerminalPairingClient.PairAsync("127.0.0.1", _server.Port, wrong, "Guesser"));
        }
        var closed = await Assert.ThrowsAsync<TerminalPairingException>(() => TerminalPairingClient.PairAsync("127.0.0.1", _server.Port, code.Code, "Guesser"));
        Assert.Contains("No pairing code is open", closed.Message);
        Assert.Empty(_devices.List());
    }

    [Fact]
    public async Task AMachineInTheMiddle_CannotPair_EvenRelayingTheRightCode()
    {
        // A relay on the shop network with its own certificate, forwarding every request to the real server.
        var impostorCertificate = new TerminalServerCertificate(Path.Combine(_root, "impostor"));
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0, listen => listen.UseHttps(impostorCertificate.Get())));
        await using WebApplication relay = builder.Build();
        using var upstream = new HttpClient(new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, _, _, _) => true }) { BaseAddress = new Uri($"https://127.0.0.1:{_server.Port}") };
        relay.Run(async context =>
        {
            var forward = new HttpRequestMessage(new HttpMethod(context.Request.Method), context.Request.Path + context.Request.QueryString);
            if (HttpMethods.IsPost(context.Request.Method))
            {
                using var body = new MemoryStream();
                await context.Request.Body.CopyToAsync(body);
                forward.Content = new ByteArrayContent(body.ToArray());
                forward.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(context.Request.ContentType ?? "application/json");
            }
            HttpResponseMessage answer = await upstream.SendAsync(forward);
            context.Response.StatusCode = (int)answer.StatusCode;
            context.Response.ContentType = answer.Content.Headers.ContentType?.ToString();
            await answer.Content.CopyToAsync(context.Response.Body);
        });
        await relay.StartAsync();
        int relayPort = new Uri(relay.Urls.First()).Port;

        TerminalPairingCode code = Pairing.Open();
        var refused = await Assert.ThrowsAsync<TerminalPairingException>(() => TerminalPairingClient.PairAsync("127.0.0.1", relayPort, code.Code, "Front counter"));

        Assert.Empty(_devices.List());
        Assert.Equal(TerminalPairing.MaxAttempts - 1, Pairing.Current!.AttemptsLeft);
        Assert.Contains("not right", refused.Message);
    }

    // ---- helpers ----

    private static Task<HttpResponseMessage> PostReturnAsync(HttpClient client, Guid saleId, TerminalReturnRequest request, Guid requestId)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, $"sales/{saleId}/return") { Content = JsonContent.Create(request, options: Json) };
        message.Headers.Add(TerminalProtocol.RequestIdHeader, requestId.ToString());
        return client.SendAsync(message);
    }

    private static async Task<TerminalReturnResponse> ReturnAsync(HttpClient client, Guid saleId, TerminalReturnRequest request, Guid requestId)
    {
        HttpResponseMessage response = await PostReturnAsync(client, saleId, request, requestId);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<TerminalReturnResponse>(Json))!;
    }

    private HttpClient Client(Guid deviceId, string secret, string? fingerprint = null)
    {
        string pinned = fingerprint ?? _server.Fingerprint;
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, certificate, _, _) => certificate != null && TerminalServerCertificate.Thumbprint(certificate) == pinned
        };
        var client = new HttpClient(handler) { BaseAddress = new Uri($"https://127.0.0.1:{_server.Port}{TerminalProtocol.BasePath}/") };
        client.DefaultRequestHeaders.Add(TerminalProtocol.DeviceHeader, deviceId.ToString());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(TerminalProtocol.AuthorizationScheme, secret);
        return client;
    }

    private async Task<HttpClient> SignedInClientAsync(string user, string pin, string terminal = "Counter 1")
    {
        var (device, secret) = _devices.Register(terminal);
        HttpClient client = Client(device.Id, secret);
        await SignInAsync(client, user, pin);
        return client;
    }

    private static async Task<string> SignInAsync(HttpClient client, string user, string pin)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync("session", new TerminalSignInRequest(user, pin), Json);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string token = (await response.Content.ReadFromJsonAsync<TerminalSignInResponse>(Json))!.SessionToken;
        client.DefaultRequestHeaders.Remove(TerminalProtocol.SessionHeader);
        client.DefaultRequestHeaders.Add(TerminalProtocol.SessionHeader, token);
        return token;
    }

    private static Task<HttpResponseMessage> PostSaleAsync(HttpClient client, TerminalSaleRequest sale, Guid requestId)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "sales") { Content = JsonContent.Create(sale, options: Json) };
        request.Headers.Add(TerminalProtocol.RequestIdHeader, requestId.ToString());
        return client.SendAsync(request);
    }

    private static async Task<TerminalSaleResponse> SaveAsync(HttpClient client, TerminalSaleRequest sale, Guid requestId)
    {
        HttpResponseMessage response = await PostSaleAsync(client, sale, requestId);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<TerminalSaleResponse>(Json))!;
    }

    private static TerminalSaleLine Line(Guid drug, Guid batch, decimal quantity, decimal price) => new(drug, batch, quantity, 0m, price);

    private TerminalSaleRequest Paracetamol(decimal quantity) => new("Walk-in", "9876543210", null, null, null,
        [Line(_paracetamolDrug, _paracetamolBatch, quantity, 30m)], [new TerminalPayment("Cash", quantity * 30m)], false);

    private TerminalSaleRequest Alprazolam(TerminalAdminOverride? prescriptionOverride) => new("Rahul Kumar", "9876500011", "4 Lal Chowk, Srinagar", "Dr A. Shah", "JKMC-4411",
        [Line(_alprazolamDrug, _alprazolamBatch, 1m, 40m)], [new TerminalPayment("Cash", 40m)], false, PrescriptionOverride: prescriptionOverride);

    private async Task<T> CountAsync<T>(Func<PharmaBillDbContext, Task<T>> query)
    {
        await using AsyncServiceScope scope = _services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<PharmaBillDbContext>());
    }

    private Task<decimal> OnHandAsync(Guid batchId) => CountAsync(db => db.StockMovements.Where(m => m.BatchId == batchId).SumAsync(m => m.QuantityChange));

    private async Task SeedAsync(IServiceProvider services)
    {
        var context = services.GetRequiredService<PharmaBillDbContext>();
        context.PharmacyProfiles.Add(new PharmacyProfile { Name = "Terminal Test Pharmacy", BusinessMode = BusinessMode.Retail, InvoicePrefix = "WIN1", State = "Jammu and Kashmir" });
        context.AppUsers.AddRange(
            new AppUser { DisplayName = "Ravi (owner)", UserName = "owner", Role = UserRole.Owner, PasswordHash = PasswordHasher.Hash("1111") },
            new AppUser { DisplayName = "Asha (cashier)", UserName = "clerk", Role = UserRole.BillingClerk, PasswordHash = PasswordHasher.Hash("2222") },
            new AppUser { DisplayName = "Accounts", UserName = "accounts", Role = UserRole.Accountant, PasswordHash = PasswordHasher.Hash("3333") },
            new AppUser { DisplayName = "Field sales", UserName = "sales", Role = UserRole.Salesman, PasswordHash = PasswordHasher.Hash("4444") },
            new AppUser { DisplayName = "Imran (pharmacist)", UserName = "pharmacist", Role = UserRole.Pharmacist, PasswordHash = PasswordHasher.Hash("5555") });
        await context.SaveChangesAsync();
        var addStock = new AddStockService(services.GetRequiredService<IUnitOfWork>(),
            new PurchaseService(services.GetRequiredService<IUnitOfWork>(), new Allowed(), new StorageLocationService(context), services.GetRequiredService<BranchService>(), new NullLicenseRuntimeGuard()),
            new Allowed(), new NullLicenseRuntimeGuard());
        Guid owner = Guid.NewGuid();
        Supplier supplier = await addStock.AddSupplierAsync("Valley Distributors", owner, UserRole.Owner);
        DateOnly today = BusinessClock.Today;
        AddStockResult pcm = await addStock.AddStockAsync(new AddStockInput(null, null, "Paracetamol 650", "Paracetamol", "Acme", "G", "PCM-01",
            today.AddYears(2), 30m, 18m, 6m, 0m, 10m, 5m, supplier.Id, "VD-1", today, "A1", HsnCode: "30049099"), owner, UserRole.Owner);
        AddStockResult alp = await addStock.AddStockAsync(new AddStockInput(null, null, "Alprazolam 0.5", "Alprazolam", "Acme", "H1", "ALP-07",
            today.AddYears(2), 40m, 25m, 20m, 0m, 10m, 12m, supplier.Id, "VD-2", today, "C3", HsnCode: "30049099"), owner, UserRole.Owner);
        (_paracetamolDrug, _paracetamolBatch, _alprazolamDrug, _alprazolamBatch) = (pcm.DrugId, pcm.BatchId, alp.DrugId, alp.BatchId);
    }

    private ServiceProvider BuildServices(DatabaseStorageOptions storage)
    {
        var services = new ServiceCollection();
        services.AddSingleton(storage);
        services.AddSingleton(new BranchSettingsStore(storage));
        services.AddScoped(_ => OpenContext(storage));
        services.AddScoped<IUnitOfWork>(sp => new UnitOfWork(sp.GetRequiredService<PharmaBillDbContext>()));
        services.AddScoped<NumberSeriesService>();
        services.AddScoped<StorageLocationService>();
        services.AddScoped<BranchService>();
        services.AddScoped<CatalogSearchService>();
        services.AddScoped<IEntitlementService, Allowed>();
        services.AddSingleton<ILicenseRuntimeGuard, NullLicenseRuntimeGuard>();
        services.AddScoped<AuthenticationService>();
        services.AddScoped<ProductTaxService>();
        services.AddScoped<InventoryService>();
        string prescriptions = Path.Combine(storage.RootDirectory, "prescriptions");
        services.AddScoped(sp => new RetailBillingService(sp.GetRequiredService<IUnitOfWork>(), sp.GetRequiredService<NumberSeriesService>(), sp.GetRequiredService<IEntitlementService>(),
            sp.GetRequiredService<CatalogSearchService>(), sp.GetRequiredService<StorageLocationService>(), sp.GetRequiredService<BranchService>(),
            sp.GetRequiredService<ILicenseRuntimeGuard>(), prescriptions));
        return services.BuildServiceProvider();
    }

    private static PharmaBillDbContext OpenContext(DatabaseStorageOptions storage)
    {
        SQLitePCL.Batteries_V2.Init();
        string password = Convert.ToHexString(new DatabaseEncryptionKeyProvider(storage.KeyPath).GetOrCreateKey());
        var deviceId = new DatabaseDeviceId(Guid.NewGuid());
        return new PharmaBillDbContext(new DbContextOptionsBuilder<PharmaBillDbContext>()
            .UseSqlite(new SqliteConnectionStringBuilder { DataSource = storage.DatabasePath, Password = password, Pooling = false, DefaultTimeout = 60 }.ToString())
            .AddInterceptors(new SaveChangesAuditInterceptor(deviceId)).Options, deviceId);
    }

    private sealed class Allowed : IEntitlementService
    {
        public Task<EntitlementStatus> GetStatusAsync(CancellationToken cancellationToken = default) => Task.FromResult(EntitlementStatus.TRIAL);
        public Task<bool> IsReadOnlyAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> CanPerformAsync(ProtectedOperation operation, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }
}
