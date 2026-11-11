using System.Collections;
using System.Text.Json;

namespace EncDotNet.S100.Datasets.Pipelines.Spec;

/// <summary>
/// Turns the describers' attribute payloads — trees of
/// <see cref="Dictionary{TKey, TValue}"/>, lists and primitive values — into a
/// <see cref="JsonElement"/> without reflection (issue #764).
/// </summary>
/// <remarks>
/// The output matches what <see cref="JsonSerializer"/> writes for the same
/// tree with default options: keys as given, in insertion order; numbers,
/// strings, booleans and dates written by <see cref="Utf8JsonWriter"/>; enums
/// as their numeric value. A value of any other type throws
/// <see cref="NotSupportedException"/>, so a payload cannot quietly grow a
/// type that would need reflection.
/// </remarks>
internal static class SpecPayloadJson
{
    /// <summary>Writes <paramref name="payload"/> and parses it back as a detached element.</summary>
    public static JsonElement ToElement(object? payload)
    {
        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            WriteValue(writer, payload);
        }

        using var document = JsonDocument.Parse(buffer.WrittenMemory);
        return document.RootElement.Clone();
    }

    private static void WriteValue(Utf8JsonWriter writer, object? value)
    {
        switch (value)
        {
            case null: writer.WriteNullValue(); break;
            case string s: writer.WriteStringValue(s); break;
            case bool b: writer.WriteBooleanValue(b); break;
            case byte n: writer.WriteNumberValue(n); break;
            case sbyte n: writer.WriteNumberValue(n); break;
            case short n: writer.WriteNumberValue(n); break;
            case ushort n: writer.WriteNumberValue(n); break;
            case int n: writer.WriteNumberValue(n); break;
            case uint n: writer.WriteNumberValue(n); break;
            case long n: writer.WriteNumberValue(n); break;
            case ulong n: writer.WriteNumberValue(n); break;
            case float n: writer.WriteNumberValue(n); break;
            case double n: writer.WriteNumberValue(n); break;
            case decimal n: writer.WriteNumberValue(n); break;
            case DateTime t: writer.WriteStringValue(t); break;
            case DateTimeOffset t: writer.WriteStringValue(t); break;
            case Enum e:
                if (Convert.GetTypeCode(e) is TypeCode.UInt64)
                    writer.WriteNumberValue(Convert.ToUInt64(e, System.Globalization.CultureInfo.InvariantCulture));
                else
                    writer.WriteNumberValue(Convert.ToInt64(e, System.Globalization.CultureInfo.InvariantCulture));
                break;
            case IEnumerable<KeyValuePair<string, object?>> map:
                writer.WriteStartObject();
                foreach (var (key, item) in map)
                {
                    writer.WritePropertyName(key);
                    WriteValue(writer, item);
                }
                writer.WriteEndObject();
                break;
            case IEnumerable<KeyValuePair<string, string>> map:
                writer.WriteStartObject();
                foreach (var (key, item) in map)
                    writer.WriteString(key, item);
                writer.WriteEndObject();
                break;
            case IEnumerable items:
                writer.WriteStartArray();
                foreach (var item in items)
                    WriteValue(writer, item);
                writer.WriteEndArray();
                break;
            default:
                throw new NotSupportedException(
                    $"Feature payloads cannot contain a value of type '{value.GetType()}'.");
        }
    }
}
