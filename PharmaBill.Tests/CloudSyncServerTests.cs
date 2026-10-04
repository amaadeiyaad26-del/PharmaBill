using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PharmaBill.CloudSync;
using PharmaBill.Core.Entities;
using PharmaBill.Data.Persistence;
using PharmaBill.Sync;
using Xunit;

namespace PharmaBill.Tests;

public sealed class CloudSyncServerTests : IAsyncDisposable
{
    private const string AdminKey = "test-admin-key";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"PharmaBill.Cloud.{Guid.NewGuid():N}");
    private readonly WebApplicationFactory<Program> _factory;

    public CloudSyncServerTests()
    {
        Directory.CreateDirectory(_root);
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Cloud", $"Data Source={Path.Combine(_root, "cloud.db")};Pooling=False");
            builder.UseSetting("Cloud:Provider", "Sqlite");
            builder.UseSetting("Cloud:BlobRoot", Path.Combine(_root, "blobs"));
            builder.UseSetting("Cloud:AdminKey", AdminKey);
        });
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
        SqliteCleanup();
    }

    private void SqliteCleanup()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_root, true);
        }
        catch (IOException)
        {
        }
    }

    private async Task<Guid> CreateTenantAsync(string name)
    {
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/admin/tenants")
        {
            Content = JsonContent.Create(new { name })
        };
        request.Headers.Add("X-Admin-Key", AdminKey);
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("tenantId").GetGuid();
    }

    private async Task<CreatedBranch> CreateBranchAsync(Guid tenantId, string name)
    {
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/admin/branches")
        {
            Content = JsonContent.Create(new { tenantId, name })
        };
        request.Headers.Add("X-Admin-Key", AdminKey);
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CreatedBranch>(Json))!;
    }

    private HttpClient ClientFor(string? apiKey, Guid? deviceId = null)
    {
        var client = _factory.CreateClient();
        if (apiKey is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        if (deviceId is not null)
        {
            client.DefaultRequestHeaders.Add("X-Device-Id", deviceId.Value.ToString("D"));
        }

        return client;
    }

    private static CloudChangeDto Change(
        string entity,
        long ms,
        long counter,
        Guid deviceId,
        IReadOnlyList<Guid>? sharedWith = null,
        Guid? changeId = null) =>
        new(
            changeId ?? Guid.NewGuid(),
            entity,
            Guid.NewGuid(),
            "Upsert",
            1,
            $"{ms}:{counter}:{deviceId:D}",
            deviceId,
            DateTime.UtcNow,
            JsonSerializer.SerializeToElement(new { id = Guid.NewGuid(), name = entity }),
            sharedWith);

    private static async Task<PushAck> PushAsync(HttpClient client, Guid deviceId, params CloudChangeDto[] changes)
    {
        var response = await client.PostAsJsonAsync("/api/sync/push", new PushRequest(deviceId, changes), Json);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<PushAck>(Json))!;
    }

    private static async Task<PullResponse> PullAsync(
        HttpClient client, Guid branchId, string? since = null, int? limit = null)
    {
        var uri = $"/api/sync/pull?branchId={branchId:D}" +
                  (since is null ? string.Empty : $"&since={Uri.EscapeDataString(since)}") +
                  (limit is null ? string.Empty : $"&limit={limit}");
        var response = await client.GetAsync(uri);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<PullResponse>(Json))!;
    }

    [Fact]
    public async Task MissingMalformedAndWrongTokensAreRejected()
    {
        var tenant = await CreateTenantAsync("T");
        var branch = await CreateBranchAsync(tenant, "B1");
        var wrongSecret = branch.ApiKey[..^3] + "xyz";
        var unknownBranch = $"pbk_{Guid.NewGuid():N}_secret";

        foreach (var key in new string?[] { null, "garbage", wrongSecret, unknownBranch })
        {
            using var client = ClientFor(key);
            Assert.Equal(HttpStatusCode.Unauthorized,
                (await client.GetAsync($"/api/sync/pull?branchId={branch.BranchId:D}")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized,
                (await client.PostAsJsonAsync("/api/sync/push", new PushRequest(Guid.NewGuid(), []), Json)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/api/sync/files/{new string('a', 64)}")).StatusCode);
        }

        using var valid = ClientFor(branch.ApiKey);
        Assert.Equal(HttpStatusCode.OK, (await valid.GetAsync($"/api/sync/pull?branchId={branch.BranchId:D}")).StatusCode);
    }

    [Fact]
    public async Task AdminEndpointsRejectWrongKey()
    {
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/admin/tenants")
        {
            Content = JsonContent.Create(new { name = "X" })
        };
        request.Headers.Add("X-Admin-Key", "wrong");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(request)).StatusCode);
    }

    [Fact]
    public async Task BranchTokenCannotPullAnotherBranch()
    {
        var tenant = await CreateTenantAsync("T");
        var first = await CreateBranchAsync(tenant, "B1");
        var second = await CreateBranchAsync(tenant, "B2");
        using var client = ClientFor(first.ApiKey);

        var response = await client.GetAsync($"/api/sync/pull?branchId={second.BranchId:D}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SalesStayInBranchWhileMasterDataReachesEveryBranch()
    {
        var tenant = await CreateTenantAsync("T");
        var first = await CreateBranchAsync(tenant, "B1");
        var second = await CreateBranchAsync(tenant, "B2");
        var otherTenant = await CreateTenantAsync("Other");
        var outsider = await CreateBranchAsync(otherTenant, "X1");
        var deviceOne = Guid.NewGuid();
        var deviceOneB = Guid.NewGuid();
        var deviceTwo = Guid.NewGuid();
        var deviceOutsider = Guid.NewGuid();
        using var branchOne = ClientFor(first.ApiKey, deviceOne);
        using var branchOneOtherDevice = ClientFor(first.ApiKey, deviceOneB);
        using var branchTwo = ClientFor(second.ApiKey, deviceTwo);
        using var other = ClientFor(outsider.ApiKey, deviceOutsider);

        await PushAsync(branchOne, deviceOne,
            Change("Sale", 1_000, 0, deviceOne),
            Change("StockMovement", 1_000, 1, deviceOne),
            Change("Drug", 1_000, 2, deviceOne));

        var seenByTwo = (await PullAsync(branchTwo, second.BranchId)).Changes.Select(c => c.Entity).ToList();
        var seenByOneOtherDevice = (await PullAsync(branchOneOtherDevice, first.BranchId)).Changes
            .Select(c => c.Entity).Order().ToList();
        var seenByOwnDevice = (await PullAsync(branchOne, first.BranchId)).Changes;
        var seenByOutsider = (await PullAsync(other, outsider.BranchId)).Changes;

        Assert.Equal(["Drug"], seenByTwo);
        Assert.Equal(["Drug", "Sale", "StockMovement"], seenByOneOtherDevice);
        Assert.Empty(seenByOwnDevice);
        Assert.Empty(seenByOutsider);
    }

    [Fact]
    public async Task ExplicitlySharedTransferReachesOnlyTheNamedBranch()
    {
        var tenant = await CreateTenantAsync("T");
        var first = await CreateBranchAsync(tenant, "B1");
        var second = await CreateBranchAsync(tenant, "B2");
        var third = await CreateBranchAsync(tenant, "B3");
        var device = Guid.NewGuid();
        using var branchOne = ClientFor(first.ApiKey, device);
        using var branchTwo = ClientFor(second.ApiKey);
        using var branchThree = ClientFor(third.ApiKey);

        await PushAsync(branchOne, device, Change("StockMovement", 5_000, 0, device, [second.BranchId]));

        Assert.Single((await PullAsync(branchTwo, second.BranchId)).Changes);
        Assert.Empty((await PullAsync(branchThree, third.BranchId)).Changes);
    }

    [Fact]
    public async Task SharingWithABranchOfAnotherTenantIsRejected()
    {
        var tenant = await CreateTenantAsync("T");
        var first = await CreateBranchAsync(tenant, "B1");
        var foreign = await CreateBranchAsync(await CreateTenantAsync("Other"), "X1");
        var device = Guid.NewGuid();
        using var client = ClientFor(first.ApiKey);

        var response = await client.PostAsJsonAsync("/api/sync/push",
            new PushRequest(device, [Change("StockMovement", 1, 0, device, [foreign.BranchId])]), Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PushIsIdempotentByChangeId()
    {
        var tenant = await CreateTenantAsync("T");
        var branch = await CreateBranchAsync(tenant, "B1");
        var other = await CreateBranchAsync(tenant, "B2");
        var device = Guid.NewGuid();
        using var client = ClientFor(branch.ApiKey);
        var change = Change("Drug", 10, 0, device);

        var first = await PushAsync(client, device, change);
        var second = await PushAsync(client, device, change, change);

        Assert.Equal(1, first.Accepted);
        Assert.Equal(0, second.Accepted);
        Assert.Equal(1, second.Duplicates);
        Assert.Contains(change.ChangeId, second.AcknowledgedChangeIds);
        using var puller = ClientFor(other.ApiKey);
        Assert.Single((await PullAsync(puller, other.BranchId)).Changes);
    }

    [Fact]
    public async Task PullReturnsServerOrderPagesWithoutGapsOrDuplicates()
    {
        var tenant = await CreateTenantAsync("T");
        var writer = await CreateBranchAsync(tenant, "B1");
        var reader = await CreateBranchAsync(tenant, "B2");
        var device = Guid.NewGuid();
        using var writerClient = ClientFor(writer.ApiKey);
        using var readerClient = ClientFor(reader.ApiKey);

        // Pushed out of HLC order and across two batches; the server order is its arrival order.
        var early = Enumerable.Range(0, 3).Select(i => Change("Drug", 9_000 - i, 0, device)).ToArray();
        var late = Enumerable.Range(0, 4).Select(i => Change("Drug", 100 + i, 0, device)).ToArray();
        var firstAck = await PushAsync(writerClient, device, early);
        var secondAck = await PushAsync(writerClient, device, late);
        Assert.True(Hlc.Compare(secondAck.ServerHlc, firstAck.ServerHlc) > 0);

        var collected = new List<CloudChangeDto>();
        string? since = null;
        var pages = 0;
        PullResponse page;
        do
        {
            page = await PullAsync(readerClient, reader.BranchId, since, 2);
            collected.AddRange(page.Changes);
            since = page.NextSince;
            pages++;
        }
        while (page.HasMore);

        Assert.Equal(4, pages);
        Assert.Equal(7, collected.Count);
        Assert.Equal(7, collected.Select(c => c.ChangeId).Distinct().Count());
        var expectedOrder = early.OrderBy(c => c.HlcStamp, Comparer<string>.Create(Hlc.Compare))
            .Concat(late.OrderBy(c => c.HlcStamp, Comparer<string>.Create(Hlc.Compare)))
            .Select(c => c.ChangeId);
        Assert.Equal(expectedOrder, collected.Select(c => c.ChangeId));

        Assert.Empty((await PullAsync(readerClient, reader.BranchId, since)).Changes);
    }

    [Fact]
    public async Task PushRejectsInvalidChanges()
    {
        var tenant = await CreateTenantAsync("T");
        var branch = await CreateBranchAsync(tenant, "B1");
        var device = Guid.NewGuid();
        using var client = ClientFor(branch.ApiKey);
        var bad = Change("Drug", 1, 0, device) with { HlcStamp = "not-a-stamp" };

        var response = await client.PostAsJsonAsync("/api/sync/push", new PushRequest(device, [bad]), Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DeviceCannotSwitchBranch()
    {
        var tenant = await CreateTenantAsync("T");
        var first = await CreateBranchAsync(tenant, "B1");
        var second = await CreateBranchAsync(tenant, "B2");
        var device = Guid.NewGuid();
        using var branchOne = ClientFor(first.ApiKey);
        using var branchTwo = ClientFor(second.ApiKey);
        await PushAsync(branchOne, device, Change("Drug", 1, 0, device));

        var response = await branchTwo.PostAsJsonAsync("/api/sync/push",
            new PushRequest(device, [Change("Drug", 2, 0, device)]), Json);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task FilesAreHashVerifiedAndScopedToTheirBranchUnlessShared()
    {
        var tenant = await CreateTenantAsync("T");
        var first = await CreateBranchAsync(tenant, "B1");
        var second = await CreateBranchAsync(tenant, "B2");
        using var branchOne = ClientFor(first.ApiKey);
        using var branchTwo = ClientFor(second.ApiKey);
        var bytes = RandomNumberGenerator.GetBytes(4096);
        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));

        using var wrong = new HttpRequestMessage(HttpMethod.Post, "/api/sync/files") { Content = new ByteArrayContent(bytes) };
        wrong.Headers.Add("X-Content-Sha256", new string('0', 64));
        Assert.Equal(HttpStatusCode.BadRequest, (await branchOne.SendAsync(wrong)).StatusCode);

        async Task<HttpResponseMessage> Upload(string query)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/sync/files" + query)
            {
                Content = new ByteArrayContent(bytes)
            };
            request.Headers.Add("X-Content-Sha256", hash);
            return await branchOne.SendAsync(request);
        }

        var uploaded = await Upload(string.Empty);
        Assert.Equal(HttpStatusCode.OK, uploaded.StatusCode);
        Assert.False((await uploaded.Content.ReadFromJsonAsync<FileUploadResult>(Json))!.AlreadyExisted);
        Assert.Equal(bytes, await branchOne.GetByteArrayAsync($"/api/sync/files/{hash}"));
        Assert.Equal(HttpStatusCode.NotFound, (await branchTwo.GetAsync($"/api/sync/files/{hash}")).StatusCode);

        var shared = await Upload("?shared=true");
        Assert.True((await shared.Content.ReadFromJsonAsync<FileUploadResult>(Json))!.AlreadyExisted);
        Assert.Equal(bytes, await branchTwo.GetByteArrayAsync($"/api/sync/files/{hash}"));
    }

    [Fact]
    public async Task WindowsClientsExchangeMasterDataThroughTheCloudAndSkipTheirOwnChanges()
    {
        var tenant = await CreateTenantAsync("T");
        var first = await CreateBranchAsync(tenant, "B1");
        var second = await CreateBranchAsync(tenant, "B2");
        await using var one = await CloudWindowsClient.CreateAsync(_factory, first.ApiKey, _root, "one");
        await using var two = await CloudWindowsClient.CreateAsync(_factory, second.ApiKey, _root, "two");

        var drug = new Drug { Name = "Cloud paracetamol", Mrp = 12.34m };
        one.Database.Context.Drugs.Add(drug);
        await one.Database.Context.SaveChangesAsync();

        var pushed = await one.Service.SyncNowAsync();
        var received = await two.Service.SyncNowAsync();
        var repeated = await one.Service.SyncNowAsync();
        var again = await two.Service.SyncNowAsync();

        Assert.True(pushed.Pushed >= 1);
        Assert.Equal(0, repeated.Pulled);
        Assert.True(received.Applied >= 1);
        Assert.Equal(0, again.Pulled);
        var copy = await two.Database.Context.Drugs.FindAsync(drug.Id);
        Assert.NotNull(copy);
        Assert.Equal("Cloud paracetamol", copy.Name);
        Assert.Equal(12.34m, copy.Mrp);
        Assert.Equal("Idle", (await two.Service.GetSettingsAsync()).Status);
    }

    [Fact]
    public async Task WindowsClientReportsErrorStatusForARejectedKey()
    {
        var tenant = await CreateTenantAsync("T");
        var branch = await CreateBranchAsync(tenant, "B1");
        await using var client = await CloudWindowsClient.CreateAsync(
            _factory, branch.ApiKey[..^3] + "xyz", _root, "bad");

        await Assert.ThrowsAsync<CloudSyncAuthenticationException>(() => client.Service.SyncNowAsync());

        var settings = await client.Service.GetSettingsAsync();
        Assert.Equal("Error", settings.Status);
        Assert.False(string.IsNullOrEmpty(settings.LastError));
    }

    [Fact]
    public async Task ClientSettingsRejectInsecureRemoteUrlsAndInvalidKeys()
    {
        await using var database = await DatabaseTestContext.CreateAsync();
        var storage = new DatabaseStorageOptions(Path.Combine(_root, "settings"));
        var service = new CloudSyncService(
            new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            new CloudSyncSettingsStore(storage),
            new FixedHttpProvider(new HttpClient()),
            new DatabaseDeviceId(database.Context.DeviceId),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<CloudSyncService>.Instance);
        var key = $"pbk_{Guid.NewGuid():N}_secret";

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ConfigureAsync("http://example.com", key, true));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ConfigureAsync("https://example.com", "nope", true));
        await service.ConfigureAsync("https://example.com", key, true);
        Assert.True((await service.GetSettingsAsync()).Enabled);
    }

    private static class Hlc
    {
        public static int Compare(string a, string b) => PharmaBill.Core.Sync.HybridLogicalClockState.Compare(a, b);
    }

    private sealed class FixedHttpProvider(HttpClient client) : ICloudHttpClientProvider
    {
        public HttpClient Create(CloudSyncSettings settings) => client;
    }

    private sealed class CloudWindowsClient : IAsyncDisposable
    {
        private readonly ServiceProvider _provider;
        private readonly HttpClient _http;

        private CloudWindowsClient(DatabaseTestContext database, ServiceProvider provider, HttpClient http, CloudSyncService service)
        {
            Database = database;
            _provider = provider;
            _http = http;
            Service = service;
        }

        public DatabaseTestContext Database { get; }
        public CloudSyncService Service { get; }

        public static async Task<CloudWindowsClient> CreateAsync(
            WebApplicationFactory<Program> factory, string apiKey, string root, string name)
        {
            var database = await DatabaseTestContext.CreateAsync();
            var storage = new DatabaseStorageOptions(Path.Combine(root, name));
            var http = factory.CreateClient();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton(database.Context);
            services.AddSingleton(new DatabaseDeviceId(database.Context.DeviceId));
            services.AddSingleton(storage);
            services.AddSingleton<SyncPayloadSerializer>();
            services.AddSingleton<PharmaBill.Core.Sync.HybridLogicalClockState>();
            services.AddSingleton<HybridLogicalClock>();
            services.AddScoped<ChangeApplier>();
            services.AddSingleton<CloudSyncSettingsStore>();
            services.AddSingleton<ICloudHttpClientProvider>(new FixedHttpProvider(http));
            services.AddSingleton<CloudSyncService>();
            var provider = services.BuildServiceProvider();
            var service = provider.GetRequiredService<CloudSyncService>();
            await service.ConfigureAsync("http://localhost/", apiKey, true);
            return new CloudWindowsClient(database, provider, http, service);
        }

        public async ValueTask DisposeAsync()
        {
            await _provider.DisposeAsync();
            _http.Dispose();
            await Database.DisposeAsync();
        }
    }
}

