using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BidMatrix.Application.Tools;

public static class CanonicalJson
{
    public static string Normalize(JsonElement value)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
        {
            WriteValue(writer, value);
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public static string Hash(JsonElement value) => HashNormalized(Normalize(value));

    public static string HashNormalized(string normalizedJson) => Convert.ToHexStringLower(
        SHA256.HashData(Encoding.UTF8.GetBytes(normalizedJson)));

    public static JsonElement ParseNormalized(string normalizedJson) =>
        JsonSerializer.Deserialize<JsonElement>(normalizedJson);

    private static void WriteValue(Utf8JsonWriter writer, JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in value.EnumerateObject().OrderBy(item => item.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteValue(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in value.EnumerateArray())
                {
                    WriteValue(writer, item);
                }
                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(value.GetString());
                break;
            case JsonValueKind.Number:
                WriteNumber(writer, value);
                break;
            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;
            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;
            case JsonValueKind.Null:
                writer.WriteNullValue();
                break;
            default:
                throw new JsonException($"Unsupported JSON value kind {value.ValueKind}.");
        }
    }

    private static void WriteNumber(Utf8JsonWriter writer, JsonElement value)
    {
        if (value.TryGetInt64(out var integer))
        {
            writer.WriteNumberValue(integer);
            return;
        }

        if (value.TryGetDecimal(out var decimalNumber))
        {
            writer.WriteRawValue(decimalNumber.ToString("G29", CultureInfo.InvariantCulture));
            return;
        }

        var doubleNumber = value.GetDouble();
        if (!double.IsFinite(doubleNumber))
        {
            throw new JsonException("Non-finite JSON numbers are not supported.");
        }

        var normalized = doubleNumber.ToString("R", CultureInfo.InvariantCulture)
            .Replace("E+", "e", StringComparison.Ordinal)
            .Replace("E", "e", StringComparison.Ordinal);
        writer.WriteRawValue(normalized);
    }
}
