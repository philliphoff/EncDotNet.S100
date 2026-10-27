using System.Text.Json;
using EncDotNet.S100.DataModel;

namespace EncDotNet.S100.Datasets.Cap;

/// <summary>
/// Reads the NWS GeoJSON form of CAP alerts (<c>api.weather.gov/alerts</c>,
/// <c>application/geo+json</c>): an alert feature, or a feature collection
/// of them. Each feature is one alert with one info and one area; the
/// feature's <c>Polygon</c> or <c>MultiPolygon</c> geometry, in longitude,
/// latitude order, is the area's polygons, and <c>geocode.UGC</c> its zone
/// codes.
/// </summary>
internal static class NwsAlertGeoJson
{
    public static CapDocument Read(ReadOnlySpan<byte> bytes)
    {
        JsonDocument document;
        try
        {
            var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            document = JsonDocument.ParseValue(ref reader);
        }
        catch (JsonException ex)
        {
            throw new CapFormatException($"The file isn't valid JSON: {ex.Message}", ex);
        }

        using (document)
        {
            var root = document.RootElement;
            var type = String(root, "type");
            if (type == "FeatureCollection")
            {
                var alerts = new List<CapAlert>();
                if (root.TryGetProperty("features", out var features) && features.ValueKind == JsonValueKind.Array)
                {
                    foreach (var feature in features.EnumerateArray())
                    {
                        if (ReadFeature(feature) is { } alert)
                            alerts.Add(alert);
                    }
                }

                return new CapDocument(CapFormat.NwsGeoJson, alerts);
            }

            if (type == "Feature" && ReadFeature(root) is { } single)
                return new CapDocument(CapFormat.NwsGeoJson, [single]);

            throw new CapFormatException("The JSON isn't an NWS alert feature or feature collection.");
        }
    }

    private static CapAlert? ReadFeature(JsonElement feature)
    {
        if (feature.ValueKind != JsonValueKind.Object
            || !feature.TryGetProperty("properties", out var p)
            || p.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var identifier = String(p, "id") ?? (String(feature, "id") is { } url ? CapReader.IdentifierFromUrl(url) : null);
        if (string.IsNullOrWhiteSpace(identifier))
            return null;

        var parameters = NamedLists(p, "parameters");
        var area = new CapArea
        {
            Description = String(p, "areaDesc") ?? string.Empty,
            Polygons = feature.TryGetProperty("geometry", out var geometry) ? Polygons(geometry) : [],
            Geocodes = NamedLists(p, "geocode"),
        };

        var info = new CapInfo
        {
            Language = String(p, "language"),
            Categories = String(p, "category") is { } category ? [category] : [],
            Event = String(p, "event") ?? string.Empty,
            ResponseTypes = String(p, "response") is { } response ? [response] : [],
            Urgency = String(p, "urgency") ?? "Unknown",
            Severity = CapReader.ParseSeverity(String(p, "severity")),
            Certainty = String(p, "certainty") ?? "Unknown",
            EventCodes = NamedLists(p, "eventCode"),
            Effective = CapReader.ParseTime(String(p, "effective")),
            Onset = CapReader.ParseTime(String(p, "onset")),
            Expires = CapReader.ParseTime(String(p, "expires")),
            Ends = CapReader.ParseTime(String(p, "ends")) ?? CapReader.EventEndingTime(parameters),
            SenderName = String(p, "senderName"),
            Headline = String(p, "headline"),
            Description = String(p, "description"),
            Instruction = String(p, "instruction"),
            Web = String(p, "web"),
            Parameters = parameters,
            Areas = [area],
        };

        return new CapAlert
        {
            Identifier = identifier,
            Sender = String(p, "sender") ?? string.Empty,
            Sent = CapReader.ParseTime(String(p, "sent")) ?? default,
            Status = String(p, "status") ?? "Actual",
            MessageType = CapReader.ParseMessageType(String(p, "messageType")),
            Scope = String(p, "scope"),
            References = References(p),
            Infos = [info],
        };
    }

    private static IReadOnlyList<string> References(JsonElement properties)
    {
        if (!properties.TryGetProperty("references", out var references) || references.ValueKind != JsonValueKind.Array)
            return [];
        var identifiers = new List<string>();
        foreach (var reference in references.EnumerateArray())
        {
            if (reference.ValueKind == JsonValueKind.Object && String(reference, "identifier") is { } id)
                identifiers.Add(id);
            else if (reference.ValueKind == JsonValueKind.String && reference.GetString() is { Length: > 0 } text)
                identifiers.AddRange(CapReader.ParseReferences(text));
        }

        return identifiers;
    }

    // {"UGC": ["ANZ535", ...], "SAME": [...]} -> one value per entry.
    private static IReadOnlyList<CapValue> NamedLists(JsonElement properties, string name)
    {
        if (!properties.TryGetProperty(name, out var map) || map.ValueKind != JsonValueKind.Object)
            return [];
        var values = new List<CapValue>();
        foreach (var entry in map.EnumerateObject())
        {
            if (entry.Value.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in entry.Value.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } value)
                        values.Add(new CapValue(entry.Name, value));
                }
            }
            else if (entry.Value.ValueKind == JsonValueKind.String && entry.Value.GetString() is { Length: > 0 } value)
            {
                values.Add(new CapValue(entry.Name, value));
            }
        }

        return values;
    }

    // The exterior ring of each polygon; GeoJSON positions are [longitude, latitude].
    private static IReadOnlyList<IReadOnlyList<GeoPosition>> Polygons(JsonElement geometry)
    {
        if (geometry.ValueKind != JsonValueKind.Object || !geometry.TryGetProperty("coordinates", out var coordinates))
            return [];

        var rings = new List<IReadOnlyList<GeoPosition>>();
        void AddPolygon(JsonElement polygon)
        {
            if (polygon.ValueKind != JsonValueKind.Array || polygon.GetArrayLength() == 0)
                return;
            var ring = new List<GeoPosition>();
            foreach (var position in polygon[0].EnumerateArray())
            {
                if (position.ValueKind != JsonValueKind.Array || position.GetArrayLength() < 2
                    || !position[0].TryGetDouble(out var longitude) || !position[1].TryGetDouble(out var latitude))
                {
                    return;
                }

                ring.Add(new GeoPosition(latitude, longitude));
            }

            if (CapReader.CloseRing(ring) is { } closed)
                rings.Add(closed);
        }

        switch (String(geometry, "type"))
        {
            case "Polygon":
                AddPolygon(coordinates);
                break;
            case "MultiPolygon" when coordinates.ValueKind == JsonValueKind.Array:
                foreach (var polygon in coordinates.EnumerateArray())
                    AddPolygon(polygon);
                break;
        }

        return rings;
    }

    private static string? String(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
        && value.GetString() is { } text
        && !string.IsNullOrWhiteSpace(text)
            ? text.Trim()
            : null;
}
