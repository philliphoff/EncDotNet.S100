namespace EncDotNet.S100.Datasets.Pipelines.Portrayal;

/// <summary>
/// A Mapsui-free geographic bounding box. Carried across the
/// portrayal-output seam so the Mapsui renderer can derive an
/// <c>MRect</c> extent (via Spherical Mercator) without the
/// processor referencing any Mapsui type.
/// </summary>
/// <param name="MinLongitude">Western longitude bound (degrees).</param>
/// <param name="MinLatitude">Southern latitude bound (degrees).</param>
/// <param name="MaxLongitude">Eastern longitude bound (degrees).</param>
/// <param name="MaxLatitude">Northern latitude bound (degrees).</param>
public readonly record struct GeographicBounds(
    double MinLongitude,
    double MinLatitude,
    double MaxLongitude,
    double MaxLatitude);

/// <summary>
/// A Mapsui-free EPSG:3857 (Web Mercator) bounding box, expressed in
/// projected metres. Used when a processor has already projected its
/// geometry to Web Mercator (e.g. station-series glyph layers) and the
/// Mapsui renderer needs the extent verbatim.
/// </summary>
/// <param name="MinX">Minimum easting (metres).</param>
/// <param name="MinY">Minimum northing (metres).</param>
/// <param name="MaxX">Maximum easting (metres).</param>
/// <param name="MaxY">Maximum northing (metres).</param>
public readonly record struct MercatorBounds(
    double MinX,
    double MinY,
    double MaxX,
    double MaxY);
