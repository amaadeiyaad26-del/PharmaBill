using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PharmaBill.Core.Ai;
using PharmaBill.Core.Entities;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Data.Services;

public sealed class PrescriptionOcrService(PharmaBillDbContext context, HttpClient httpClient, GeminiApiKeyStore apiKeyStore, IOcrTextRecognizer? textRecognizer = null, ILogger<PrescriptionOcrService>? logger = null) : IPrescriptionOcrService
{
	public const string GeminiModel = "gemini-2.5-flash";

	private const int MaximumImageBytes = 15728640;

	public bool IsCloudConfigured
	{
		get
		{
			try
			{
				return !string.IsNullOrWhiteSpace(apiKeyStore.Read());
			}
			catch (Exception ex) when ((ex is PlatformNotSupportedException || ex is CryptographicException) ? true : false)
			{
				return false;
			}
		}
	}

	public async Task<PrescriptionParseResultDto> ParseAsync(byte[] imageBytes, string mimeType, bool allowCloud, CancellationToken cancellationToken = default(CancellationToken))
	{
		ArgumentNullException.ThrowIfNull(imageBytes, "imageBytes");
		if (imageBytes.Length == 0 || imageBytes.Length > 15728640)
		{
			throw new InvalidOperationException("The prescription image must be between 1 byte and 15 MB.");
		}
		string fallbackNote = string.Empty;
		if (allowCloud)
		{
			if (!IsCloudConfigured)
			{
				fallbackNote = "No Gemini API key is saved in Settings, so the offline reader was used. ";
				logger?.LogWarning("Gemini prescription reading skipped: no API key is configured.");
			}
			else
			{
				try
				{
					return await MatchAsync(await ReadWithGeminiAsync(imageBytes, mimeType, cancellationToken), cancellationToken);
				}
				catch (Exception ex) when (((ex is HttpRequestException || ex is InvalidDataException || ex is JsonException || ex is TaskCanceledException) ? true : false) && !cancellationToken.IsCancellationRequested)
				{
					logger?.LogError(ex, "Gemini prescription reading failed: {Reason}", ex.Message);
					fallbackNote = "The AI reader was unavailable (" + ex.Message + "), so the offline reader was used. ";
				}
			}
		}
		PrescriptionParseResultDto prescriptionParseResultDto = await MatchAsync(await ReadLocallyAsync(imageBytes, cancellationToken), cancellationToken);
		return prescriptionParseResultDto with
		{
			Message = fallbackNote + prescriptionParseResultDto.Message,
			ApiKeyMissing = !IsCloudConfigured
		};
	}

	private async Task<PrescriptionParseResultDto> ReadLocallyAsync(byte[] imageBytes, CancellationToken cancellationToken)
	{
		if (textRecognizer == null)
		{
			return new PrescriptionParseResultDto
			{
				Message = "No offline text recogniser is available on this PC. Enter the items manually."
			};
		}
		string text;
		try
		{
			text = await textRecognizer.RecognizeAsync(imageBytes, cancellationToken);
		}
		catch (Exception ex) when ((ex is InvalidOperationException || ex is InvalidDataException || ex is NotSupportedException || ex is IOException) ? true : false)
		{
			return new PrescriptionParseResultDto
			{
				Message = "The image could not be read: " + ex.Message
			};
		}
		PrescriptionParseResultDto prescriptionParseResultDto = PrescriptionTextParser.Parse(text);
		return prescriptionParseResultDto with
		{
			Message = (prescriptionParseResultDto.HasContent ? "Read offline. Please verify every field against the prescription." : "No readable text was found. Enter the items manually.")
		};
	}

	private async Task<PrescriptionParseResultDto> ReadWithGeminiAsync(byte[] imageBytes, string mimeType, CancellationToken cancellationToken)
	{
		string value = apiKeyStore.Read() ?? throw new InvalidDataException("No Gemini API key.");
		var inputValue = new
		{
			contents = new[]
			{
				new
				{
					parts = new object[2]
					{
						new
						{
							text = "You are reading an Indian medical prescription (possibly handwritten). Return only strict JSON with exactly these fields:\r\ndoctorName, doctorRegistrationNo, patientName, prescriptionDate (YYYY-MM-DD or empty),\r\nmedicines (array of drugName, dosage (for example 1-0-1 or BD), duration (for example 5 days)),\r\nrawText (all the text you can read).\r\nDo not invent unreadable values: use empty strings and omit medicines you cannot read."
						},
						new
						{
							inline_data = new
							{
								mime_type = mimeType,
								data = Convert.ToBase64String(imageBytes)
							}
						}
					}
				}
			},
			generationConfig = new
			{
				responseMimeType = "application/json"
			}
		};
		using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, "https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent")
		{
			Content = JsonContent.Create(inputValue)
		};
		request.Headers.Add("x-goog-api-key", value);
		using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
		if (!response.IsSuccessStatusCode)
		{
			string text = await response.Content.ReadAsStringAsync(cancellationToken);
			logger?.LogError("Gemini returned HTTP {StatusCode} {Reason}: {ResponseBody}", (int)response.StatusCode, response.ReasonPhrase, (text.Length > 2000) ? text.Substring(0, 2000) : text);
			throw new HttpRequestException($"Gemini prescription reading failed (HTTP {(int)response.StatusCode} {response.ReasonPhrase}).", null, response.StatusCode);
		}
		using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
		using JsonDocument jsonDocument = await JsonDocument.ParseAsync(stream, default, cancellationToken);
		if (!jsonDocument.RootElement.TryGetProperty("candidates", out var value2) || value2.GetArrayLength() == 0 || !value2[0].TryGetProperty("content", out var value3) || !value3.TryGetProperty("parts", out var value4) || value4.GetArrayLength() == 0 || !value4[0].TryGetProperty("text", out var value5))
		{
			throw new InvalidDataException("Gemini returned no prescription data.");
		}
		return PrescriptionDtoMapper.FromGeminiJson(value5.GetString())with
		{
			Message = "Read with Gemini. Please verify every field against the prescription."
		};
	}

	private async Task<PrescriptionParseResultDto> MatchAsync(PrescriptionParseResultDto result, CancellationToken cancellationToken)
	{
		if (result.DetectedMedicines.Count == 0)
		{
			return result;
		}
		return MatchMedicines(result, await (from drug in context.Drugs.AsNoTracking()
			where drug.IsActive
			select new DrugNameCandidate(drug.Id, drug.Name, drug.BrandName, drug.GenericName)).ToListAsync(cancellationToken));
	}

	public static PrescriptionParseResultDto MatchMedicines(PrescriptionParseResultDto result, IReadOnlyCollection<DrugNameCandidate> candidates)
	{
		return result with
		{
			DetectedMedicines = result.DetectedMedicines.Select((PrescribedItemDto item) =>
			{
				DrugNameMatch drugNameMatch = FuzzyMatcher.FindBestMatch(item.DrugName, candidates);
				return ((object)drugNameMatch != null) ? item with
				{
					MatchedCatalogProductId = drugNameMatch.DrugId,
					ConfidenceScore = drugNameMatch.Score
				} : item with
				{
					MatchedCatalogProductId = null,
					ConfidenceScore = 0.0
				};
			}).ToList()
		};
	}
}
