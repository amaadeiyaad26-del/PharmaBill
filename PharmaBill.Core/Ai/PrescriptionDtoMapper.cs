using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace PharmaBill.Core.Ai;

public static class PrescriptionDtoMapper
{
	public const string GeminiPrompt = "You are reading an Indian medical prescription (possibly handwritten). Return only strict JSON with exactly these fields:\r\ndoctorName, doctorRegistrationNo, patientName, prescriptionDate (YYYY-MM-DD or empty),\r\nmedicines (array of drugName, dosage (for example 1-0-1 or BD), duration (for example 5 days)),\r\nrawText (all the text you can read).\r\nDo not invent unreadable values: use empty strings and omit medicines you cannot read.";

	public static PrescriptionParseResultDto FromGeminiJson(string? json)
	{
		string text = StripFences(json);
		if (string.IsNullOrWhiteSpace(text))
		{
			throw new InvalidDataException("The AI provider returned no prescription data.");
		}
		JsonDocument jsonDocument;
		try
		{
			jsonDocument = JsonDocument.Parse(text);
		}
		catch (JsonException)
		{
			return new PrescriptionParseResultDto
			{
				RawExtractedText = text
			};
		}
		using (jsonDocument)
		{
			JsonElement rootElement = jsonDocument.RootElement;
			if (rootElement.ValueKind != JsonValueKind.Object)
			{
				throw new InvalidDataException("The AI provider returned an unexpected prescription format.");
			}
			List<PrescribedItemDto> list = new List<PrescribedItemDto>();
			if (TryGet(rootElement, "medicines", out var value) && value.ValueKind == JsonValueKind.Array)
			{
				foreach (JsonElement item in value.EnumerateArray())
				{
					if (item.ValueKind == JsonValueKind.Object)
					{
						string text2 = GetString(item, "drugName");
						if (text2.Length != 0)
						{
							list.Add(new PrescribedItemDto
							{
								DrugName = text2,
								Dosage = GetString(item, "dosage"),
								Duration = GetString(item, "duration")
							});
						}
					}
				}
			}
			DateTime? prescriptionDate = null;
			string text3 = GetString(rootElement, "prescriptionDate");
			if (text3.Length > 0)
			{
				DateTime date;
				if (DateTime.TryParseExact(text3, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var result))
				{
					prescriptionDate = result;
				}
				else if (PrescriptionTextParser.TryParseDate(text3, out date))
				{
					prescriptionDate = date;
				}
			}
			return new PrescriptionParseResultDto
			{
				DoctorName = GetString(rootElement, "doctorName"),
				DoctorRegistrationNo = GetString(rootElement, "doctorRegistrationNo"),
				PatientName = GetString(rootElement, "patientName"),
				PrescriptionDate = prescriptionDate,
				DetectedMedicines = list,
				RawExtractedText = GetString(rootElement, "rawText"),
				Source = "Gemini"
			};
		}
	}

	private static string StripFences(string? json)
	{
		string text = (json ?? string.Empty).Trim();
		if (text.StartsWith("```", StringComparison.Ordinal))
		{
			int num = text.IndexOf('\n');
			int num2 = text.LastIndexOf("```", StringComparison.Ordinal);
			if (num >= 0 && num2 > num)
			{
				string text2 = text;
				int num3 = num + 1;
				text = text2.Substring(num3, num2 - num3).Trim();
			}
		}
		return text;
	}

	private static bool TryGet(JsonElement element, string name, out JsonElement value)
	{
		foreach (JsonProperty item in element.EnumerateObject())
		{
			if (string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase))
			{
				value = item.Value;
				return true;
			}
		}
		value = default;
		return false;
	}

	private static string GetString(JsonElement element, string name)
	{
		bool flag = TryGet(element, name, out var value);
		if (flag)
		{
			JsonValueKind valueKind = value.ValueKind;
			bool flag2 = valueKind - 3 <= JsonValueKind.Object;
			flag = flag2;
		}
		if (!flag)
		{
			return string.Empty;
		}
		return ((value.ValueKind == JsonValueKind.String) ? value.GetString() : value.GetRawText())?.Trim() ?? string.Empty;
	}
}
