namespace EncDotNet.S100.Datasets.Nis;

/// <summary>A WGS 84 bounding box in decimal degrees.</summary>
/// <param name="West">The western longitude.</param>
/// <param name="South">The southern latitude.</param>
/// <param name="East">The eastern longitude.</param>
/// <param name="North">The northern latitude.</param>
public readonly record struct NisBounds(double West, double South, double East, double North)
{
    /// <summary>Whether a position lies inside or on the box.</summary>
    /// <param name="position">The position.</param>
    /// <returns><see langword="true"/> when it does.</returns>
    public bool Contains(NisPosition position)
        => position.Longitude >= West && position.Longitude <= East
            && position.Latitude >= South && position.Latitude <= North;

    /// <summary>Whether the extent of a geometry overlaps the box.</summary>
    /// <param name="geometry">The geometry.</param>
    /// <returns><see langword="true"/> when it does.</returns>
    public bool Intersects(NisGeometry geometry)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        double west = double.MaxValue, south = double.MaxValue, east = double.MinValue, north = double.MinValue;
        foreach (var part in geometry.Parts)
        {
            foreach (var p in part)
            {
                west = Math.Min(west, p.Longitude);
                east = Math.Max(east, p.Longitude);
                south = Math.Min(south, p.Latitude);
                north = Math.Max(north, p.Latitude);
            }
        }
        return west <= East && east >= West && south <= North && north >= South;
    }
}
