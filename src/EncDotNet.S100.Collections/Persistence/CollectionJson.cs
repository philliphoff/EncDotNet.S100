using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using EncDotNet.S100.Collections.Feeds;
using EncDotNet.S100.DataModel;

namespace EncDotNet.S100.Collections.Persistence;

/// <summary>
/// The persisted set of collection definitions (the viewer's
/// <c>collections.json</c>). Holds only references to sources; indexes are
/// stored separately.
/// </summary>
/// <param name="Version">The document format version.</param>
/// <param name="Collections">The collections, in display order.</param>
public sealed record CollectionStoreDocument(int Version, IReadOnlyList<DatasetCollection> Collections)
{
    /// <summary>The format version this library writes.</summary>
    public const int CurrentVersion = 1;

    /// <summary>An empty document at the current version.</summary>
    public static CollectionStoreDocument Empty { get; } = new(CurrentVersion, []);
}

/// <summary>
/// JSON (de)serialization of collection definitions and source indexes.
/// </summary>
/// <remarks>
/// <para>
/// Collection definitions are small, user-meaningful, and written indented.
/// Source indexes can be large (thousands of coverage polygons), so they are
/// written compactly and gzip-compressed, with each vertex encoded as a
/// <c>[latitude, longitude]</c> pair.
/// </para>
/// <para>
/// Hosts own file placement and atomic replacement; this class only converts
/// to and from streams and strings.
/// </para>
/// </remarks>
public static class CollectionJson
{
    // Source-generated metadata bound to each set of options, so nothing is
    // discovered by reflection at run time (issue #764).
    internal static CollectionJsonContext Store { get; } = new(CreateOptions(indented: true));

    internal static CollectionJsonContext Index { get; } = new(CreateOptions(indented: false));

    /// <summary>Options for collection definitions (indented, camelCase).</summary>
    public static JsonSerializerOptions StoreOptions => Store.Options;

    /// <summary>Options for source indexes (compact, camelCase).</summary>
    public static JsonSerializerOptions IndexOptions => Index.Options;

    /// <summary>Serializes a collection store document.</summary>
    public static string SerializeStore(CollectionStoreDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return JsonSerializer.Serialize(document, Store.CollectionStoreDocument);
    }

    /// <summary>
    /// Deserializes a collection store document.
    /// </summary>
    /// <exception cref="JsonException">The JSON is malformed.</exception>
    /// <exception cref="NotSupportedException">The document is from a newer, unknown format version.</exception>
    public static CollectionStoreDocument DeserializeStore(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        var document = JsonSerializer.Deserialize(json, Store.CollectionStoreDocument)
            ?? CollectionStoreDocument.Empty;
        if (document.Version > CollectionStoreDocument.CurrentVersion)
        {
            throw new NotSupportedException(
                $"Collection store version {document.Version} is newer than supported version {CollectionStoreDocument.CurrentVersion}.");
        }

        return document with { Collections = document.Collections ?? [] };
    }

    /// <summary>Writes a gzip-compressed source index to <paramref name="stream"/>.</summary>
    public static void WriteIndex(Stream stream, SourceIndex index)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(index);

        using var gzip = new GZipStream(stream, CompressionLevel.Optimal, leaveOpen: true);
        JsonSerializer.Serialize(gzip, index, Index.SourceIndex);
    }

    /// <summary>Reads a gzip-compressed source index from <paramref name="stream"/>.</summary>
    /// <exception cref="JsonException">The content is malformed.</exception>
    /// <exception cref="InvalidDataException">The content is not gzip-compressed.</exception>
    public static SourceIndex ReadIndex(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        using var gzip = new GZipStream(stream, CompressionMode.Decompress, leaveOpen: true);
        return JsonSerializer.Deserialize(gzip, Index.SourceIndex)
            ?? throw new JsonException("Source index is empty.");
    }

    private static JsonSerializerOptions CreateOptions(bool indented)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = indented,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        options.Converters.Add(new GeoPositionConverter());
        return options;
    }

    /// <summary>Encodes a <see cref="GeoPosition"/> as a <c>[latitude, longitude]</c> array.</summary>
    private sealed class GeoPositionConverter : JsonConverter<GeoPosition>
    {
        public override GeoPosition Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartArray)
                throw new JsonException("Expected a [latitude, longitude] array.");

            reader.Read();
            var latitude = reader.GetDouble();
            reader.Read();
            var longitude = reader.GetDouble();
            reader.Read();
            if (reader.TokenType != JsonTokenType.EndArray)
                throw new JsonException("Expected a [latitude, longitude] array.");

            return new GeoPosition(latitude, longitude);
        }

        public override void Write(Utf8JsonWriter writer, GeoPosition value, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            writer.WriteNumberValue(value.Latitude);
            writer.WriteNumberValue(value.Longitude);
            writer.WriteEndArray();
        }
    }
}

/// <summary>Source-generated JSON metadata for the types <see cref="CollectionJson"/> reads and writes.</summary>
[JsonSerializable(typeof(CollectionStoreDocument))]
[JsonSerializable(typeof(SourceIndex))]
[JsonSerializable(typeof(S100FeedDocument))]
[JsonSerializable(typeof(ItemLocation))]
[JsonSerializable(typeof(GeoPolygon))]
internal sealed partial class CollectionJsonContext : JsonSerializerContext;
