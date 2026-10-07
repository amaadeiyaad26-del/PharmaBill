using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PharmaBill.Sync;

public sealed class CloudSyncClient(HttpClient http, string apiKey, Guid branchId, Guid deviceId)
{
	private static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web);

	private HttpRequestMessage Request(HttpMethod method, string uri)
	{
		HttpRequestMessage httpRequestMessage = new HttpRequestMessage(method, uri);
		httpRequestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
		httpRequestMessage.Headers.Add("X-Device-Id", deviceId.ToString("D"));
		return httpRequestMessage;
	}

	private static async Task EnsureOkAsync(HttpResponseMessage response, CancellationToken ct)
	{
		if (response.IsSuccessStatusCode)
		{
			return;
		}
		HttpStatusCode statusCode = response.StatusCode;
		if ((statusCode == HttpStatusCode.Unauthorized || statusCode == HttpStatusCode.Forbidden) ? true : false)
		{
			throw new CloudSyncAuthenticationException("The cloud server rejected the branch API key.");
		}
		string value = await response.Content.ReadAsStringAsync(ct);
		throw new HttpRequestException($"Cloud server returned {(int)response.StatusCode}: {value}");
	}

	public async Task<CloudPushAck> PushAsync(IReadOnlyList<CloudChangeWire> changes, CancellationToken ct = default(CancellationToken))
	{
		using HttpRequestMessage request = Request(HttpMethod.Post, "api/sync/push");
		request.Content = JsonContent.Create(new
		{
			deviceId = deviceId,
			changes = changes
		}, null, Json);
		using HttpResponseMessage response = await http.SendAsync(request, ct);
		await EnsureOkAsync(response, ct);
		return (await response.Content.ReadFromJsonAsync<CloudPushAck>(Json, ct)) ?? throw new InvalidDataException("Empty push acknowledgement.");
	}

	public async Task<CloudPullPage> PullAsync(string since, int limit = 200, CancellationToken ct = default(CancellationToken))
	{
		string uri = $"api/sync/pull?branchId={branchId:D}&limit={limit}" + (string.IsNullOrEmpty(since) ? string.Empty : ("&since=" + Uri.EscapeDataString(since)));
		using HttpRequestMessage request = Request(HttpMethod.Get, uri);
		using HttpResponseMessage response = await http.SendAsync(request, ct);
		await EnsureOkAsync(response, ct);
		return (await response.Content.ReadFromJsonAsync<CloudPullPage>(Json, ct)) ?? throw new InvalidDataException("Empty pull response.");
	}

	public async Task<string> UploadFileAsync(string path, bool shared, CancellationToken ct = default(CancellationToken))
	{
		string result;
		await using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true))
		{
			string hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, ct));
			stream.Position = 0L;
			using HttpRequestMessage request = Request(HttpMethod.Post, "api/sync/files?shared=" + shared.ToString().ToLowerInvariant());
			request.Headers.Add("X-Content-Sha256", hash);
			request.Content = new StreamContent(stream);
			request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
			using HttpResponseMessage response = await http.SendAsync(request, ct);
			await EnsureOkAsync(response, ct);
			result = hash;
		}
		return result;
	}

	public async Task<byte[]?> DownloadFileAsync(string sha256, CancellationToken ct = default(CancellationToken))
	{
		using HttpRequestMessage request = Request(HttpMethod.Get, "api/sync/files/" + sha256.ToLowerInvariant());
		using HttpResponseMessage response = await http.SendAsync(request, ct);
		if (response.StatusCode == HttpStatusCode.NotFound)
		{
			return null;
		}
		await EnsureOkAsync(response, ct);
		byte[] array = await response.Content.ReadAsByteArrayAsync(ct);
		if (!string.Equals(Convert.ToHexStringLower(SHA256.HashData(array)), sha256, StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidDataException("Downloaded file failed SHA-256 verification.");
		}
		return array;
	}
}
