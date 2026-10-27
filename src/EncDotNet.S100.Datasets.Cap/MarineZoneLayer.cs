using System.Text.Json;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Features;

namespace EncDotNet.S100.Datasets.Cap;

/// <summary>An NWS marine zone: its UGC code, name, office and outline.</summary>
/// <param name="Code">The zone's UGC code, e.g. <c>"ANZ535"</c>.</param>
/// <param name="Name">The zone's name, e.g. <c>"Tidal Potomac from Key Bridge to Indian Head MD"</c>.</param>
/// <param name="Office">The forecast office that issues for it, e.g. <c>"LWX"</c>; empty when not given.</param>
/// <param name="Surfaces">The zone's outline: one or more surfaces, each with its holes, WGS-84.</param>
public sealed record MarineZone(string Code, string Name, string Office, IReadOnlyList<SurfacePart> Surfaces);

/// <summary>
/// NWS marine zones by UGC code, used to draw a CAP area that names only zone
/// codes (design note <c>docs/design/weather-sources.md</c> §6.1).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Default"/> is the layer bundled with this package: the NWS
/// coastal (<c>mz18mr25</c>) and offshore (<c>oz18mr25</c>) marine zone
/// shapefiles from <c>https://www.weather.gov/gis/MarineZones</c>, 696 zones,
/// simplified once to a compact GeoJSON (Douglas-Peucker at 0.005°, about
/// 500 m, coordinates to four decimals) by <c>tools/cap-zones</c>. The NWS
/// data is a US Government work in the public domain. The simplification
/// is fine for drawing a warning area on a chart; neighbouring zones may
/// overlap or leave slivers of up to a few hundred metres along shared edges.
/// </para>
/// <para>
/// <see cref="Load(Stream)"/> reads a newer or fuller layer in the same form:
/// a GeoJSON feature collection whose features carry the code as <c>id</c>
/// (or a <c>properties.ID</c>, as the NWS shapefile's attributes name it), a
/// <c>name</c> (or <c>NAME</c>), an optional <c>wfo</c> (or <c>WFO</c>), and a
/// <c>Polygon</c> or <c>MultiPolygon</c>.
/// </para>
/// </remarks>
public sealed class MarineZoneLayer
{
    private const string ResourceName = "EncDotNet.S100.Datasets.Cap.MarineZones.geojson";

    private static readonly Lazy<MarineZoneLayer> DefaultLayer = new(LoadDefault);

    private readonly Dictionary<string, MarineZone> _zones;

    private MarineZoneLayer(Dictionary<string, MarineZone> zones, string? source)
    {
        _zones = zones;
        Source = source;
    }

    /// <summary>The bundled NWS coastal and offshore marine zones, read once on first use.</summary>
    public static MarineZoneLayer Default => DefaultLayer.Value;

    /// <summary>An empty layer, which resolves no code.</summary>
    public static MarineZoneLayer Empty { get; } = new(new Dictionary<string, MarineZone>(StringComparer.OrdinalIgnoreCase), null);

    /// <summary>Where the layer came from, as its file says, or <see langword="null"/>.</summary>
    public string? Source { get; }

    /// <summary>How many zones the layer holds.</summary>
    public int Count => _zones.Count;

    /// <summary>The zone codes, in no particular order.</summary>
    public IEnumerable<string> Codes => _zones.Keys;

    /// <summary>The zone with UGC code <paramref name="code"/>, matched ignoring case, or <see langword="null"/>.</summary>
    /// <param name="code">The code, e.g. <c>"ANZ535"</c>.</param>
    public MarineZone? Find(string code)
    {
        ArgumentNullException.ThrowIfNull(code);
        return _zones.GetValueOrDefault(code.Trim());
    }

    /// <summary>Builds a layer from zones, for tests and hosts that keep their own.</summary>
    /// <param name="zones">The zones; a later zone with the same code replaces an earlier one.</param>
    public static MarineZoneLayer Create(IEnumerable<MarineZone> zones)
    {
        ArgumentNullException.ThrowIfNull(zones);
        var map = new Dictionary<string, MarineZone>(StringComparer.OrdinalIgnoreCase);
        foreach (var zone in zones)
            map[zone.Code] = zone;
        return new MarineZoneLayer(map, null);
    }

    /// <summary>Reads a marine zone layer from GeoJSON (see the remarks for its form). The stream is left open.</summary>
    /// <param name="stream">The GeoJSON.</param>
    /// <exception cref="CapFormatException">The stream isn't a GeoJSON feature collection.</exception>
    public static MarineZoneLayer Load(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(stream);
        }
        catch (JsonException ex)
        {
            throw new CapFormatException($"The marine zone layer isn't valid JSON: {ex.Message}", ex);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("features", out var features)
                || features.ValueKind != JsonValueKind.Array)
            {
                throw new CapFormatException("The marine zone layer isn't a GeoJSON feature collection.");
            }

            var zones = new Dictionary<string, MarineZone>(StringComparer.OrdinalIgnoreCase);
            foreach (var feature in features.EnumerateArray())
            {
                if (ReadZone(feature) is { } zone)
                    zones[zone.Code] = zone;
            }

            return new MarineZoneLayer(zones, String(root, "source"));
        }
    }

    private static MarineZoneLayer LoadDefault()
    {
        using var stream = typeof(MarineZoneLayer).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{ResourceName}' is missing.");
        return Load(stream);
    }

    private static MarineZone? ReadZone(JsonElement feature)
    {
        if (feature.ValueKind != JsonValueKind.Object)
            return null;
        feature.TryGetProperty("properties", out var properties);
        var code = String(feature, "id") ?? String(properties, "ID");
        if (code is null || !feature.TryGetProperty("geometry", out var geometry))
            return null;

        var surfaces = new List<SurfacePart>();
        if (geometry.ValueKind == JsonValueKind.Object && geometry.TryGetProperty("coordinates", out var coordinates))
        {
            switch (String(geometry, "type"))
            {
                case "Polygon":
                    AddSurface(surfaces, coordinates);
                    break;
                case "MultiPolygon" when coordinates.ValueKind == JsonValueKind.Array:
                    foreach (var polygon in coordinates.EnumerateArray())
                        AddSurface(surfaces, polygon);
                    break;
            }
        }

        if (surfaces.Count == 0)
            return null;
        return new MarineZone(
            code.ToUpperInvariant(),
            String(properties, "name") ?? String(properties, "NAME") ?? string.Empty,
            String(properties, "wfo") ?? String(properties, "WFO") ?? string.Empty,
            surfaces);
    }

    private static void AddSurface(List<SurfacePart> surfaces, JsonElement polygon)
    {
        if (polygon.ValueKind != JsonValueKind.Array)
            return;
        var rings = new List<IReadOnlyList<GeoPosition>>();
        foreach (var ringElement in polygon.EnumerateArray())
        {
            if (ringElement.ValueKind != JsonValueKind.Array)
                return;
            var ring = new List<GeoPosition>(ringElement.GetArrayLength());
            foreach (var position in ringElement.EnumerateArray())
            {
                if (position.ValueKind == JsonValueKind.Array && position.GetArrayLength() >= 2
                    && position[0].TryGetDouble(out var longitude) && position[1].TryGetDouble(out var latitude))
                {
                    ring.Add(new GeoPosition(latitude, longitude));
                }
            }

            if (CapReader.CloseRing(ring) is { } closed)
                rings.Add(closed);
            else if (rings.Count == 0)
                return;
        }

        if (rings.Count > 0)
            surfaces.Add(new SurfacePart(rings[0], rings.Skip(1).ToArray()));
    }

    private static string? String(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
        && value.GetString() is { Length: > 0 } text
            ? text
            : null;
}
