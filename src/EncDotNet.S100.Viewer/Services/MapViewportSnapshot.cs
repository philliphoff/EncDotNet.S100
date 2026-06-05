namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Immutable lat/lon (EPSG:4326) bounding box of the map's current
/// viewport. Published by <see cref="IMapViewportNotifier"/> on every
/// viewport change so subscribers can reason about the visible area
/// in geographic coordinates without depending on Mapsui types.
/// </summary>
/// <remarks>
/// The viewer projects Mapsui's native EPSG:3857 (Web Mercator)
/// viewport via <c>SphericalMercator.ToLonLat</c> when constructing a
/// snapshot. Latitudes are therefore clamped to roughly
/// <c>±85.0511°</c> by the projection; longitudes can fall outside
/// <c>[-180, +180]</c> when the user pans across the antimeridian
/// (Mapsui doesn't wrap by default), which is intentional — the
/// downstream gate sees a wide span and stays closed.
/// </remarks>
internal sealed record MapViewportSnapshot
{
    /// <summary>South edge of the visible viewport, in decimal
    /// degrees (EPSG:4326).</summary>
    public required double MinLatitude { get; init; }

    /// <summary>West edge of the visible viewport, in decimal
    /// degrees (EPSG:4326).</summary>
    public required double MinLongitude { get; init; }

    /// <summary>North edge of the visible viewport, in decimal
    /// degrees (EPSG:4326).</summary>
    public required double MaxLatitude { get; init; }

    /// <summary>East edge of the visible viewport, in decimal
    /// degrees (EPSG:4326).</summary>
    public required double MaxLongitude { get; init; }

    /// <summary>Latitude span (north - south) in degrees.</summary>
    public double LatitudeSpanDegrees => MaxLatitude - MinLatitude;

    /// <summary>Longitude span (east - west) in degrees.</summary>
    public double LongitudeSpanDegrees => MaxLongitude - MinLongitude;
}
