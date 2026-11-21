using EncDotNet.S100.DataModel;

namespace EncDotNet.S100.Viewer.Services.Tides;

/// <summary>
/// Thins predicted stations on the map (tide predictions design D6): a
/// station is kept when no kept station is within a screen distance, taken
/// in a stable order so the choice doesn't jump while panning.
/// </summary>
internal static class StationThinning
{
    /// <summary>Thins <paramref name="stations"/>, already in the order to keep them.</summary>
    /// <typeparam name="T">The station type.</typeparam>
    /// <param name="stations">The stations, in priority order.</param>
    /// <param name="position">A station's position.</param>
    /// <param name="distance">The distance, in Web Mercator metres.</param>
    /// <returns>The kept stations.</returns>
    public static IReadOnlyList<T> Thin<T>(IEnumerable<T> stations, Func<T, GeoPosition> position, double distance)
    {
        ArgumentNullException.ThrowIfNull(stations);
        ArgumentNullException.ThrowIfNull(position);
        var kept = new List<(T Station, double X, double Y)>();
        var squared = distance * distance;
        foreach (var station in stations)
        {
            var (x, y) = Mercator(position(station));
            var clear = true;
            foreach (var other in kept)
            {
                var dx = other.X - x;
                var dy = other.Y - y;
                if ((dx * dx) + (dy * dy) < squared)
                {
                    clear = false;
                    break;
                }
            }

            if (clear)
            {
                kept.Add((station, x, y));
            }
        }

        return [.. kept.Select(k => k.Station)];
    }

    /// <summary>Whether a view is at 1:500,000 or larger, where stations are drawn (D4).</summary>
    /// <param name="view">The view.</param>
    public static bool IsDrawable(MapViewportSnapshot view)
    {
        ArgumentNullException.ThrowIfNull(view);
        var latitude = (view.MinLatitude + view.MaxLatitude) / 2;
        return view.MercatorResolution > 0
            && MapScaleFormatter.ResolutionToScaleDenominator(view.MercatorResolution, latitude) is { } scale
            && scale <= TideStationPortrayal.StationMaxScaleDenominator;
    }

    /// <summary>The view, padded by a tenth each way, as a box to find stations in.</summary>
    /// <param name="view">The view.</param>
    public static Pipelines.BoundingBox Box(MapViewportSnapshot view)
    {
        ArgumentNullException.ThrowIfNull(view);
        var padLat = (view.MaxLatitude - view.MinLatitude) * 0.1;
        var padLon = (view.MaxLongitude - view.MinLongitude) * 0.1;
        return new Pipelines.BoundingBox(
            southLatitude: Math.Max(-90, view.MinLatitude - padLat),
            westLongitude: view.MinLongitude - padLon,
            northLatitude: Math.Min(90, view.MaxLatitude + padLat),
            eastLongitude: view.MaxLongitude + padLon);
    }

    private static (double X, double Y) Mercator(GeoPosition position)
    {
        const double radius = 6378137.0;
        var x = radius * position.Longitude * Math.PI / 180;
        var lat = Math.Clamp(position.Latitude, -85.05112878, 85.05112878) * Math.PI / 180;
        var y = radius * Math.Log(Math.Tan((Math.PI / 4) + (lat / 2)));
        return (x, y);
    }
}
