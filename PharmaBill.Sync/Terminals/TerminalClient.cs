using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using PharmaBill.Core.Gst;
using PharmaBill.Data.Services;

namespace PharmaBill.Sync.Terminals;

/// <summary>The server PC could not be reached. Nothing was decided there - or, for a save, it is not known yet.</summary>
public sealed class TerminalServerUnreachableException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Something answered at the server's address with a different certificate: not the paired server.</summary>
public sealed class TerminalServerIdentityException(string message) : Exception(message);

/// <summary>The server no longer accepts this PC's key: removed from the server's Devices panel.</summary>
public sealed class TerminalUnpairedException(string message) : Exception(message);

/// <summary>The user's session ended (timed out, signed out on the server, or the user was deactivated).</summary>
public sealed class TerminalSignedOutException(string message) : Exception(message);

/// <summary>The bill is past the record-lock period: retry with an admin's unlock.</summary>
public sealed class TerminalLockedException(string message) : Exception(message);

/// <summary>The server refused the request and said why (stock, MRP, permission, prescription...).</summary>
public sealed class TerminalRefusedException(HttpStatusCode status, string message) : Exception(message)
{
	public HttpStatusCode Status { get; } = status;
}

// A paired terminal's connection to its server: every call pinned to the server's certificate, carrying this PC's
// key and the signed-in user's session. Nothing is stored or decided on this PC.
public sealed class TerminalClient : IDisposable
{
	private static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web);

	private static readonly TimeSpan[] SaveRetryDelays = [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3)];

	private readonly HttpClient _http;

	private readonly TerminalClientSettings _settings;

	private volatile bool _identityMismatch;

	private string? _sessionToken;

	public TerminalClient(TerminalClientSettings settings, TimeSpan? timeout = null)
	{
		_settings = settings;
		var handler = new HttpClientHandler
		{
			ServerCertificateCustomValidationCallback = (_, certificate, _, _) =>
			{
				bool matches = certificate != null && TerminalServerCertificate.Thumbprint(certificate) == settings.ServerFingerprint;
				_identityMismatch = !matches;
				return matches;
			}
		};
		_http = new HttpClient(handler)
		{
			BaseAddress = new Uri($"https://{settings.ServerHost}:{settings.ServerPort}{TerminalProtocol.BasePath}/"),
			Timeout = timeout ?? TimeSpan.FromSeconds(15)
		};
		_http.DefaultRequestHeaders.Add(TerminalProtocol.DeviceHeader, settings.DeviceId.ToString());
		_http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(TerminalProtocol.AuthorizationScheme, settings.Secret);
	}

	public TerminalClientSettings Settings => _settings;

	public bool IsSignedIn => _sessionToken != null;

	public string ServerLabel => $"{_settings.ServerName} ({_settings.ServerHost})";

	public Task<TerminalHello> HelloAsync(CancellationToken token = default) => SendAsync<TerminalHello>(HttpMethod.Get, "hello", null, null, token);

	public async Task<TerminalSignInResponse> SignInAsync(string userName, string pin, CancellationToken token = default)
	{
		TerminalSignInResponse response = await SendAsync<TerminalSignInResponse>(HttpMethod.Post, "session", JsonContent.Create(new TerminalSignInRequest(userName, pin), options: Json), null, token);
		_sessionToken = response.SessionToken;
		return response;
	}

	public async Task SignOutAsync(CancellationToken token = default)
	{
		try
		{
			if (_sessionToken != null)
			{
				await SendAsync<object?>(HttpMethod.Delete, "session", null, null, token);
			}
		}
		catch (Exception exception) when (exception is TerminalServerUnreachableException or TerminalSignedOutException)
		{
			// Signing out locally is enough: the server session ends by itself.
		}
		finally
		{
			_sessionToken = null;
		}
	}

	public Task<TerminalShop> ShopAsync(CancellationToken token = default) => SendAsync<TerminalShop>(HttpMethod.Get, "shop", null, null, token);

	public Task<string> NextInvoiceNoAsync(CancellationToken token = default) => SendAsync<string>(HttpMethod.Get, "invoice/next", null, null, token);

	public Task<RetailStockChoice[]> SearchStockAsync(string query, CancellationToken token = default) =>
		SendAsync<RetailStockChoice[]>(HttpMethod.Get, "stock?q=" + Uri.EscapeDataString(query ?? string.Empty), null, null, token);

	public Task<RetailStockChoice?> ByBarcodeAsync(string barcode, CancellationToken token = default) =>
		SendAsync<RetailStockChoice?>(HttpMethod.Get, "stock/barcode/" + Uri.EscapeDataString(barcode), null, null, token);

	public Task<RetailPatientMatch[]> SearchPatientsAsync(string phone, CancellationToken token = default) =>
		SendAsync<RetailPatientMatch[]>(HttpMethod.Get, "patients?phone=" + Uri.EscapeDataString(phone ?? string.Empty), null, null, token);

	public Task<RetailPatientBill[]> PatientHistoryAsync(Guid patientId, CancellationToken token = default) =>
		SendAsync<RetailPatientBill[]>(HttpMethod.Get, $"patients/{patientId}/history", null, null, token);

	public Task<RetailBillPrintData> PrintDataAsync(Guid saleId, CancellationToken token = default) =>
		SendAsync<RetailBillPrintData>(HttpMethod.Get, $"sales/{saleId}/print", null, null, token);

	public Task<RecentRetailBill[]> RecentBillsAsync(string? query, CancellationToken token = default) =>
		SendAsync<RecentRetailBill[]>(HttpMethod.Get, "sales?q=" + Uri.EscapeDataString(query ?? string.Empty), null, null, token);

	public Task<RetailSaleReturnableLine[]> ReturnableLinesAsync(Guid saleId, CancellationToken token = default) =>
		SendAsync<RetailSaleReturnableLine[]>(HttpMethod.Get, $"sales/{saleId}/returnable", null, null, token);

	public Task<ProductTaxDetails> SetProductTaxAsync(Guid drugId, TerminalProductTaxRequest request, CancellationToken token = default) =>
		SendAsync<ProductTaxDetails>(HttpMethod.Put, $"products/{drugId}/tax", JsonContent.Create(request, options: Json), null, token);

	public Task<object?> SetBatchMrpAsync(Guid batchId, decimal mrp, CancellationToken token = default) =>
		SendAsync<object?>(HttpMethod.Put, $"batches/{batchId}/mrp", JsonContent.Create(new TerminalBatchMrpRequest(mrp), options: Json), null, token);

	public Task<TerminalVoidResponse> VoidAsync(Guid saleId, TerminalVoidRequest request, CancellationToken token = default) =>
		SendAsync<TerminalVoidResponse>(HttpMethod.Post, $"sales/{saleId}/void", JsonContent.Create(request, options: Json), null, token);

	public Task<TerminalReturnResponse> ReturnAsync(Guid saleId, TerminalReturnRequest request, Guid requestId, CancellationToken token = default) =>
		WithRetriesAsync(() => SendAsync<TerminalReturnResponse>(HttpMethod.Post, $"sales/{saleId}/return", JsonContent.Create(request, options: Json), requestId, token), token);

	/// <summary>Saves a bill on the server. The same <paramref name="requestId"/> must be used for every attempt at this bill:
	/// if a reply is lost, a retry returns the bill the server already made instead of billing twice.</summary>
	public Task<TerminalSaleResponse> SaveSaleAsync(TerminalSaleRequest sale, Guid requestId, string? prescriptionPath, CancellationToken token = default) =>
		WithRetriesAsync(() => SendAsync<TerminalSaleResponse>(HttpMethod.Post, "sales", SaleContent(sale, prescriptionPath), requestId, token), token);

	public void Dispose() => _http.Dispose();

	private static HttpContent SaleContent(TerminalSaleRequest sale, string? prescriptionPath)
	{
		if (string.IsNullOrWhiteSpace(prescriptionPath))
		{
			return JsonContent.Create(sale, options: Json);
		}
		var form = new MultipartFormDataContent();
		form.Add(new StringContent(JsonSerializer.Serialize(sale, Json)), "sale");
		form.Add(new ByteArrayContent(File.ReadAllBytes(prescriptionPath)), "prescription", Path.GetFileName(prescriptionPath));
		return form;
	}

	// A save is retried a few times on a dropped connection, always with the same request ID. Anything else -
	// a refusal, a signed-out session - is final and goes straight back to the cashier.
	private static async Task<T> WithRetriesAsync<T>(Func<Task<T>> attempt, CancellationToken token)
	{
		for (int retry = 0; ; retry++)
		{
			try
			{
				return await attempt();
			}
			catch (TerminalServerUnreachableException) when (retry < SaveRetryDelays.Length)
			{
				await Task.Delay(SaveRetryDelays[retry], token);
			}
		}
	}

	private async Task<T> SendAsync<T>(HttpMethod method, string path, HttpContent? content, Guid? requestId, CancellationToken token)
	{
		using var request = new HttpRequestMessage(method, path) { Content = content };
		if (_sessionToken != null)
		{
			request.Headers.Add(TerminalProtocol.SessionHeader, _sessionToken);
		}
		if (requestId is Guid id)
		{
			request.Headers.Add(TerminalProtocol.RequestIdHeader, id.ToString());
		}
		HttpResponseMessage response;
		try
		{
			response = await _http.SendAsync(request, token);
		}
		catch (HttpRequestException) when (_identityMismatch)
		{
			throw new TerminalServerIdentityException($"Something at {_settings.ServerHost} answered, but it is not the paired server PC ({_settings.ServerName}). Nothing was sent. If the server PC was replaced or reinstalled, pair this PC again; otherwise someone may be interfering with the shop network.");
		}
		catch (Exception exception) when (exception is HttpRequestException || (exception is TaskCanceledException && !token.IsCancellationRequested))
		{
			throw new TerminalServerUnreachableException($"The server PC ({_settings.ServerName}) can't be reached. Billing is stopped on this counter. Check that the server PC is switched on and connected to the shop network.", exception);
		}
		using (response)
		{
			if (response.IsSuccessStatusCode)
			{
				if (response.StatusCode == HttpStatusCode.NoContent || typeof(T) == typeof(object))
				{
					return default!;
				}
				return (await response.Content.ReadFromJsonAsync<T>(Json, token))!;
			}
			TerminalError error = await ReadErrorAsync(response, token);
			throw response.StatusCode switch
			{
				HttpStatusCode.Unauthorized when error.Code == TerminalErrorCodes.Unpaired => new TerminalUnpairedException(error.Error),
				HttpStatusCode.Unauthorized when error.Code == TerminalErrorCodes.SignedOut => SignedOut(error.Error),
				(HttpStatusCode)423 => new TerminalLockedException(error.Error),
				_ => new TerminalRefusedException(response.StatusCode, error.Error)
			};
		}
	}

	private TerminalSignedOutException SignedOut(string message)
	{
		_sessionToken = null;
		return new TerminalSignedOutException(message);
	}

	private static async Task<TerminalError> ReadErrorAsync(HttpResponseMessage response, CancellationToken token)
	{
		try
		{
			return await response.Content.ReadFromJsonAsync<TerminalError>(Json, token) ?? new TerminalError($"The server refused this ({(int)response.StatusCode}).");
		}
		catch (Exception exception) when (exception is JsonException or NotSupportedException)
		{
			return new TerminalError($"The server refused this ({(int)response.StatusCode}).");
		}
	}
}
