using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace PharmaBill.Data.Services;

public sealed class GeminiPurchaseImportService(HttpClient httpClient, GeminiApiKeyStore apiKeyStore, ILogger<GeminiPurchaseImportService>? logger = null)
{
	public const string DefaultModel = GeminiGenerateContent.DefaultModel;

	private const long MaximumDocumentBytes = 20971520L;

	private static readonly JsonSerializerOptions LenientJsonOptions = new JsonSerializerOptions
	{
		PropertyNameCaseInsensitive = true,
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
		NumberHandling = JsonNumberHandling.AllowReadingFromString
	};

	private static readonly TimeSpan VisionOverallTimeout = TimeSpan.FromSeconds(90);

	public async Task<ExtractedPurchaseInvoice> ExtractAsync(string filePath, CancellationToken cancellationToken = default(CancellationToken))
	{
		string apiKey = apiKeyStore.Read();
		if (string.IsNullOrWhiteSpace(apiKey))
		{
			throw new InvalidOperationException("Add your Gemini API key in Settings before importing a bill.");
		}

		string fullPath = Path.GetFullPath(filePath);
		FileInfo fileInfo = new FileInfo(fullPath);
		if (!fileInfo.Exists)
		{
			throw new FileNotFoundException("The selected purchase document could not be found.", fullPath);
		}

		if (fileInfo.Length == 0L || fileInfo.Length > MaximumDocumentBytes)
		{
			throw new InvalidOperationException("The purchase document must be between 1 byte and 20 MB.");
		}

		byte[] bytes = await File.ReadAllBytesAsync(fullPath, cancellationToken);
		string mimeType = DetectMimeType(fullPath, bytes);
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
							text = "You are reading an Indian pharmacy distributor / supplier invoice photo or PDF. "
								+ "Extract the purchase bill. Return ONLY strict JSON (no markdown) with exactly these fields:\n"
								+ "documentType (PURCHASE_INVOICE or PURCHASE_RETURN),\n"
								+ "supplier, invoiceNo, invoiceDate (YYYY-MM-DD),\n"
								+ "items (array of objects with: itemName, batch, expiry (MM/YYYY), quantity, free, mrp, rate, gst, amount),\n"
								+ "subtotal, grandTotal.\n"
								+ "HEADER RULES:\n"
								+ "- supplier = text under 'Seller (Wholesaler)' ONLY (e.g. 'M/s Example Pharma Distributors'). "
								+ "NEVER use Buyer (Retailer), filenames, camera ids, or device ids.\n"
								+ "- invoiceNo = value after 'Invoice No.:' or 'Bill No.'.\n"
								+ "- invoiceDate = YYYY-MM-DD near Invoice No.\n"
								+ "TABLE RULES (critical):\n"
								+ "- Read EVERY medicine row between the Product table header and 'Taxable value'.\n"
								+ "- Typical demo / wholesaler bills have exactly 6 medicines — extract ALL of them when present:\n"
								+ "  Paracetamol, Amoxicillin+Clavulanic Acid, Pantoprazole, Cetirizine, Azithromycin, Metformin.\n"
								+ "- NEVER confuse dosage strength (500 mg, 125 mg, 40 mg, 10 mg) with Qty.\n"
								+ "- Qty is the integer pack/quantity column AFTER Mfg/Exp (e.g. 20, 100, 60, 80, 50, 100).\n"
								+ "- Rate is the Rate (Rs.) column (e.g. 180.00, 145.00). Amount = Qty × Rate when readable.\n"
								+ "- Batch codes look like PCT2407, AMC2512, PAN2509, CET2601, AZI2603, MET2511.\n"
								+ "- Mfg/Exp composites '07/25 - 06/27': expiry = SECOND date as MM/YYYY (06/2027).\n"
								+ "- STOP before Taxable value / CGST / SGST / Grand total / Amount in words / Signatory.\n"
								+ "- IGNORE Drug Licence, Form 20/21, GSTIN, Tax Invoice, Buyer box, Dummy/Training headers.\n"
								+ "If a field is unreadable keep the row with batch \"NA\" or expiry \"\". Do not invent unseen medicines."
						},
						new
						{
							inline_data = new
							{
								mime_type = mimeType,
								data = Convert.ToBase64String(bytes)
							}
						}
					}
				}
			},
			generationConfig = new
			{
				responseMimeType = "application/json",
				temperature = 0.1
			}
		};

		using CancellationTokenSource overall = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		overall.CancelAfter(VisionOverallTimeout);
		using HttpResponseMessage response = await GeminiGenerateContent.SendAsync(
			httpClient,
			apiKey,
			inputValue,
			overall.Token,
			logger,
			perAttemptTimeout: TimeSpan.FromSeconds(45));
		if (!response.IsSuccessStatusCode)
		{
			string body = await response.Content.ReadAsStringAsync(cancellationToken);
			logger?.LogWarning("Gemini purchase extract HTTP {Status}: {Body}", (int)response.StatusCode, body.Length > 500 ? body[..500] : body);
			throw new HttpRequestException(
				GeminiGenerateContent.UserMessageFor(response.StatusCode),
				null,
				response.StatusCode);
		}

		using Stream responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
		using JsonDocument jsonDocument = await JsonDocument.ParseAsync(responseStream, default, cancellationToken);
		if (!jsonDocument.RootElement.TryGetProperty("candidates", out var candidates)
			|| candidates.GetArrayLength() == 0
			|| !candidates[0].TryGetProperty("content", out var content)
			|| !content.TryGetProperty("parts", out var parts)
			|| parts.GetArrayLength() == 0
			|| !parts[0].TryGetProperty("text", out var textElement))
		{
			throw new InvalidDataException("Gemini returned no purchase-document JSON.");
		}

		string modelText = UnwrapJson(textElement.GetString());
		System.Diagnostics.Debug.WriteLine($"Gemini purchase import raw JSON ({modelText.Length} chars): {modelText}");
		ExtractedPurchaseInvoice invoice = DeserializeInvoice(modelText);
		System.Diagnostics.Debug.WriteLine($"Gemini purchase import deserialized {invoice.Items.Count} item(s).");
		return NormalizeInvoice(invoice);
	}

	internal static ExtractedPurchaseInvoice DeserializeInvoice(string modelText)
	{
		string cleaned = UnwrapJson(modelText);
		if (string.IsNullOrWhiteSpace(cleaned))
		{
			throw new InvalidDataException("Gemini returned an empty purchase document.");
		}

		try
		{
			using JsonDocument document = JsonDocument.Parse(cleaned);
			JsonElement root = document.RootElement;
			if (root.ValueKind == JsonValueKind.Array)
			{
				List<ExtractedPurchaseLine> lines = ParseLineArray(root);
				decimal sub = lines.Sum(ComputeAmount);
				return new ExtractedPurchaseInvoice("PURCHASE_INVOICE", "Supplier", "PHONE-" + DateTime.Now.ToString("yyyyMMdd-HHmm"), DateTime.Today.ToString("yyyy-MM-dd"), lines, sub, sub);
			}

			if (root.ValueKind == JsonValueKind.Object)
			{
				if (root.TryGetProperty("invoice", out JsonElement nested)
					|| root.TryGetProperty("data", out nested)
					|| root.TryGetProperty("bill", out nested)
					|| root.TryGetProperty("result", out nested))
				{
					root = nested;
				}

				if (TryGetPropertyIgnoreCase(root, "items", out JsonElement itemsElement)
					|| TryGetPropertyIgnoreCase(root, "lines", out itemsElement)
					|| TryGetPropertyIgnoreCase(root, "medicines", out itemsElement)
					|| TryGetPropertyIgnoreCase(root, "products", out itemsElement))
				{
					List<ExtractedPurchaseLine> flexibleLines = ParseLineArray(itemsElement);
					if (flexibleLines.Count > 0)
					{
						string supplier = ReadString(root, "supplier", "supplierName", "distributor") ?? "Supplier";
						string invoiceNo = ReadString(root, "invoiceNo", "invoiceNumber", "billNo", "billNumber")
							?? "PHONE-" + DateTime.Now.ToString("yyyyMMdd-HHmm");
						string invoiceDate = ReadString(root, "invoiceDate", "billDate", "date")
							?? DateTime.Today.ToString("yyyy-MM-dd");
						string documentType = ReadString(root, "documentType", "type") ?? "PURCHASE_INVOICE";
						decimal subtotal = ReadDecimal(root, "subtotal", "taxable") ?? flexibleLines.Sum(ComputeAmount);
						decimal grand = ReadDecimal(root, "grandTotal", "total", "netAmount") ?? subtotal;
						return new ExtractedPurchaseInvoice(documentType, supplier, invoiceNo, invoiceDate, flexibleLines, subtotal, grand);
					}
				}

				ExtractedPurchaseInvoice? parsed = JsonSerializer.Deserialize<ExtractedPurchaseInvoice>(root.GetRawText(), LenientJsonOptions);
				if (parsed != null && parsed.Items is { Count: > 0 })
				{
					return parsed;
				}
			}
		}
		catch (JsonException ex)
		{
			throw new InvalidDataException("Gemini purchase JSON could not be read: " + ex.Message, ex);
		}

		throw new InvalidDataException("Gemini returned an empty purchase document.");
	}

	private static List<ExtractedPurchaseLine> ParseLineArray(JsonElement arrayElement)
	{
		List<ExtractedPurchaseLine> lines = new List<ExtractedPurchaseLine>();
		if (arrayElement.ValueKind != JsonValueKind.Array)
		{
			return lines;
		}

		foreach (JsonElement row in arrayElement.EnumerateArray())
		{
			if (row.ValueKind != JsonValueKind.Object)
			{
				continue;
			}

			string? name = ReadString(row, "itemName", "medicineName", "medicine", "name", "drug", "product", "description", "item");
			if (string.IsNullOrWhiteSpace(name))
			{
				continue;
			}

			string batch = ReadString(row, "batch", "batchNo", "batchNumber") ?? "NA";
			string expiry = NormalizeExpiryText(ReadString(row, "expiry", "expiryDate", "exp", "mfgExp", "mfg_exp", "expDate"));
			decimal quantity = ReadDecimal(row, "quantity", "qty", "qtyIn", "packQty") ?? 0m;
			decimal free = ReadDecimal(row, "free", "freeQty", "scheme") ?? 0m;
			decimal mrp = ReadDecimal(row, "mrp", "MRP", "maxRetailPrice") ?? 0m;
			decimal rate = ReadDecimal(row, "rate", "ptr", "price", "unitRate", "purchaseRate") ?? 0m;
			decimal gst = ReadDecimal(row, "gst", "gstPercent", "gstRate", "tax", "taxPercent") ?? 0m;
			decimal amount = ReadDecimal(row, "amount", "lineAmount", "value", "netAmount") ?? 0m;
			decimal discount = ReadDecimal(row, "discount", "discountAmount", "disc") ?? 0m;
			if (amount <= 0m && quantity > 0m && rate > 0m)
			{
				amount = decimal.Round(quantity * rate - discount, 2, MidpointRounding.AwayFromZero);
			}

			lines.Add(new ExtractedPurchaseLine(name.Trim(), batch.Trim(), expiry.Trim(), quantity, free, mrp, rate, gst, amount));
		}

		return lines;
	}

	private static bool TryGetPropertyIgnoreCase(JsonElement element, string name, out JsonElement value)
	{
		foreach (JsonProperty property in element.EnumerateObject())
		{
			if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
			{
				value = property.Value;
				return true;
			}
		}

		value = default;
		return false;
	}

	private static string? ReadString(JsonElement element, params string[] names)
	{
		foreach (string name in names)
		{
			if (!TryGetPropertyIgnoreCase(element, name, out JsonElement value))
			{
				continue;
			}

			if (value.ValueKind == JsonValueKind.String)
			{
				string? text = value.GetString();
				if (!string.IsNullOrWhiteSpace(text))
				{
					return text.Trim();
				}
			}
			else if (value.ValueKind is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False)
			{
				return value.ToString();
			}
		}

		return null;
	}

	private static decimal? ReadDecimal(JsonElement element, params string[] names)
	{
		foreach (string name in names)
		{
			if (!TryGetPropertyIgnoreCase(element, name, out JsonElement value))
			{
				continue;
			}

			if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out decimal number))
			{
				return number;
			}

			if (value.ValueKind == JsonValueKind.String)
			{
				string? text = value.GetString();
				if (string.IsNullOrWhiteSpace(text))
				{
					continue;
				}

				text = text.Replace("₹", string.Empty, StringComparison.Ordinal).Replace(",", string.Empty, StringComparison.Ordinal).Replace("%", string.Empty, StringComparison.Ordinal).Trim();
				if (decimal.TryParse(text, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out decimal parsed)
					|| decimal.TryParse(text, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.CurrentCulture, out parsed))
				{
					return parsed;
				}
			}
		}

		return null;
	}

	internal static ExtractedPurchaseInvoice NormalizeInvoice(ExtractedPurchaseInvoice invoice)
	{
		string documentType = string.Equals(invoice.DocumentType, "PURCHASE_RETURN", StringComparison.OrdinalIgnoreCase)
			? "PURCHASE_RETURN"
			: "PURCHASE_INVOICE";
		string supplier = SanitizeSupplierName(invoice.Supplier);
		string invoiceNo = SanitizeInvoiceNo(invoice.InvoiceNo);
		string invoiceDate = string.IsNullOrWhiteSpace(invoice.InvoiceDate)
			? DateTime.Today.ToString("yyyy-MM-dd")
			: invoice.InvoiceDate.Trim();

		List<ExtractedPurchaseLine> items = new List<ExtractedPurchaseLine>();
		foreach (ExtractedPurchaseLine raw in invoice.Items ?? Array.Empty<ExtractedPurchaseLine>())
		{
			if (string.IsNullOrWhiteSpace(raw.ItemName) || IsNonMedicineNoise(raw.ItemName))
			{
				continue;
			}

			// Never treat dosage strength as Qty when Amount÷Rate implies a different pack count.
			decimal quantity = raw.Quantity > 0m ? raw.Quantity : 1m;
			decimal free = raw.Free < 0m ? 0m : raw.Free;
			decimal mrp = raw.Mrp < 0m ? 0m : raw.Mrp;
			decimal rate = raw.Rate < 0m ? 0m : raw.Rate;
			decimal gst = raw.Gst < 0m ? 0m : raw.Gst;
			decimal amount = raw.Amount > 0m ? decimal.Round(raw.Amount, 2, MidpointRounding.AwayFromZero) : 0m;
			if (rate > 0m && amount > 0m)
			{
				decimal derived = decimal.Round(amount / rate, 0, MidpointRounding.AwayFromZero);
				if (derived >= 1m && derived <= 5000m
					&& (quantity <= 0m || IsLikelyDosageStrength(quantity, raw.ItemName) || Math.Abs(quantity * rate - amount) > 1m))
				{
					quantity = derived;
				}
			}

			amount = ComputeAmount(raw with { Quantity = quantity, Rate = rate, Amount = amount });
			string batch = string.IsNullOrWhiteSpace(raw.Batch) || string.Equals(raw.Batch.Trim(), "BATCH", StringComparison.OrdinalIgnoreCase)
				? "NA"
				: raw.Batch.Trim();
			if (IsNonMedicineNoise(batch) || Regex.IsMatch(batch, @"form\s*20|form\s*21|dummy", RegexOptions.IgnoreCase))
			{
				batch = "NA";
			}

			string expiry = NormalizeExpiryText(raw.Expiry);
			items.Add(new ExtractedPurchaseLine(raw.ItemName.Trim(), batch, expiry, quantity, free, mrp, rate, gst, amount));
		}

		if (items.Count == 0)
		{
			throw new InvalidDataException("No medicine lines could be extracted from this bill image. Try a clearer photo or enter lines manually.");
		}

		decimal subtotal = items.Sum(item => item.Amount);
		decimal grand = invoice.GrandTotal > 0m ? invoice.GrandTotal : subtotal;
		if (invoice.Subtotal > 0m && Math.Abs(invoice.Subtotal - subtotal) <= 1m)
		{
			subtotal = invoice.Subtotal;
		}

		return new ExtractedPurchaseInvoice(documentType, supplier, invoiceNo, invoiceDate, items, subtotal, grand);
	}

	private static decimal ComputeAmount(ExtractedPurchaseLine item)
	{
		decimal computed = decimal.Round(item.Quantity * item.Rate, 2, MidpointRounding.AwayFromZero);
		if (item.Amount > 0m)
		{
			if (item.Quantity > 0m && item.Rate > 0m && Math.Abs(item.Amount - computed) > 0.05m)
			{
				return computed;
			}

			return decimal.Round(item.Amount, 2, MidpointRounding.AwayFromZero);
		}

		return computed;
	}

	/// <summary>True when quantity looks like a dosage strength embedded in the product name (e.g. 125 from "125 mg").</summary>
	private static bool IsLikelyDosageStrength(decimal quantity, string itemName)
	{
		if (quantity != decimal.Truncate(quantity) || quantity is < 5m or > 1000m)
		{
			return false;
		}

		string token = decimal.Truncate(quantity).ToString(System.Globalization.CultureInfo.InvariantCulture);
		return Regex.IsMatch(
			itemName ?? string.Empty,
			@"\b" + Regex.Escape(token) + @"\s*(mg|mcg|ml|gm|g)\b",
			RegexOptions.IgnoreCase);
	}

	internal static string DetectMimeType(string path, byte[] bytes)
	{
		if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
		{
			return "image/jpeg";
		}

		if (bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
		{
			return "image/png";
		}

		if (bytes.Length >= 4 && bytes[0] == 0x25 && bytes[1] == 0x50 && bytes[2] == 0x44 && bytes[3] == 0x46)
		{
			return "application/pdf";
		}

		if (bytes.Length >= 12
			&& bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46
			&& bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50)
		{
			return "image/webp";
		}

		return Path.GetExtension(path).ToLowerInvariant() switch
		{
			".pdf" => "application/pdf",
			".png" => "image/png",
			".jpg" or ".jpeg" or ".jfif" => "image/jpeg",
			".webp" => "image/webp",
			".heic" or ".heif" => "image/heic",
			_ => "image/jpeg"
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

		int objectStart = text.IndexOf('{');
		int arrayStart = text.IndexOf('[');
		if (objectStart < 0 && arrayStart < 0)
		{
			return text;
		}

		if (arrayStart >= 0 && (objectStart < 0 || arrayStart < objectStart))
		{
			int end = text.LastIndexOf(']');
			return end > arrayStart ? text[arrayStart..(end + 1)] : text;
		}

		int objectEnd = text.LastIndexOf('}');
		return objectEnd > objectStart ? text[objectStart..(objectEnd + 1)] : text;
	}

	public static string SanitizeSupplierName(string? raw)
	{
		string text = (raw ?? string.Empty).Trim();
		if (text.Length == 0 || LooksLikeFileOrDeviceId(text))
		{
			return "Supplier";
		}

		// Never keep the Buyer (Retailer) box as the supplier.
		if (Regex.IsMatch(text, @"\bbuyer\b|\bretailer\b|medical\s*hall|sample\s*hall", RegexOptions.IgnoreCase))
		{
			return "Supplier";
		}

		text = Regex.Replace(text, @"^(?:W/s|Ws|Ms)\.?\s+", "M/s ", RegexOptions.IgnoreCase);

		if (!text.StartsWith("M/s", StringComparison.OrdinalIgnoreCase)
			&& !text.Contains("Pharma", StringComparison.OrdinalIgnoreCase)
			&& !text.Contains("Distribut", StringComparison.OrdinalIgnoreCase)
			&& !text.Contains("Agency", StringComparison.OrdinalIgnoreCase))
		{
			// Keep genuine names; still reject obvious junk.
			if (text.Contains("Drug Licence", StringComparison.OrdinalIgnoreCase)
				|| text.Contains("GSTIN", StringComparison.OrdinalIgnoreCase)
				|| text.Contains("Tax Invoice", StringComparison.OrdinalIgnoreCase))
			{
				return "Supplier";
			}
		}

		if (!text.StartsWith("M/s", StringComparison.OrdinalIgnoreCase)
			&& (text.Contains("Pharma", StringComparison.OrdinalIgnoreCase) || text.Contains("Distribut", StringComparison.OrdinalIgnoreCase)))
		{
			text = "M/s " + text;
		}

		return text.Length > 120 ? text[..120] : text;
	}

	public static string SanitizeInvoiceNo(string? raw)
	{
		string text = (raw ?? string.Empty).Trim();
		if (text.Length == 0 || LooksLikeFileOrDeviceId(text))
		{
			return "UNKNOWN";
		}

		return text;
	}

	public static bool LooksLikeFileOrDeviceId(string text)
	{
		return Regex.IsMatch(text, @"\.(jpe?g|png|pdf|heic|webp)$", RegexOptions.IgnoreCase)
			|| Regex.IsMatch(text, @"^(IMG_|DSC_|PXL_|VID_|Screenshot|Camera|WIN_|PHONE-)", RegexOptions.IgnoreCase)
			|| text.Contains("normalized", StringComparison.OrdinalIgnoreCase)
			|| text.Contains("InwardScan", StringComparison.OrdinalIgnoreCase)
			|| Regex.IsMatch(text, @"^[0-9a-f]{8,}(-[0-9a-f]{4,})+$", RegexOptions.IgnoreCase);
	}

	public static bool IsNonMedicineNoise(string name)
	{
		string text = name.Trim();
		return Regex.IsMatch(
			text,
			@"drug\s*(?:no\.?|number|#|licen[cs]e|license)|form\s*20|form\s*21|\bgstin\b|\bcstin\b|"
			+ @"\bseller\b|\bbuyer\b|\bwholesaler\b|\bretailer\b|tax\s*invoice|cash[-\s]?credit|\bmemo\b|"
			+ @"dummy\s*invoice|training|demo\s*only|jurisdiction|authorised\s*signat|authorized\s*signat|"
			+ @"taxable\s*value|\bcgst\b|\bsgst\b|\bigst\b|grand\s*total|amount\s*in\s*words|"
			+ @"terms\s*&?\s*conditions|for\s+office\s+use|dummy[.\-]?(?:ws|rt)",
			RegexOptions.IgnoreCase);
	}

	/// <summary>
	/// From composite Mfg/Exp cells like "07/25 - 06/27", keep the second date as expiry (MM/YYYY).
	/// Unparseable values become empty string — the medicine row is still kept.
	/// </summary>
	internal static string NormalizeExpiryText(string? raw)
	{
		if (string.IsNullOrWhiteSpace(raw))
		{
			return string.Empty;
		}

		string text = raw.Trim();
		Match labeled = Regex.Match(
			text,
			@"(?:exp(?:iry)?|e\.?x\.?p\.?)\s*[:\-]?\s*(0?[1-9]|1[0-2])[/\-.](\d{2}|\d{4})",
			RegexOptions.IgnoreCase);
		if (labeled.Success)
		{
			return FormatMonthYear(labeled.Groups[1].Value, labeled.Groups[2].Value);
		}

		Match composite = Regex.Match(
			text,
			@"(0?[1-9]|1[0-2])[/\-.](\d{2}|\d{4})\s*[-–/]\s*(0?[1-9]|1[0-2])[/\-.](\d{2}|\d{4})");
		if (composite.Success)
		{
			return FormatMonthYear(composite.Groups[3].Value, composite.Groups[4].Value);
		}

		Match single = Regex.Match(text, @"\b(0?[1-9]|1[0-2])[/\-.](\d{2}|\d{4})\b");
		if (single.Success)
		{
			return FormatMonthYear(single.Groups[1].Value, single.Groups[2].Value);
		}

		Match iso = Regex.Match(text, @"\b(20\d{2})-(\d{2})(?:-\d{2})?\b");
		if (iso.Success)
		{
			return iso.Groups[2].Value + "/" + iso.Groups[1].Value;
		}

		return string.Empty;
	}

	private static string FormatMonthYear(string monthText, string yearText)
	{
		int month = int.Parse(monthText, System.Globalization.CultureInfo.InvariantCulture);
		int year = int.Parse(yearText, System.Globalization.CultureInfo.InvariantCulture);
		if (yearText.Length <= 2)
		{
			year += 2000;
		}

		return month.ToString("00", System.Globalization.CultureInfo.InvariantCulture)
			+ "/"
			+ year.ToString(System.Globalization.CultureInfo.InvariantCulture);
	}
}
