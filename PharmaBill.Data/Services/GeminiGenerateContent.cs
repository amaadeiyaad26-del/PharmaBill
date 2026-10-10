using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.Extensions.Logging;

namespace PharmaBill.Data.Services;

/// <summary>
/// Shared Gemini generateContent caller: stable model IDs, transient retries, and user-safe status text.
/// </summary>
public static class GeminiGenerateContent
{
	/// <summary>Primary Flash model for generateContent.</summary>
	public const string DefaultModel = "gemini-3.8-flash";

	/// <summary>Tried in order when a model is missing (404) or remains unavailable after retries.</summary>
	public static readonly string[] ModelCandidates =
	[
		"gemini-3.8-flash",
		"gemini-3.7-flash",
		"gemini-2.5-flash-lite",
		"gemini-2.0-flash",
	];

	public const string OpenRouterModel = "google/gemini-3.8-flash";

	public static readonly TimeSpan PerAttemptTimeout = TimeSpan.FromSeconds(10);

	public static readonly TimeSpan OverallTimeout = TimeSpan.FromSeconds(45);

	private static readonly int[] RetryDelaysMs = [2000, 5000];

	public const string BusyUserMessage =
		"Gemini limit reached, try again in a minute";

	public const string InvalidKeyUserMessage =
		"API key invalid or not allowed, check the key in Settings";

	public const string NetworkUserMessage =
		"No internet or blocked connection";

	public const string ModelUnavailableUserMessage =
		"Gemini model is unavailable; update the app and try again";

	public const string InvalidResponseUserMessage =
		"Gemini returned invalid data";

	public const string PrescriptionBusyFallbackNote =
		"Gemini limit reached, try again in a minute. Offline reader was used. ";

	public static string Endpoint(string model) =>
		"https://generativelanguage.googleapis.com/v1beta/models/" + model + ":generateContent";

	public static bool IsTransient(HttpStatusCode status) =>
		status is HttpStatusCode.ServiceUnavailable
			or HttpStatusCode.TooManyRequests
			or HttpStatusCode.InternalServerError;

	public static bool IsUserFacingHttpNoise(string? message)
	{
		if (string.IsNullOrWhiteSpace(message))
		{
			return false;
		}

		return message.Contains("HTTP 503", StringComparison.OrdinalIgnoreCase)
			|| message.Contains("HTTP 429", StringComparison.OrdinalIgnoreCase)
			|| message.Contains("HTTP 500", StringComparison.OrdinalIgnoreCase)
			|| message.Contains("Gemini returned HTTP", StringComparison.OrdinalIgnoreCase)
			|| message.Contains("Gemini prescription reading failed (HTTP", StringComparison.OrdinalIgnoreCase)
			|| message.Contains("Gemini could not read this invoice (HTTP", StringComparison.OrdinalIgnoreCase)
			|| message.Contains("OpenRouter returned HTTP", StringComparison.OrdinalIgnoreCase);
	}

	public static string UserMessageFor(HttpStatusCode statusCode) =>
		statusCode switch
		{
			HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable => BusyUserMessage,
			HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => InvalidKeyUserMessage,
			HttpStatusCode.NotFound => ModelUnavailableUserMessage,
			_ => NetworkUserMessage
		};

	public static string UserMessageFor(Exception exception) =>
		exception is HttpRequestException httpException && httpException.StatusCode.HasValue
			? UserMessageFor(httpException.StatusCode.Value)
			: exception switch
			{
				InvalidDataException or System.Text.Json.JsonException => InvalidResponseUserMessage,
				TaskCanceledException or OperationCanceledException => NetworkUserMessage,
				HttpRequestException => NetworkUserMessage,
				_ => NetworkUserMessage
			};

	public static string SanitizeUserMessage(string? technical, string fallback = BusyUserMessage) =>
		IsUserFacingHttpNoise(technical) || string.IsNullOrWhiteSpace(technical) ? fallback : technical.Trim();

	/// <summary>
	/// POSTs to Gemini generateContent, cycling models on 404 and retrying transient 503/429/500.
	/// Caller owns the returned response and must dispose it.
	/// </summary>
	public static async Task<HttpResponseMessage> SendAsync(
		HttpClient httpClient,
		string apiKey,
		object body,
		CancellationToken cancellationToken,
		ILogger? logger = null,
		TimeSpan? perAttemptTimeout = null)
	{
		ArgumentNullException.ThrowIfNull(httpClient);
		ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
		ArgumentNullException.ThrowIfNull(body);

		TimeSpan attemptLimit = perAttemptTimeout ?? PerAttemptTimeout;
		HttpResponseMessage? last = null;
		foreach (string model in ModelCandidates)
		{
			for (int attempt = 0; attempt < RetryDelaysMs.Length + 1; attempt++)
			{
				last?.Dispose();
				last = null;

				using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, Endpoint(model))
				{
					Content = JsonContent.Create(body)
				};
				request.Headers.TryAddWithoutValidation("x-goog-api-key", apiKey);

				using CancellationTokenSource attemptTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
				attemptTimeout.CancelAfter(attemptLimit);

				try
				{
					last = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, attemptTimeout.Token);
				}
				catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
				{
					logger?.LogWarning(
						"Gemini request exception: Type={ExceptionType}; Message={Message}; Model={Model}; Endpoint={Endpoint}; Attempt={Attempt}; TimeoutSeconds={Timeout}",
						nameof(OperationCanceledException),
						"Request timed out.",
						model,
						Endpoint(model),
						attempt + 1,
						attemptLimit.TotalSeconds);
					if (attempt < RetryDelaysMs.Length)
					{
						await Task.Delay(RetryDelaysMs[attempt], cancellationToken);
						continue;
					}

					throw;
				}
				catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException)
				{
					logger?.LogWarning(
						ex,
						"Gemini request exception: Type={ExceptionType}; Message={Message}; Model={Model}; Endpoint={Endpoint}; Attempt={Attempt}",
						ex.GetType().Name,
						ex.Message,
						model,
						Endpoint(model),
						attempt + 1);
					throw;
				}

				if (last.IsSuccessStatusCode)
				{
					if (attempt > 0 || !string.Equals(model, DefaultModel, StringComparison.Ordinal))
					{
						logger?.LogInformation("Gemini generateContent succeeded with {Model} on attempt {Attempt}", model, attempt + 1);
					}

					return last;
				}

				int status = (int)last.StatusCode;
				string responseBody = await last.Content.ReadAsStringAsync(CancellationToken.None);
				logger?.LogWarning(
					"Gemini HTTP response: Status={Status}; Model={Model}; Endpoint={Endpoint}; BodyPreview={BodyPreview}",
					status,
					model,
					Endpoint(model),
					responseBody.Length > 300 ? responseBody[..300] : responseBody);
				last.Content = new StringContent(responseBody, Encoding.UTF8, "application/json");
				if (status == 404)
				{
					logger?.LogWarning("Gemini model {Model} not found (HTTP 404); trying next candidate", model);
					break;
				}

				if (IsTransient(last.StatusCode) && attempt < RetryDelaysMs.Length)
				{
					logger?.LogWarning(
						"Gemini {Model} returned HTTP {Status} (attempt {Attempt}); retrying in {Delay}ms",
						model,
						status,
						attempt + 1,
						RetryDelaysMs[attempt]);
					await Task.Delay(RetryDelaysMs[attempt], cancellationToken);
					continue;
				}

				logger?.LogWarning("Gemini {Model} returned HTTP {Status} after {Attempts} attempt(s)", model, status, attempt + 1);
				return last;
			}
		}

		return last ?? throw new HttpRequestException("Gemini generateContent failed with no response.", null, HttpStatusCode.ServiceUnavailable);
	}
}
