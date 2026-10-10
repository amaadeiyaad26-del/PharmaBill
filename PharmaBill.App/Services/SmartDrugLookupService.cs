using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PharmaBill.Core.Ai;
using PharmaBill.Data.Persistence;
using PharmaBill.Data.Services;

namespace PharmaBill.App.Services;

public sealed class SmartDrugLookupService : ISmartDrugLookupService
{
	public static readonly TimeSpan RequestTimeout = GeminiGenerateContent.OverallTimeout;

	private readonly HttpClient _http;

	private readonly Func<string?> _readApiKey;

	private readonly IServiceScopeFactory? _scopes;

	private readonly ILogger<SmartDrugLookupService>? _logger;

	public SmartDrugLookupService(HttpClient http, GeminiApiKeyStore apiKeys, IServiceScopeFactory scopes, ILogger<SmartDrugLookupService>? logger = null)
		: this(http, apiKeys.Read, scopes, logger)
	{
	}

	public SmartDrugLookupService(HttpClient http, Func<string?> readApiKey, ILogger<SmartDrugLookupService>? logger = null)
		: this(http, readApiKey, scopes: null, logger)
	{
	}

	public SmartDrugLookupService(HttpClient http, Func<string?> readApiKey, IServiceScopeFactory? scopes, ILogger<SmartDrugLookupService>? logger = null)
	{
		_http = http;
		_readApiKey = readApiKey;
		_scopes = scopes;
		_logger = logger;
	}

	public async Task<SmartDrugSuggestion?> TryLookupAsync(string query, CancellationToken cancellationToken = default)
	{
		SmartDrugLookupOutcome outcome = await LookupAsync(query, cancellationToken);
		return outcome.Suggestion;
	}

	public async Task<SmartDrugLookupOutcome> LookupAsync(string query, CancellationToken cancellationToken = default)
	{
		string typed = query?.Trim() ?? string.Empty;
		if (typed.Length < 2)
		{
			return new SmartDrugLookupOutcome(null, "Type at least 2 letters to search online.");
		}

		using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeout.CancelAfter(RequestTimeout);
		try
		{
			string? saved = ReadConfiguredKey();
			string? openRouter = IsOpenRouterKey(saved) ? saved : NullIfEmpty(Environment.GetEnvironmentVariable("OPENROUTER_API_KEY"));
			string? gemini = IsOpenRouterKey(saved) ? null : saved;
			SmartDrugSuggestion? suggestion = null;
			string? aiFailure = null;
			bool aiBusy = false;
			if (!string.IsNullOrWhiteSpace(gemini))
			{
				(suggestion, aiFailure, aiBusy) = await AskGeminiAsync(gemini, typed, timeout.Token);
			}
			else if (!string.IsNullOrWhiteSpace(openRouter))
			{
				(suggestion, aiFailure, aiBusy) = await AskOpenRouterAsync(openRouter, typed, timeout.Token);
			}
			else
			{
				aiFailure = "No Gemini or OpenRouter API key is configured.";
			}

			if (suggestion != null)
			{
				string source = !string.IsNullOrWhiteSpace(gemini) ? "Gemini" : "OpenRouter";
				return new SmartDrugLookupOutcome(suggestion, "Filled from " + source + ". Check the MRP and purchase rate before saving.");
			}

			SmartDrugSuggestion? local = await TryLocalFallbackAsync(typed, timeout.Token);
			if (local != null)
			{
				return new SmartDrugLookupOutcome(local, "Filled from your local medicine list. Check the MRP and purchase rate before saving.");
			}

			if (timeout.IsCancellationRequested)
			{
				_logger?.LogWarning("Medicine lookup timed out for {Query}", typed);
				return new SmartDrugLookupOutcome(null, GeminiGenerateContent.NetworkUserMessage);
			}

			(SmartDrugSuggestion? publicMatch, string? publicFailure) = await AskOpenFdaAsync(typed, timeout.Token);
			if (publicMatch != null)
			{
				return new SmartDrugLookupOutcome(publicMatch, "Filled from the public OpenFDA index. HSN 3004 and GST 12% were used. Check the MRP and purchase rate before saving.");
			}

			if (timeout.IsCancellationRequested)
			{
				return new SmartDrugLookupOutcome(null, GeminiGenerateContent.NetworkUserMessage);
			}

			if (aiBusy || GeminiGenerateContent.IsUserFacingHttpNoise(aiFailure) || GeminiGenerateContent.IsUserFacingHttpNoise(publicFailure))
			{
				_logger?.LogWarning("Medicine lookup busy for {Query}: {Detail}", typed, CombineFailure(aiFailure, publicFailure));
				return new SmartDrugLookupOutcome(null, GeminiGenerateContent.BusyUserMessage);
			}

			string detail = CombineFailure(SanitizeFailure(aiFailure), SanitizeFailure(publicFailure))
				?? "No online match found for '" + typed + "'.";
			_logger?.LogWarning("Medicine lookup found nothing for {Query}: {Detail}", typed, detail);
			return new SmartDrugLookupOutcome(null, detail);
		}
		catch (OperationCanceledException)
		{
			_logger?.LogWarning("Medicine lookup timed out for {Query}", typed);
			SmartDrugSuggestion? local = await TryLocalFallbackAsync(typed, CancellationToken.None);
			return local != null
				? new SmartDrugLookupOutcome(local, "Filled from your local medicine list. Check the MRP and purchase rate before saving.")
				: new SmartDrugLookupOutcome(null, GeminiGenerateContent.NetworkUserMessage);
		}
		catch (Exception ex)
		{
			_logger?.LogWarning(ex, "Medicine lookup failed for {Query}", typed);
			SmartDrugSuggestion? local = await TryLocalFallbackAsync(typed, CancellationToken.None);
			if (local != null)
			{
				return new SmartDrugLookupOutcome(local, "Filled from your local medicine list. Check the MRP and purchase rate before saving.");
			}

			return new SmartDrugLookupOutcome(null, SanitizeFailure(DescribeTransport(ex, typed)) ?? GeminiGenerateContent.NetworkUserMessage);
		}
	}

	private async Task<SmartDrugSuggestion?> TryLocalFallbackAsync(string typed, CancellationToken cancellationToken)
	{
		if (_scopes == null)
		{
			return null;
		}

		try
		{
			using IServiceScope scope = _scopes.CreateScope();
			PharmaBillDbContext context = scope.ServiceProvider.GetRequiredService<PharmaBillDbContext>();
			return await LocalDrugSuggestionLookup.FindAsync(context, typed, cancellationToken);
		}
		catch (Exception ex)
		{
			_logger?.LogWarning(ex, "Local medicine fallback failed for {Query}", typed);
			return null;
		}
	}

	private async Task<(SmartDrugSuggestion? Suggestion, string? Failure, bool Busy)> AskGeminiAsync(string apiKey, string typed, CancellationToken cancellationToken)
	{
		try
		{
			object body = BuildBody(typed);
			using HttpResponseMessage response = await GeminiGenerateContent.SendAsync(_http, apiKey, body, cancellationToken, _logger);
			string payload = await response.Content.ReadAsStringAsync(cancellationToken);
			if (!response.IsSuccessStatusCode)
			{
				_logger?.LogWarning("Gemini medicine lookup returned HTTP {Status} for {Query}", (int)response.StatusCode, typed);
				bool busy = GeminiGenerateContent.IsTransient(response.StatusCode);
				return (null, GeminiGenerateContent.UserMessageFor(response.StatusCode), busy);
			}

			SmartDrugSuggestion? suggestion = ParseProviderPayload(payload, typed);
			return suggestion == null
				? (null, "Gemini replied, but the medicine details could not be read.", false)
				: (suggestion, null, false);
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception ex)
		{
			_logger?.LogWarning(ex, "Gemini medicine lookup failed for {Query}", typed);
			return (null, GeminiGenerateContent.UserMessageFor(ex), false);
		}
	}

	private async Task<(SmartDrugSuggestion? Suggestion, string? Failure, bool Busy)> AskOpenRouterAsync(string apiKey, string typed, CancellationToken cancellationToken)
	{
		try
		{
			for (int attempt = 0; attempt <= 3; attempt++)
			{
				if (attempt > 0)
				{
					await Task.Delay(attempt == 1 ? 500 : attempt == 2 ? 1200 : 2500, cancellationToken);
				}

				using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, "https://openrouter.ai/api/v1/chat/completions")
				{
					Content = JsonContent.Create(new
					{
						model = GeminiGenerateContent.OpenRouterModel,
						messages = new[]
						{
							new { role = "user", content = Prompt(typed) }
						}
					})
				};
				request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
				request.Headers.TryAddWithoutValidation("HTTP-Referer", "https://pharmabill.local");
				request.Headers.TryAddWithoutValidation("X-Title", "PharmaBill");

				using CancellationTokenSource attemptTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
				attemptTimeout.CancelAfter(GeminiGenerateContent.PerAttemptTimeout);
				using HttpResponseMessage response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, attemptTimeout.Token);
				string payload = await response.Content.ReadAsStringAsync(cancellationToken);
				if (response.IsSuccessStatusCode)
				{
					SmartDrugSuggestion? suggestion = ParseOpenRouterPayload(payload, typed);
					return suggestion == null
						? (null, "OpenRouter replied, but the medicine details could not be read.", false)
						: (suggestion, null, false);
				}

				_logger?.LogWarning("OpenRouter medicine lookup returned HTTP {Status} for {Query} (attempt {Attempt})", (int)response.StatusCode, typed, attempt + 1);
				if (!GeminiGenerateContent.IsTransient(response.StatusCode) || attempt == 3)
				{
					bool busy = GeminiGenerateContent.IsTransient(response.StatusCode);
					return (null, GeminiGenerateContent.UserMessageFor(response.StatusCode), busy);
				}
			}

			return (null, GeminiGenerateContent.NetworkUserMessage, false);
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception ex)
		{
			_logger?.LogWarning(ex, "OpenRouter medicine lookup failed for {Query}", typed);
			return (null, GeminiGenerateContent.UserMessageFor(ex), false);
		}
	}

	private async Task<(SmartDrugSuggestion? Suggestion, string? Failure)> AskOpenFdaAsync(string typed, CancellationToken cancellationToken)
	{
		string safe = SanitizePublicQuery(typed);
		if (safe.Length < 2)
		{
			return (null, "No online match found for '" + typed + "'.");
		}

		(SmartDrugSuggestion? brandMatch, string? brandFailure, bool brandMissing) = await ReadOpenFdaAsync("brand_name:\"" + safe + "\"", typed, cancellationToken);
		if (brandMatch != null || !brandMissing)
		{
			return (brandMatch, brandFailure);
		}

		(SmartDrugSuggestion? genericMatch, string? genericFailure, _) = await ReadOpenFdaAsync("generic_name:\"" + safe + "\"", typed, cancellationToken);
		return genericMatch == null
			? (null, genericFailure ?? "No online match found for '" + typed + "'.")
			: (genericMatch, null);
	}

	private async Task<(SmartDrugSuggestion? Suggestion, string? Failure, bool NotFound)> ReadOpenFdaAsync(string search, string typed, CancellationToken cancellationToken)
	{
		try
		{
			string url = "https://api.fda.gov/drug/ndc.json?search=" + Uri.EscapeDataString(search) + "&limit=1";
			using HttpResponseMessage response = await _http.GetAsync(url, cancellationToken);
			string payload = await response.Content.ReadAsStringAsync(cancellationToken);
			if (response.StatusCode == HttpStatusCode.NotFound)
			{
				return (null, "No online match found for '" + typed + "'.", true);
			}

			if (!response.IsSuccessStatusCode)
			{
				_logger?.LogWarning("OpenFDA medicine lookup returned HTTP {Status} for {Query}", (int)response.StatusCode, typed);
				return (null, GeminiGenerateContent.IsTransient(response.StatusCode)
					? GeminiGenerateContent.BusyUserMessage
					: "Online lookup could not complete.", false);
			}

			SmartDrugSuggestion? suggestion = ParseOpenFdaPayload(payload, typed);
			return suggestion == null
				? (null, "No online match found for '" + typed + "'.", true)
				: (suggestion, null, false);
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception ex)
		{
			_logger?.LogWarning(ex, "OpenFDA medicine lookup failed for {Query}", typed);
			return (null, DescribeTransport(ex, typed), false);
		}
	}

	private string? ReadConfiguredKey()
	{
		try
		{
			return NullIfEmpty(_readApiKey());
		}
		catch (Exception ex)
		{
			_logger?.LogWarning(ex, "Could not read the medicine lookup API key.");
			return null;
		}
	}

	internal static SmartDrugSuggestion? ParseProviderPayload(string? payload, string fallbackBrand)
	{
		if (string.IsNullOrWhiteSpace(payload))
		{
			return null;
		}

		try
		{
			using JsonDocument document = JsonDocument.Parse(payload);
			if (!document.RootElement.TryGetProperty("candidates", out JsonElement candidates) || candidates.GetArrayLength() == 0)
			{
				return null;
			}

			JsonElement parts = candidates[0].GetProperty("content").GetProperty("parts");
			if (parts.GetArrayLength() == 0 || !parts[0].TryGetProperty("text", out JsonElement textElement))
			{
				return null;
			}

			return ParseSuggestionJson(textElement.GetString(), fallbackBrand);
		}
		catch
		{
			return null;
		}
	}

	public static SmartDrugSuggestion? ParseSuggestionJson(string? modelText, string fallbackBrand)
	{
		string json = UnwrapJson(modelText);
		if (json.Length == 0)
		{
			return null;
		}

		try
		{
			using JsonDocument document = JsonDocument.Parse(json);
			JsonElement root = document.RootElement;
			if (root.ValueKind == JsonValueKind.Array)
			{
				if (root.GetArrayLength() == 0)
				{
					return null;
				}

				root = root[0];
			}

			if (root.ValueKind != JsonValueKind.Object)
			{
				return null;
			}

			string brand = FirstText(root, "brandName", "brand", "name") ?? fallbackBrand.Trim();
			if (brand.Length == 0)
			{
				return null;
			}

			return new SmartDrugSuggestion(
				brand,
				FirstText(root, "genericName", "generic", "composition", "salt"),
				FirstText(root, "manufacturer", "company"),
				FirstText(root, "packSizeLabel", "packSize", "package", "unit"),
				ReadGst(root),
				FirstText(root, "hsnCode", "hsn") ?? "3004",
				ReadMrp(root));
		}
		catch
		{
			return null;
		}
	}

	internal static SmartDrugSuggestion? ParseOpenRouterPayload(string? payload, string fallbackBrand)
	{
		if (string.IsNullOrWhiteSpace(payload))
		{
			return null;
		}

		try
		{
			using JsonDocument document = JsonDocument.Parse(payload);
			JsonElement choices = document.RootElement.GetProperty("choices");
			if (choices.GetArrayLength() == 0)
			{
				return null;
			}

			string? text = choices[0].GetProperty("message").GetProperty("content").GetString();
			return ParseSuggestionJson(text, fallbackBrand);
		}
		catch
		{
			return null;
		}
	}

	internal static SmartDrugSuggestion? ParseOpenFdaPayload(string? payload, string fallbackBrand)
	{
		if (string.IsNullOrWhiteSpace(payload))
		{
			return null;
		}

		try
		{
			using JsonDocument document = JsonDocument.Parse(payload);
			if (!document.RootElement.TryGetProperty("results", out JsonElement results) || results.GetArrayLength() == 0)
			{
				return null;
			}

			JsonElement row = results[0];
			string brand = FirstText(row, "brand_name") ?? fallbackBrand.Trim();
			string? generic = FirstText(row, "generic_name");
			if (string.IsNullOrWhiteSpace(generic) && row.TryGetProperty("active_ingredients", out JsonElement ingredients) && ingredients.ValueKind == JsonValueKind.Array && ingredients.GetArrayLength() > 0)
			{
				generic = FirstText(ingredients[0], "name");
				string? strength = FirstText(ingredients[0], "strength");
				if (!string.IsNullOrWhiteSpace(generic) && !string.IsNullOrWhiteSpace(strength))
				{
					generic = generic + " " + strength;
				}
			}

			string? pack = null;
			if (row.TryGetProperty("packaging", out JsonElement packaging) && packaging.ValueKind == JsonValueKind.Array && packaging.GetArrayLength() > 0)
			{
				pack = FirstText(packaging[0], "description");
			}

			pack ??= FirstText(row, "dosage_form");
			if (pack != null && pack.Length > 80)
			{
				pack = pack[..80].Trim();
			}

			if (brand.Length == 0 && string.IsNullOrWhiteSpace(generic))
			{
				return null;
			}

			return new SmartDrugSuggestion(brand, generic, FirstText(row, "labeler_name"), pack, 12m, "3004", null);
		}
		catch
		{
			return null;
		}
	}

	private static string Prompt(string typed)
	{
		return "Look up this Indian pharmacy product name: \"" + typed + "\". "
			+ "Return only JSON with keys brandName, genericName, manufacturer, packSizeLabel, gstRate, hsnCode, approximateMrp. "
			+ "brandName should match the typed product. genericName is the salt composition. packSizeLabel is the retail pack such as Strip of 10, Bottle, Box, or Vial. "
			+ "Use gstRate 12 and hsnCode \"3004\" when the tax slab is not known. Use JSON null for genericName, manufacturer, packSizeLabel, or approximateMrp when unsure. Do not invent a different brand.";
	}

	private static object BuildBody(string typed)
	{
		string prompt = Prompt(typed);
		return new
		{
			contents = new[]
			{
				new
				{
					parts = new[]
					{
						new { text = prompt }
					}
				}
			},
			generationConfig = new
			{
				responseMimeType = "application/json",
				temperature = 0.1
			}
		};
	}

	private static string UnwrapJson(string? modelText)
	{
		string text = modelText?.Trim() ?? string.Empty;
		if (text.StartsWith("```", StringComparison.Ordinal))
		{
			int firstLine = text.IndexOf('\n');
			int lastFence = text.LastIndexOf("```", StringComparison.Ordinal);
			if (firstLine >= 0 && lastFence > firstLine)
			{
				text = text.Substring(firstLine + 1, lastFence - firstLine - 1).Trim();
			}
		}

		return text;
	}

	private static string? FirstText(JsonElement root, params string[] names)
	{
		foreach (string name in names)
		{
			if (!root.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.String)
			{
				continue;
			}

			string text = value.GetString()?.Trim() ?? string.Empty;
			if (text.Length > 0 && !text.Equals("null", StringComparison.OrdinalIgnoreCase) && !text.Equals("n/a", StringComparison.OrdinalIgnoreCase))
			{
				return text;
			}
		}

		return null;
	}

	private static decimal ReadGst(JsonElement root)
	{
		if (!TryDecimal(root, out decimal gst, "gstRate", "gst"))
		{
			return 12m;
		}

		return gst >= 0m && gst <= 100m ? gst : 12m;
	}

	private static decimal? ReadMrp(JsonElement root)
	{
		if (!TryDecimal(root, out decimal mrp, "approximateMrp", "mrp"))
		{
			return null;
		}

		return mrp > 0m ? mrp : null;
	}

	private static bool TryDecimal(JsonElement root, out decimal number, params string[] names)
	{
		number = 0m;
		foreach (string name in names)
		{
			if (!root.TryGetProperty(name, out JsonElement value))
			{
				continue;
			}

			if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out number))
			{
				return true;
			}

			if (value.ValueKind == JsonValueKind.String && decimal.TryParse(value.GetString(), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out number))
			{
				return true;
			}
		}

		return false;
	}

	private static bool IsOpenRouterKey(string? key)
	{
		return !string.IsNullOrWhiteSpace(key) && key.Trim().StartsWith("sk-or-", StringComparison.OrdinalIgnoreCase);
	}

	private static string? NullIfEmpty(string? value)
	{
		string text = value?.Trim() ?? string.Empty;
		return text.Length == 0 ? null : text;
	}

	private static string SanitizePublicQuery(string typed)
	{
		char[] chars = typed.Where(ch => char.IsLetterOrDigit(ch) || ch is ' ' or '-' or '+').ToArray();
		return new string(chars).Trim();
	}

	private static string? CombineFailure(string? first, string? second)
	{
		if (string.IsNullOrWhiteSpace(first))
		{
			return second;
		}

		if (string.IsNullOrWhiteSpace(second) || string.Equals(first, second, StringComparison.Ordinal))
		{
			return first;
		}

		return first.Trim() + " " + second.Trim();
	}

	private static string? SanitizeFailure(string? message)
	{
		if (string.IsNullOrWhiteSpace(message))
		{
			return null;
		}

		return GeminiGenerateContent.IsUserFacingHttpNoise(message)
			? GeminiGenerateContent.BusyUserMessage
			: message.Trim();
	}

	private static string DescribeTransport(Exception ex, string typed)
	{
		if (ex is OperationCanceledException or TaskCanceledException)
		{
			return GeminiGenerateContent.NetworkUserMessage;
		}

		if (ex is HttpRequestException)
		{
			return GeminiGenerateContent.NetworkUserMessage;
		}

		return "No online match found for '" + typed + "'.";
	}
}
