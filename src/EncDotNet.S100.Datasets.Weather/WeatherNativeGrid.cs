namespace EncDotNet.S100.Datasets.Weather;

/// <summary>
/// A regular latitude/longitude grid as the file stores it, before
/// normalisation: point <c>(row, col)</c> of a decoded step is at latitude
/// <c>FirstLatitude + row × LatitudeIncrement</c> and longitude
/// <c>FirstLongitude + col × LongitudeIncrement</c>, and the values are
/// row-major (<c>row × Columns + col</c>).
/// </summary>
/// <remarks>
/// A GRIB2 grid with scanning mode bit 2 clear (rows running south) has a
/// negative <see cref="LatitudeIncrement"/>, as does a CF grid with a
/// decreasing latitude axis. Longitudes may be in either the 0 to 360 or the
/// −180 to 180 convention. <see cref="WeatherGridLayout.Create"/> turns this
/// into the south-west-origin <see cref="WeatherGrid"/> the coverage pipeline
/// expects.
/// </remarks>
/// <param name="Rows">Number of rows (points along the latitude axis).</param>
/// <param name="Columns">Number of columns (points along the longitude axis).</param>
/// <param name="FirstLatitude">Latitude of the first row, in degrees.</param>
/// <param name="FirstLongitude">Longitude of the first column, in degrees.</param>
/// <param name="LatitudeIncrement">Degrees from one row to the next; negative when rows run south.</param>
/// <param name="LongitudeIncrement">Degrees from one column to the next; negative when columns run west.</param>
public sealed record WeatherNativeGrid(
    int Rows,
    int Columns,
    double FirstLatitude,
    double FirstLongitude,
    double LatitudeIncrement,
    double LongitudeIncrement);
