using System.Text.Json;
using System.Text.Json.Serialization;

namespace EncDotNet.S100.Mcp.Tools.Catalog;

/// <summary>
/// Serialises <see cref="DatasetId"/> as a bare JSON string so that the
/// identifier round-trips symmetrically: every tool accepts a plain
/// string <c>datasetId</c> argument, and every tool result therefore
/// emits the same plain string rather than a wrapped
/// <c>{"value":"…"}</c> object.
/// </summary>
/// <remarks>
/// On read the converter is lenient — it accepts either a bare string
/// (the canonical form) or the legacy <c>{"value":"…"}</c> object — so
/// that any caller or persisted payload using the older wrapped shape
/// continues to bind. On write it always emits the bare string.
/// </remarks>
public sealed class DatasetIdJsonConverter : JsonConverter<DatasetId>
{
    /// <inheritdoc />
    public override DatasetId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
                return new DatasetId(reader.GetString()!);

            case JsonTokenType.StartObject:
            {
                string? value = null;
                while (reader.Read())
                {
                    if (reader.TokenType == JsonTokenType.EndObject)
                    {
                        break;
                    }
                    if (reader.TokenType != JsonTokenType.PropertyName)
                    {
                        continue;
                    }
                    var name = reader.GetString();
                    reader.Read();
                    if (string.Equals(name, "value", StringComparison.OrdinalIgnoreCase))
                    {
                        value = reader.GetString();
                    }
                    else
                    {
                        reader.Skip();
                    }
                }
                if (string.IsNullOrEmpty(value))
                {
                    throw new JsonException("Expected a non-empty 'value' property for DatasetId.");
                }
                return new DatasetId(value);
            }

            default:
                throw new JsonException(
                    $"Cannot convert {reader.TokenType} to DatasetId; expected a string or a {{\"value\":\"…\"}} object.");
        }
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, DatasetId value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.Value);

    /// <inheritdoc />
    public override DatasetId ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => new(reader.GetString()!);

    /// <inheritdoc />
    public override void WriteAsPropertyName(Utf8JsonWriter writer, DatasetId value, JsonSerializerOptions options)
        => writer.WritePropertyName(value.Value);
}
