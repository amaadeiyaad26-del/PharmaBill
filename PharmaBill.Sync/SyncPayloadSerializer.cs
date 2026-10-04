using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using PharmaBill.Core;
using PharmaBill.Core.Entities;
using PharmaBill.Core.Sync;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Sync;

public sealed class SyncPayloadSerializer
{
    private static readonly HashSet<string> MoneyPropertyNames = new(StringComparer.Ordinal)
    {
        "Mrp", "Ptr", "Pts", "SalePrice", "PurchasePrice", "UnitPrice", "UnitRate",
        "ReferencePrice", "DiscountAmount", "LineTotal", "Subtotal", "TaxableAmount",
        "TaxAmount", "TotalAmount", "PaidAmount", "CgstAmount", "SgstAmount", "IgstAmount",
        "RoundOff", "CreditAmount", "Amount", "Debit", "Credit", "OpeningBalance",
        "CreditLimit", "StockValue"
    };

    private static readonly HashSet<string> ExcludedPropertyNames = new(StringComparer.Ordinal)
    {
        "PasswordHash", "PasswordSalt", "PinHash", "DeviceKey", "SubscriptionToken",
        "DocumentPath", "SyncState"
    };

    private static readonly JsonSerializerOptions JsonOptions = CreateOptions();

    public SyncChangeEnvelope FromChangeLog(ChangeLog change)
    {
        ArgumentNullException.ThrowIfNull(change);
        var clrType = ResolveEntityType(change.EntityName);
        using var payloadDocument = JsonDocument.Parse(change.Payload);
        var payloadNode = ConvertNode(payloadDocument.RootElement, clrType, toWire: true, null);
        var hlcStamp = CanonicalStamp(change.HlcStamp, change.ChangedAtUtc, change.DeviceId);
        var changedAt = EnsureUtc(change.ChangedAtUtc);
        return new SyncChangeEnvelope(
            change.Id,
            change.EntityName,
            change.EntityId,
            change.Operation == "SoftDeleted" ? "SoftDeleted" : "Upsert",
            1,
            hlcStamp,
            change.DeviceId,
            changedAt,
            JsonSerializer.SerializeToElement(payloadNode, JsonOptions));
    }

    public string SerializeCanonical<T>(T value) =>
        Encoding.UTF8.GetString(JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions));

    public string SerializeEntity(EntityBase entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        var type = entity.GetType();
        var raw = JsonSerializer.SerializeToElement(entity, type);
        var node = ConvertNode(raw, type, toWire: true, null);
        return node?.ToJsonString(JsonOptions) ?? "null";
    }

    public EntityBase DeserializeEntity(SyncChangeEnvelope envelope)
    {
        var type = ResolveEntityType(envelope.Entity);
        var node = ConvertNode(envelope.Payload, type, toWire: false, null);
        return (EntityBase?)JsonSerializer.Deserialize(node!.ToJsonString(JsonOptions), type, JsonOptions)
            ?? throw new InvalidDataException($"The {envelope.Entity} payload is invalid.");
    }

    public string CanonicalizeStoredPayload(string entityName, string payload)
    {
        var type = ResolveEntityType(entityName);
        using var document = JsonDocument.Parse(payload);
        var node = ConvertNode(document.RootElement, type, toWire: true, null);
        return node?.ToJsonString(JsonOptions) ?? "null";
    }

    public static Type ResolveEntityType(string entityName)
    {
        if (string.IsNullOrWhiteSpace(entityName))
        {
            throw new InvalidDataException($"Sync entity '{entityName}' is not supported.");
        }

        var type = typeof(EntityBase).Assembly.GetType($"PharmaBill.Core.Entities.{entityName}", false, false);
        if (type is null || !typeof(EntityBase).IsAssignableFrom(type) || type.IsAbstract ||
            type == typeof(ChangeLog) || type == typeof(SyncConflict) || type == typeof(DeviceInfo))
        {
            throw new InvalidDataException($"Sync entity '{entityName}' is not supported.");
        }

        return type;
    }

    private static JsonNode? ConvertNode(JsonElement element, Type? clrType, bool toWire, string? propertyName)
    {
        if (element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        var nullableType = clrType is null ? null : Nullable.GetUnderlyingType(clrType);
        clrType = nullableType ?? clrType;

        if (element.ValueKind == JsonValueKind.Object)
        {
            var result = new JsonObject();
            foreach (var property in element.EnumerateObject().OrderBy(item => item.Name, StringComparer.Ordinal))
            {
                if (toWire && ExcludedPropertyNames.Contains(property.Name))
                {
                    continue;
                }

                var propertyInfo = clrType?.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                    .FirstOrDefault(candidate =>
                        candidate.Name == property.Name ||
                        JsonNamingPolicy.CamelCase.ConvertName(candidate.Name) == property.Name);
                if (propertyInfo is null ||
                    (toWire && (ExcludedPropertyNames.Contains(propertyInfo.Name) ||
                                (clrType == typeof(Batch) && propertyInfo.Name == nameof(Batch.Quantity)))))
                {
                    continue;
                }

                var outputName = JsonNamingPolicy.CamelCase.ConvertName(propertyInfo.Name);
                var converted = ConvertNode(property.Value, propertyInfo.PropertyType, toWire, propertyInfo.Name);
                if (toWire && propertyInfo.Name == "Notes" &&
                    converted is JsonValue notesValue &&
                    notesValue.TryGetValue<string>(out var notes) &&
                    notes?.StartsWith("DocumentPath=", StringComparison.Ordinal) == true)
                {
                    converted = null;
                }

                result[outputName] = converted;
            }

            return result;
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            var itemType = clrType?.IsArray == true
                ? clrType.GetElementType()
                : clrType?.IsGenericType == true ? clrType.GetGenericArguments().FirstOrDefault() : null;
            var array = new JsonArray();
            foreach (var item in element.EnumerateArray())
            {
                array.Add(ConvertNode(item, itemType, toWire, propertyName));
            }
            return array;
        }

        if (clrType?.IsEnum == true)
        {
            if (toWire)
            {
                var numeric = element.ValueKind == JsonValueKind.String
                    ? Enum.Parse(clrType, element.GetString()!, ignoreCase: false)
                    : Enum.ToObject(clrType, element.GetInt32());
                return JsonValue.Create(Enum.GetName(clrType, numeric)
                    ?? throw new InvalidDataException($"Unknown {clrType.Name} enum value."));
            }

            var enumName = element.GetString()
                ?? throw new InvalidDataException($"Expected {clrType.Name} enum string.");
            if (!Enum.TryParse(clrType, enumName, ignoreCase: false, out var parsed) ||
                !Enum.IsDefined(clrType, parsed!))
            {
                throw new InvalidDataException($"Unknown {clrType.Name} enum value '{enumName}'.");
            }

            return JsonValue.Create(enumName);
        }

        if (clrType == typeof(decimal) && MoneyPropertyNames.Contains(propertyName ?? string.Empty))
        {
            if (toWire)
            {
                var amount = element.GetDecimal();
                return JsonValue.Create(Money.ToPaise(amount));
            }

            var paise = element.GetInt64();
            return JsonValue.Create(Money.FromPaise(paise));
        }

        if (clrType == typeof(DateTime))
        {
            var parsed = element.ValueKind == JsonValueKind.String
                ? DateTime.Parse(element.GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
                : throw new InvalidDataException("Sync timestamps must be ISO-8601 strings.");
            if (toWire)
            {
                return JsonValue.Create(EnsureUtc(parsed).ToString("O", CultureInfo.InvariantCulture));
            }
        }

        if (clrType == typeof(Guid) && element.ValueKind == JsonValueKind.String)
        {
            return JsonValue.Create(Guid.Parse(element.GetString()!).ToString("D").ToLowerInvariant());
        }

        if (element.ValueKind == JsonValueKind.String)
        {
            return JsonValue.Create(element.GetString());
        }

        if (element.ValueKind == JsonValueKind.Number)
        {
            return JsonNode.Parse(element.GetRawText());
        }

        if (element.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return JsonValue.Create(element.GetBoolean());
        }

        return JsonNode.Parse(element.GetRawText());
    }

    private static DateTime EnsureUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    private static string CanonicalStamp(string stamp, DateTime atUtc, Guid deviceId)
    {
        try
        {
            _ = HybridLogicalClockState.Parse(stamp);
            return stamp;
        }
        catch (FormatException)
        {
            var milliseconds = new DateTimeOffset(EnsureUtc(atUtc)).ToUnixTimeMilliseconds();
            return new HlcValue(Math.Max(0, milliseconds), 0, deviceId).ToString();
        }
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
