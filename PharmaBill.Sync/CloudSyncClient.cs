using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;

namespace PharmaBill.Sync;

public sealed record CloudChangeWire(
    Guid ChangeId,
    string Entity,
    Guid EntityId,
    string Operation,
    int SchemaVersion,
    string HlcStamp,
    Guid OriginDeviceId,
    DateTime ChangedAtUtc,
    JsonElement Payload,
    IReadOnlyList<Guid>? SharedWithBranchIds = null);

public sealed record CloudPushAck(int Accepted, int Duplicates, string ServerHlc, IReadOnlyList<Guid> AcknowledgedChangeIds);

public sealed record CloudPullPage(IReadOnlyList<CloudChangeWire> Changes, string NextSince, bool HasMore, string ServerHlc);

public sealed class CloudSyncAuthenticationException(string message) : Exception(message);

public interface ICloudHttpClientProvider
{
    HttpClient Create(CloudSyncSettings settings);
}

public sealed class DefaultCloudHttpClientProvider : ICloudHttpClientProvider
{
    private readonly object _gate = new();
    private HttpClient? _client;
    private string _signature = string.Empty;

    public HttpClient Create(CloudSyncSettings settings)
    {
        lock (_gate)
        {
            var signature = settings.ServerUrl + "|" + settings.ApiKey;
            if (_client is null || signature != _signature)
            {
                _client?.Dispose();
                _client = new HttpClient { BaseAddress = new Uri(settings.ServerUrl), Timeout = TimeSpan.FromSeconds(60) };
                _signature = signature;
            }

            return _client;
        }
    }
}

public sealed class CloudSyncClient(HttpClient http, string apiKey, Guid branchId, Guid deviceId)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private HttpRequestMessage Request(HttpMethod method, string uri)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Headers.Add("X-Device-Id", deviceId.ToString("D"));
        return request;
    }

    private static async Task EnsureOkAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new CloudSyncAuthenticationException("The cloud server rejected the branch API key.");
        var body = await response.Content.ReadAsStringAsync(ct);
        throw new HttpRequestException($"Cloud server returned {(int)response.StatusCode}: {body}");
    }

    public async Task<CloudPushAck> PushAsync(IReadOnlyList<CloudChangeWire> changes, CancellationToken ct = default)
    {
        using var request = Request(HttpMethod.Post, "api/sync/push");
        request.Content = JsonContent.Create(new { deviceId, changes }, options: Json);
        using var response = await http.SendAsync(request, ct);
        await EnsureOkAsync(response, ct);
        return await response.Content.ReadFromJsonAsync<CloudPushAck>(Json, ct)
               ?? throw new InvalidDataException("Empty push acknowledgement.");
    }

    public async Task<CloudPullPage> PullAsync(string since, int limit = 200, CancellationToken ct = default)
    {
        var uri = $"api/sync/pull?branchId={branchId:D}&limit={limit}" +
                  (string.IsNullOrEmpty(since) ? string.Empty : $"&since={Uri.EscapeDataString(since)}");
        using var request = Request(HttpMethod.Get, uri);
        using var response = await http.SendAsync(request, ct);
        await EnsureOkAsync(response, ct);
        return await response.Content.ReadFromJsonAsync<CloudPullPage>(Json, ct)
               ?? throw new InvalidDataException("Empty pull response.");
    }

    public async Task<string> UploadFileAsync(string path, bool shared, CancellationToken ct = default)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        var hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, ct));
        stream.Position = 0;
        using var request = Request(HttpMethod.Post, $"api/sync/files?shared={shared.ToString().ToLowerInvariant()}");
        request.Headers.Add("X-Content-Sha256", hash);
        request.Content = new StreamContent(stream);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        using var response = await http.SendAsync(request, ct);
        await EnsureOkAsync(response, ct);
        return hash;
    }

    // Verifies the SHA-256 of the downloaded content before it is returned.
    public async Task<byte[]?> DownloadFileAsync(string sha256, CancellationToken ct = default)
    {
        using var request = Request(HttpMethod.Get, $"api/sync/files/{sha256.ToLowerInvariant()}");
        using var response = await http.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        await EnsureOkAsync(response, ct);
        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        if (!string.Equals(Convert.ToHexStringLower(SHA256.HashData(bytes)), sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Downloaded file failed SHA-256 verification.");
        return bytes;
    }
}
