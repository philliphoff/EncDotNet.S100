using System.Collections.Immutable;
using System.Text.Json;
using EncDotNet.S100.Mcp.Tools.Geometry;

namespace EncDotNet.S100.Mcp;

/// <summary>
/// Parses a <see cref="GeoQuery"/> from the JSON envelope passed
/// across the MCP wire. The wire format wraps each variant in a
/// <c>{ "kind": "...", … }</c> object:
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><description><c>{"kind":"point","latitude":lat,"longitude":lon}</c></description></item>
///   <item><description><c>{"kind":"box","south":s,"west":w,"north":n,"east":e}</c></description></item>
///   <item><description><c>{"kind":"polyline","vertices":[[lat,lon],…],"corridorWidthMeters":w}</c> (<c>corridorWidthMeters</c> optional)</description></item>
///   <item><description><c>{"kind":"polygon","ring":[[lat,lon],…]}</c></description></item>
/// </list>
/// <para>
/// This is the JSON projection of the <see cref="GeoQuery"/>
/// discriminated union; keeping the wire shape close to the in-memory
/// shape avoids per-tool ad-hoc parameter sets and lets new spatial
/// tools reuse one parser.
/// </para>
/// </remarks>
internal static class GeoQueryJsonReader
{
    /// <summary>
    /// Reads a <see cref="GeoQuery"/> from a JSON string. Throws
    /// <see cref="ArgumentException"/> on shape errors so the calling
    /// tool wrapper's <c>internal_error</c> fallback surfaces a
    /// meaningful message — geometry-level validation is done later
    /// by <see cref="GeoQueryValidator"/>.
    /// </summary>
    public static GeoQuery Parse(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("Query must be a JSON object.", nameof(json));
        }

        if (!root.TryGetProperty("kind", out var kindEl) || kindEl.ValueKind != JsonValueKind.String)
        {
            throw new ArgumentException("Query is missing 'kind'.", nameof(json));
        }

        return kindEl.GetString() switch
        {
            "point" => ParsePoint(root),
            "box" => ParseBox(root),
            "polyline" => ParsePolyline(root),
            "polygon" => ParsePolygon(root),
            var k => throw new ArgumentException($"Unknown query kind '{k}'.", nameof(json)),
        };
    }

    private static GeoQuery ParsePoint(JsonElement root)
    {
        var lat = root.GetProperty("latitude").GetDouble();
        var lon = root.GetProperty("longitude").GetDouble();
        return new GeoQuery.Point(new GeoPoint(lat, lon));
    }

    private static GeoQuery ParseBox(JsonElement root)
    {
        var s = root.GetProperty("south").GetDouble();
        var w = root.GetProperty("west").GetDouble();
        var n = root.GetProperty("north").GetDouble();
        var e = root.GetProperty("east").GetDouble();
        return new GeoQuery.Box(new GeoBoundingBox(s, w, n, e));
    }

    private static GeoQuery ParsePolyline(JsonElement root)
    {
        var vertices = ReadVertexArray(root.GetProperty("vertices"));
        double? width = null;
        if (root.TryGetProperty("corridorWidthMeters", out var widthEl)
            && widthEl.ValueKind != JsonValueKind.Null)
        {
            width = widthEl.GetDouble();
        }
        return new GeoQuery.Polyline(new GeoPolyline(vertices, width));
    }

    private static GeoQuery ParsePolygon(JsonElement root)
    {
        var ring = ReadVertexArray(root.GetProperty("ring"));
        return new GeoQuery.Polygon(new GeoPolygon(ring));
    }

    private static ImmutableArray<GeoPoint> ReadVertexArray(JsonElement el)
    {
        if (el.ValueKind != JsonValueKind.Array)
        {
            throw new ArgumentException("Vertex array must be a JSON array of [lat,lon] pairs.");
        }
        var builder = ImmutableArray.CreateBuilder<GeoPoint>(el.GetArrayLength());
        foreach (var item in el.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Array || item.GetArrayLength() != 2)
            {
                throw new ArgumentException("Each vertex must be a [lat,lon] pair.");
            }
            var lat = item[0].GetDouble();
            var lon = item[1].GetDouble();
            builder.Add(new GeoPoint(lat, lon));
        }
        return builder.MoveToImmutable();
    }
}
