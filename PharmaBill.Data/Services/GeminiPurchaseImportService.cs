using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Security.Cryptography;
using System.Net.Http.Json;

namespace PharmaBill.Data.Services;

public sealed class GeminiApiKeyStore
{
    private readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PharmaBill",
        "settings",
        "gemini-api-key.dpapi");

    public void Save(string apiKey)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Gemini API keys are protected using Windows DPAPI.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        var directory = Path.GetDirectoryName(_path)
            ?? throw new InvalidOperationException("The API-key settings path is invalid.");
        Directory.CreateDirectory(directory);
        var protectedBytes = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(apiKey.Trim()),
            null,
            DataProtectionScope.CurrentUser);
        File.WriteAllBytes(_path, protectedBytes);
    }

    public string? Read()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Gemini API keys are protected using Windows DPAPI.");
        }

        if (!File.Exists(_path))
        {
            return null;
        }

        var protectedBytes = File.ReadAllBytes(_path);
        return Encoding.UTF8.GetString(ProtectedData.Unprotect(
            protectedBytes,
            null,
            DataProtectionScope.CurrentUser));
    }

    public void Delete()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }
}

public sealed record ExtractedPurchaseInvoice(
    string DocumentType,
    string Supplier,
    string InvoiceNo,
    string InvoiceDate,
    IReadOnlyList<ExtractedPurchaseLine> Items,
    decimal Subtotal,
    decimal GrandTotal);

public sealed record ExtractedPurchaseLine(
    string ItemName,
    string Batch,
    string Expiry,
    decimal Quantity,
    decimal Free,
    decimal Mrp,
    decimal Rate,
    decimal Gst,
    decimal Amount);

public sealed class GeminiPurchaseImportService(
    HttpClient httpClient,
    GeminiApiKeyStore apiKeyStore)
{
    private const long MaximumDocumentBytes = 20 * 1024 * 1024;
    private static readonly JsonSerializerOptions StrictJsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public async Task<ExtractedPurchaseInvoice> ExtractAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        var apiKey = apiKeyStore.Read();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("Add your Gemini API key in Settings before importing a bill.");
        }

        var fullPath = Path.GetFullPath(filePath);
        var mimeType = Path.GetExtension(fullPath).ToLowerInvariant() switch
        {
            ".pdf" => "application/pdf",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            _ => throw new InvalidOperationException("Choose a PDF, PNG or JPEG purchase document.")
        };
        var fileInfo = new FileInfo(fullPath);
        if (!fileInfo.Exists)
        {
            throw new FileNotFoundException("The selected purchase document could not be found.", fullPath);
        }

        if (fileInfo.Length == 0 || fileInfo.Length > MaximumDocumentBytes)
        {
            throw new InvalidOperationException("The purchase document must be between 1 byte and 20 MB.");
        }

        var fileBytes = await File.ReadAllBytesAsync(fullPath, cancellationToken);
        var requestBody = new
        {
            contents = new[]
            {
                new
                {
                    parts = new object[]
                    {
                        new
                        {
                            text = """
                                Extract this supplier bill as a purchase document. Return only strict JSON with exactly these fields:
                                documentType (PURCHASE_INVOICE or PURCHASE_RETURN), supplier, invoiceNo, invoiceDate (YYYY-MM-DD),
                                items (array of itemName, batch, expiry (MM/YYYY or YYYY-MM-DD), quantity, free, mrp, rate, gst, amount),
                                subtotal, grandTotal.
                                Use numbers for quantities and money, no currency symbols. Do not invent unreadable values: use empty strings or 0.
                                """
                        },
                        new
                        {
                            inline_data = new
                            {
                                mime_type = mimeType,
                                data = Convert.ToBase64String(fileBytes)
                            }
                        }
                    }
                }
            },
            generationConfig = new { responseMimeType = "application/json" }
        };
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent")
        {
            Content = JsonContent.Create(requestBody)
        };
        request.Headers.Add("x-goog-api-key", apiKey);
        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException(
                $"Gemini purchase-document extraction failed ({(int)response.StatusCode}): {responseText}",
                null,
                response.StatusCode);
        }

        using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var responseJson = await JsonDocument.ParseAsync(responseStream, cancellationToken: cancellationToken);
        if (!responseJson.RootElement.TryGetProperty("candidates", out var candidates) ||
            candidates.GetArrayLength() == 0 ||
            !candidates[0].TryGetProperty("content", out var content) ||
            !content.TryGetProperty("parts", out var parts) ||
            parts.GetArrayLength() == 0 ||
            !parts[0].TryGetProperty("text", out var textElement))
        {
            throw new InvalidDataException("Gemini returned no purchase-document JSON.");
        }

        var extracted = JsonSerializer.Deserialize<ExtractedPurchaseInvoice>(
            textElement.GetString() ?? string.Empty,
            StrictJsonOptions) ?? throw new InvalidDataException("Gemini returned an empty purchase document.");
        ValidateReviewData(extracted);
        return extracted;
    }

    private static void ValidateReviewData(ExtractedPurchaseInvoice invoice)
    {
        if (invoice.DocumentType is not ("PURCHASE_INVOICE" or "PURCHASE_RETURN"))
        {
            throw new InvalidDataException("Document type must be PURCHASE_INVOICE or PURCHASE_RETURN.");
        }

        if (string.IsNullOrWhiteSpace(invoice.Supplier) ||
            string.IsNullOrWhiteSpace(invoice.InvoiceNo) ||
            invoice.Items.Count == 0)
        {
            throw new InvalidDataException("The extracted document is missing its supplier, invoice number or items.");
        }

        foreach (var item in invoice.Items)
        {
            if (string.IsNullOrWhiteSpace(item.ItemName) ||
                string.IsNullOrWhiteSpace(item.Batch) ||
                item.Quantity <= 0 ||
                item.Free < 0 ||
                item.Mrp < 0 ||
                item.Rate < 0 ||
                item.Gst < 0 ||
                decimal.Round(item.Quantity * item.Rate, 2, MidpointRounding.AwayFromZero) !=
                decimal.Round(item.Amount, 2, MidpointRounding.AwayFromZero))
            {
                throw new InvalidDataException("An extracted row is incomplete or quantity × rate does not equal amount.");
            }
        }

        var computedSubtotal = invoice.Items.Sum(item => item.Amount);
        if (decimal.Round(computedSubtotal, 2, MidpointRounding.AwayFromZero) !=
            decimal.Round(invoice.Subtotal, 2, MidpointRounding.AwayFromZero))
        {
            throw new InvalidDataException("The extracted item amounts do not match the bill subtotal.");
        }

    }
}
