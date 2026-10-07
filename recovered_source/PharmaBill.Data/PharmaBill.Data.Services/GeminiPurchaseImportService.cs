using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace PharmaBill.Data.Services;

public sealed class GeminiPurchaseImportService(HttpClient httpClient, GeminiApiKeyStore apiKeyStore)
{
	private const long MaximumDocumentBytes = 20971520L;

	private static readonly JsonSerializerOptions StrictJsonOptions = new JsonSerializerOptions
	{
		PropertyNameCaseInsensitive = false,
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
	};

	public async Task<ExtractedPurchaseInvoice> ExtractAsync(string filePath, CancellationToken cancellationToken = default(CancellationToken))
	{
		string apiKey = apiKeyStore.Read();
		if (string.IsNullOrWhiteSpace(apiKey))
		{
			throw new InvalidOperationException("Add your Gemini API key in Settings before importing a bill.");
		}
		string fullPath = Path.GetFullPath(filePath);
		string text;
		switch (Path.GetExtension(fullPath).ToLowerInvariant())
		{
		case ".pdf":
			text = "application/pdf";
			break;
		case ".png":
			text = "image/png";
			break;
		case ".jpg":
		case ".jpeg":
			text = "image/jpeg";
			break;
		default:
			throw new InvalidOperationException("Choose a PDF, PNG or JPEG purchase document.");
		}
		string mimeType = text;
		FileInfo fileInfo = new FileInfo(fullPath);
		if (!fileInfo.Exists)
		{
			throw new FileNotFoundException("The selected purchase document could not be found.", fullPath);
		}
		if (fileInfo.Length == 0L || fileInfo.Length > 20971520)
		{
			throw new InvalidOperationException("The purchase document must be between 1 byte and 20 MB.");
		}
		byte[] inArray = await File.ReadAllBytesAsync(fullPath, cancellationToken);
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
							text = "Extract this supplier bill as a purchase document. Return only strict JSON with exactly these fields:\r\ndocumentType (PURCHASE_INVOICE or PURCHASE_RETURN), supplier, invoiceNo, invoiceDate (YYYY-MM-DD),\r\nitems (array of itemName, batch, expiry (MM/YYYY or YYYY-MM-DD), quantity, free, mrp, rate, gst, amount),\r\nsubtotal, grandTotal.\r\nUse numbers for quantities and money, no currency symbols. Do not invent unreadable values: use empty strings or 0."
						},
						new
						{
							inline_data = new
							{
								mime_type = mimeType,
								data = Convert.ToBase64String(inArray)
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
		request.Headers.Add("x-goog-api-key", apiKey);
		using HttpResponseMessage response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
		if (!response.IsSuccessStatusCode)
		{
			string value = await response.Content.ReadAsStringAsync(cancellationToken);
			throw new HttpRequestException($"Gemini purchase-document extraction failed ({(int)response.StatusCode}): {value}", null, response.StatusCode);
		}
		using Stream responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
		using JsonDocument jsonDocument = await JsonDocument.ParseAsync(responseStream, default, cancellationToken);
		if (!jsonDocument.RootElement.TryGetProperty("candidates", out var value2) || value2.GetArrayLength() == 0 || !value2[0].TryGetProperty("content", out var value3) || !value3.TryGetProperty("parts", out var value4) || value4.GetArrayLength() == 0 || !value4[0].TryGetProperty("text", out var value5))
		{
			throw new InvalidDataException("Gemini returned no purchase-document JSON.");
		}
		ExtractedPurchaseInvoice? extractedPurchaseInvoice = JsonSerializer.Deserialize<ExtractedPurchaseInvoice>(value5.GetString() ?? string.Empty, StrictJsonOptions) ?? throw new InvalidDataException("Gemini returned an empty purchase document.");
		ValidateReviewData(extractedPurchaseInvoice);
		return extractedPurchaseInvoice;
	}

	private static void ValidateReviewData(ExtractedPurchaseInvoice invoice)
	{
		string documentType = invoice.DocumentType;
		if ((!(documentType == "PURCHASE_INVOICE") && !(documentType == "PURCHASE_RETURN")) || 1 == 0)
		{
			throw new InvalidDataException("Document type must be PURCHASE_INVOICE or PURCHASE_RETURN.");
		}
		if (string.IsNullOrWhiteSpace(invoice.Supplier) || string.IsNullOrWhiteSpace(invoice.InvoiceNo) || invoice.Items.Count == 0)
		{
			throw new InvalidDataException("The extracted document is missing its supplier, invoice number or items.");
		}
		foreach (ExtractedPurchaseLine item in invoice.Items)
		{
			if (string.IsNullOrWhiteSpace(item.ItemName) || string.IsNullOrWhiteSpace(item.Batch) || item.Quantity <= 0m || item.Free < 0m || item.Mrp < 0m || item.Rate < 0m || item.Gst < 0m || decimal.Round(item.Quantity * item.Rate, 2, MidpointRounding.AwayFromZero) != decimal.Round(item.Amount, 2, MidpointRounding.AwayFromZero))
			{
				throw new InvalidDataException("An extracted row is incomplete or quantity × rate does not equal amount.");
			}
		}
		if (decimal.Round(invoice.Items.Sum((ExtractedPurchaseLine item) => item.Amount), 2, MidpointRounding.AwayFromZero) != decimal.Round(invoice.Subtotal, 2, MidpointRounding.AwayFromZero))
		{
			throw new InvalidDataException("The extracted item amounts do not match the bill subtotal.");
		}
	}
}
