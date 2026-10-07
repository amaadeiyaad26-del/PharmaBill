using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using PharmaBill.Core;
using PharmaBill.Core.Entities;
using PharmaBill.Core.Sync;

namespace PharmaBill.Sync;

public sealed class SyncPayloadSerializer
{
	private static readonly HashSet<string> MoneyPropertyNames = new HashSet<string>(StringComparer.Ordinal)
	{
		"Mrp", "Ptr", "Pts", "SalePrice", "PurchasePrice", "UnitPrice", "UnitRate", "ReferencePrice", "DiscountAmount", "LineTotal",
		"Subtotal", "TaxableAmount", "TaxAmount", "TotalAmount", "PaidAmount", "CgstAmount", "SgstAmount", "IgstAmount", "RoundOff", "CreditAmount",
		"Amount", "Debit", "Credit", "OpeningBalance", "CreditLimit", "StockValue"
	};

	private static readonly HashSet<string> ExcludedPropertyNames = new HashSet<string>(StringComparer.Ordinal) { "PasswordHash", "PasswordSalt", "PinHash", "DeviceKey", "SubscriptionToken", "DocumentPath", "SyncState" };

	private static readonly JsonSerializerOptions JsonOptions = CreateOptions();

	public SyncChangeEnvelope FromChangeLog(ChangeLog change)
	{
		ArgumentNullException.ThrowIfNull(change, "change");
		Type clrType = ResolveEntityType(change.EntityName);
		using JsonDocument jsonDocument = JsonDocument.Parse(change.Payload);
		JsonNode value = ConvertNode(jsonDocument.RootElement, clrType, toWire: true, null);
		string hlcStamp = CanonicalStamp(change.HlcStamp, change.ChangedAtUtc, change.DeviceId);
		DateTime changedAtUtc = EnsureUtc(change.ChangedAtUtc);
		return new SyncChangeEnvelope(change.Id, change.EntityName, change.EntityId, (change.Operation == "SoftDeleted") ? "SoftDeleted" : "Upsert", 1, hlcStamp, change.DeviceId, changedAtUtc, JsonSerializer.SerializeToElement(value, JsonOptions));
	}

	public string SerializeCanonical<T>(T value)
	{
		return Encoding.UTF8.GetString(JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions));
	}

	public string SerializeEntity(EntityBase entity)
	{
		ArgumentNullException.ThrowIfNull(entity, "entity");
		Type type = entity.GetType();
		return ConvertNode(JsonSerializer.SerializeToElement(entity, type), type, toWire: true, null)?.ToJsonString(JsonOptions) ?? "null";
	}

	public EntityBase DeserializeEntity(SyncChangeEnvelope envelope)
	{
		Type type = ResolveEntityType(envelope.Entity);
		return ((EntityBase)JsonSerializer.Deserialize(ConvertNode(envelope.Payload, type, toWire: false, null).ToJsonString(JsonOptions), type, JsonOptions)) ?? throw new InvalidDataException("The " + envelope.Entity + " payload is invalid.");
	}

	public string CanonicalizeStoredPayload(string entityName, string payload)
	{
		Type clrType = ResolveEntityType(entityName);
		using JsonDocument jsonDocument = JsonDocument.Parse(payload);
		return ConvertNode(jsonDocument.RootElement, clrType, toWire: true, null)?.ToJsonString(JsonOptions) ?? "null";
	}

	public static Type ResolveEntityType(string entityName)
	{
		if (string.IsNullOrWhiteSpace(entityName))
		{
			throw new InvalidDataException("Sync entity '" + entityName + "' is not supported.");
		}
		Type type = typeof(EntityBase).Assembly.GetType("PharmaBill.Core.Entities." + entityName, throwOnError: false, ignoreCase: false);
		if ((object)type == null || !typeof(EntityBase).IsAssignableFrom(type) || type.IsAbstract || type == typeof(ChangeLog) || type == typeof(SyncConflict) || type == typeof(DeviceInfo))
		{
			throw new InvalidDataException("Sync entity '" + entityName + "' is not supported.");
		}
		return type;
	}

	private static JsonNode? ConvertNode(JsonElement element, Type? clrType, bool toWire, string? propertyName)
	{
		JsonValueKind valueKind = element.ValueKind;
		if ((valueKind == JsonValueKind.Undefined || valueKind == JsonValueKind.Null) ? true : false)
		{
			return null;
		}
		clrType = (((object)clrType == null) ? null : Nullable.GetUnderlyingType(clrType)) ?? clrType;
		if (element.ValueKind == JsonValueKind.Object)
		{
			JsonObject jsonObject = new JsonObject();
			{
				foreach (JsonProperty property in element.EnumerateObject().OrderBy((JsonProperty item) => item.Name, StringComparer.Ordinal))
				{
					if (toWire && ExcludedPropertyNames.Contains(property.Name))
					{
						continue;
					}
					PropertyInfo propertyInfo = clrType?.GetProperties(BindingFlags.Instance | BindingFlags.Public).FirstOrDefault((PropertyInfo candidate) => candidate.Name == property.Name || JsonNamingPolicy.CamelCase.ConvertName(candidate.Name) == property.Name);
					if ((object)propertyInfo != null && (!toWire || (!ExcludedPropertyNames.Contains(propertyInfo.Name) && (!(clrType == typeof(Batch)) || !(propertyInfo.Name == "Quantity")))))
					{
						string propertyName2 = JsonNamingPolicy.CamelCase.ConvertName(propertyInfo.Name);
						JsonNode jsonNode = ConvertNode(property.Value, propertyInfo.PropertyType, toWire, propertyInfo.Name);
						if (toWire && propertyInfo.Name == "Notes" && jsonNode is JsonValue jsonValue && jsonValue.TryGetValue<string>(out string value) && value != null && value.StartsWith("DocumentPath=", StringComparison.Ordinal))
						{
							jsonNode = null;
						}
						jsonObject[propertyName2] = jsonNode;
					}
				}
				return jsonObject;
			}
		}
		if (element.ValueKind == JsonValueKind.Array)
		{
			Type clrType2;
			if ((object)clrType != null && clrType.IsArray)
			{
				clrType2 = clrType.GetElementType();
			}
			else
			{
				clrType2 = (((object)clrType != null && clrType.IsGenericType) ? clrType.GetGenericArguments().FirstOrDefault() : null);
			}
			JsonArray jsonArray = new JsonArray();
			{
				foreach (JsonElement item in element.EnumerateArray())
				{
					jsonArray.Add(ConvertNode(item, clrType2, toWire, propertyName));
				}
				return jsonArray;
			}
		}
		if ((object)clrType != null && clrType.IsEnum)
		{
			if (toWire)
			{
				object value2 = ((element.ValueKind == JsonValueKind.String) ? Enum.Parse(clrType, element.GetString(), ignoreCase: false) : Enum.ToObject(clrType, element.GetInt32()));
				return JsonValue.Create(Enum.GetName(clrType, value2) ?? throw new InvalidDataException("Unknown " + clrType.Name + " enum value."));
			}
			string value3 = element.GetString() ?? throw new InvalidDataException("Expected " + clrType.Name + " enum string.");
			if (!Enum.TryParse(clrType, value3, ignoreCase: false, out object result) || !Enum.IsDefined(clrType, result))
			{
				throw new InvalidDataException($"Unknown {clrType.Name} enum value '{value3}'.");
			}
			return JsonValue.Create(value3);
		}
		if (clrType == typeof(decimal) && MoneyPropertyNames.Contains(propertyName ?? string.Empty))
		{
			if (toWire)
			{
				return JsonValue.Create(Money.ToPaise(element.GetDecimal()));
			}
			return JsonValue.Create(Money.FromPaise(element.GetInt64()));
		}
		if (clrType == typeof(DateTime))
		{
			if (element.ValueKind != JsonValueKind.String)
			{
				throw new InvalidDataException("Sync timestamps must be ISO-8601 strings.");
			}
			DateTime value4 = DateTime.Parse(element.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
			if (toWire)
			{
				return JsonValue.Create(EnsureUtc(value4).ToString("O", CultureInfo.InvariantCulture));
			}
		}
		if (clrType == typeof(Guid) && element.ValueKind == JsonValueKind.String)
		{
			return JsonValue.Create(Guid.Parse(element.GetString()).ToString("D").ToLowerInvariant());
		}
		if (element.ValueKind == JsonValueKind.String)
		{
			return JsonValue.Create(element.GetString());
		}
		if (element.ValueKind == JsonValueKind.Number)
		{
			return JsonNode.Parse(element.GetRawText());
		}
		valueKind = element.ValueKind;
		if (valueKind - 5 <= JsonValueKind.Object)
		{
			return JsonValue.Create(element.GetBoolean());
		}
		return JsonNode.Parse(element.GetRawText());
	}

	private static DateTime EnsureUtc(DateTime value)
	{
		return value.Kind switch
		{
			DateTimeKind.Utc => value, 
			DateTimeKind.Local => value.ToUniversalTime(), 
			_ => DateTime.SpecifyKind(value, DateTimeKind.Utc), 
		};
	}

	private static string CanonicalStamp(string stamp, DateTime atUtc, Guid deviceId)
	{
		try
		{
			HybridLogicalClockState.Parse(stamp);
			return stamp;
		}
		catch (FormatException)
		{
			long val = new DateTimeOffset(EnsureUtc(atUtc)).ToUnixTimeMilliseconds();
			return new HlcValue(Math.Max(0L, val), 0L, deviceId).ToString();
		}
	}

	private static JsonSerializerOptions CreateOptions()
	{
		return new JsonSerializerOptions
		{
			PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
			WriteIndented = false,
			Converters = { (JsonConverter)new JsonStringEnumConverter() }
		};
	}
}
