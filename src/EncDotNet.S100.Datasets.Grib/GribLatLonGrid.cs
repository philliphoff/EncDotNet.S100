using EncDotNet.S100.Datasets.Weather;

namespace EncDotNet.S100.Datasets.Grib;

/// <summary>
/// A regular latitude/longitude grid (grid definition template 3.0), as the
/// file states it.
/// </summary>
/// <remarks>
/// Angles are in degrees, converted from the template's basic angle and
/// subdivisions (millionths of a degree by default). The increments are the
/// template's, which are always positive: the scanning mode says which way
/// the rows and columns run. When the template omits an increment (resolution
/// flags), it's worked out from the first and last points.
/// </remarks>
/// <param name="Ni">Number of points along a parallel (columns).</param>
/// <param name="Nj">Number of points along a meridian (rows).</param>
/// <param name="FirstLatitude">Latitude of the first grid point, in degrees.</param>
/// <param name="FirstLongitude">Longitude of the first grid point, in degrees, as stored (often 0 to 360).</param>
/// <param name="LastLatitude">Latitude of the last grid point, in degrees.</param>
/// <param name="LastLongitude">Longitude of the last grid point, in degrees, as stored.</param>
/// <param name="IIncrement">The i-direction (longitude) increment, in degrees; positive.</param>
/// <param name="JIncrement">The j-direction (latitude) increment, in degrees; positive.</param>
/// <param name="ScanningMode">The scanning mode flags (flag table 3.4).</param>
/// <param name="ShapeOfEarth">The shape of the Earth (code table 3.2), for example <c>6</c> for a sphere of radius 6,371,229 m.</param>
public sealed record GribLatLonGrid(
    int Ni,
    int Nj,
    double FirstLatitude,
    double FirstLongitude,
    double LastLatitude,
    double LastLongitude,
    double IIncrement,
    double JIncrement,
    GribScanningMode ScanningMode,
    byte ShapeOfEarth)
{
    /// <summary>
    /// The grid as the weather model's <see cref="WeatherNativeGrid"/>: the
    /// first point, and increments signed by the scanning mode (negative
    /// latitude increment when rows run south, which is scanning mode bit 2
    /// clear; negative longitude increment when points run west, bit 1 set).
    /// </summary>
    /// <remarks>
    /// The values <see cref="GribFile.ReadValues"/> returns are in this grid's
    /// row-major order: <see cref="GribScanningMode.JConsecutive"/> and
    /// <see cref="GribScanningMode.Boustrophedon"/> are undone by the decoder.
    /// </remarks>
    public WeatherNativeGrid ToWeatherNativeGrid() =>
        new(
            Rows: Nj,
            Columns: Ni,
            FirstLatitude: FirstLatitude,
            FirstLongitude: FirstLongitude,
            LatitudeIncrement: (ScanningMode & GribScanningMode.JPositive) != 0 ? JIncrement : -JIncrement,
            LongitudeIncrement: (ScanningMode & GribScanningMode.INegative) != 0 ? -IIncrement : IIncrement);
}
