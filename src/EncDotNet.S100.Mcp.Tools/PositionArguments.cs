using System.Globalization;
using System.Text.Json;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines.Query;

namespace EncDotNet.S100.Mcp.Tools;

/// <summary>
/// The one way the MCP tools take a position (#1628): <c>latitude</c> and
/// <c>longitude</c> in decimal degrees, WGS-84. Tools that took
/// <c>lat</c>/<c>lon</c> (or <c>centerLat</c>/<c>centerLon</c>) still accept
/// them as aliases, and a position inside a value may be an object with
/// either pair of names or a <c>[lat, lon]</c> pair.
/// </summary>
public static class PositionArguments
{
    /// <summary>
    /// Resolves a position given as <c>latitude</c>/<c>longitude</c> or as
    /// the alias pair, such as <c>lat</c>/<c>lon</c>.
    /// </summary>
    /// <param name="latitude">The latitude, by its canonical name.</param>
    /// <param name="longitude">The longitude, by its canonical name.</param>
    /// <param name="latitudeAlias">The latitude, by its alias.</param>
    /// <param name="longitudeAlias">The longitude, by its alias.</param>
    /// <param name="names">The canonical and alias names, for messages, such as <c>("latitude", "longitude", "lat", "lon")</c>.</param>
    /// <param name="position">The position, or <c>null</c> when neither pair is given.</param>
    /// <returns>An error when the names disagree or a pair is half given; otherwise <c>null</c>.</returns>
    public static InvalidArgument? Resolve(
        double? latitude,
        double? longitude,
        double? latitudeAlias,
        double? longitudeAlias,
        (string Latitude, string Longitude, string LatitudeAlias, string LongitudeAlias) names,
        out GeoPosition? position)
    {
        position = null;
        if (Conflict(latitude, latitudeAlias))
        {
            return new InvalidArgument(names.Latitude, $"{names.Latitude} and {names.LatitudeAlias} differ; give one");
        }

        if (Conflict(longitude, longitudeAlias))
        {
            return new InvalidArgument(names.Longitude, $"{names.Longitude} and {names.LongitudeAlias} differ; give one");
        }

        var lat = latitude ?? latitudeAlias;
        var lon = longitude ?? longitudeAlias;
        if (lat is null && lon is null)
        {
            return null;
        }

        if (lat is null || lon is null)
        {
            return new InvalidArgument(lat is null ? names.Latitude : names.Longitude, $"give both {names.Latitude} and {names.Longitude}");
        }

        position = new GeoPosition(lat.Value, lon.Value);
        return null;
    }

    /// <summary>
    /// Reads a position from a JSON value: <c>{"latitude":…,"longitude":…}</c>,
    /// <c>{"lat":…,"lon":…}</c> or a <c>[lat, lon]</c> pair.
    /// </summary>
    /// <param name="element">The value.</param>
    /// <param name="latitude">The latitude.</param>
    /// <param name="longitude">The longitude.</param>
    /// <returns><c>true</c> when the value is a position of one of those shapes with finite numbers.</returns>
    public static bool TryRead(JsonElement element, out double latitude, out double longitude)
    {
        latitude = longitude = double.NaN;
        switch (element.ValueKind)
        {
            case JsonValueKind.Array when element.GetArrayLength() == 2:
                return Number(element[0], out latitude) && Number(element[1], out longitude);
            case JsonValueKind.Object:
                return (Property(element, "latitude", out latitude) || Property(element, "lat", out latitude))
                    && (Property(element, "longitude", out longitude) || Property(element, "lon", out longitude));
            default:
                return false;
        }
    }

    private static bool Conflict(double? a, double? b) => a is { } x && b is { } y && x != y;

    private static bool Property(JsonElement element, string name, out double value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return Number(property.Value, out value);
            }
        }

        value = double.NaN;
        return false;
    }

    private static bool Number(JsonElement element, out double value)
    {
        value = double.NaN;
        return element.ValueKind switch
        {
            JsonValueKind.Number => element.TryGetDouble(out value) && double.IsFinite(value),
            JsonValueKind.String => double.TryParse(element.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value),
            _ => false,
        };
    }
}
