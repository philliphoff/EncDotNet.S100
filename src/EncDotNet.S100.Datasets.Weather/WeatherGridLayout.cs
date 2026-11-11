using System.Globalization;

namespace EncDotNet.S100.Datasets.Weather;

/// <summary>
/// How a file's grid (<see cref="WeatherNativeGrid"/>) maps onto the
/// normalised <see cref="WeatherGrid"/>, and the code that reorders a decoded
/// step to match (design note weather-sources.md §3.4).
/// </summary>
/// <remarks>
/// <para>Normalisation does four things:</para>
/// <list type="bullet">
/// <item><description>Rows that run north to south are flipped, so the origin is the southern row.</description></item>
/// <item><description>Columns that run west are flipped, so columns run east.</description></item>
/// <item><description>
/// Longitudes are moved into −180 to 180. A grid that lies on one side of 180°
/// is shifted as a whole: a 0 to 360 grid entirely east of 180° (for example
/// 185°E to 225°E) gets −360. A global grid has its columns at 180° and above
/// moved to the west, and a repeated wrap-around column (0° and 360°) is
/// dropped.
/// </description></item>
/// <item><description>Missing values become <see cref="float.NaN"/>.</description></item>
/// </list>
/// <para>
/// A regional grid that truly crosses the antimeridian, such as 175°E to
/// 225°E (175°E to 135°W), has no contiguous −180 to 180 form and is rejected
/// with <see cref="UnsupportedWeatherGridException"/> (open question §10.3).
/// </para>
/// </remarks>
public sealed class WeatherGridLayout
{
    // Tolerance for comparing longitudes, in degrees: far below any real grid
    // spacing, far above the rounding in a GRIB2 millionths-of-a-degree value.
    private const double Tolerance = 1e-6;

    private readonly int[] _columnMap;

    private WeatherGridLayout(WeatherNativeGrid native, WeatherGrid grid, bool rowsFlipped, bool columnsFlipped, int columnRotation, int[] columnMap)
    {
        Native = native;
        Grid = grid;
        RowsFlipped = rowsFlipped;
        ColumnsFlipped = columnsFlipped;
        ColumnRotation = columnRotation;
        _columnMap = columnMap;
    }

    /// <summary>The grid as the file stores it.</summary>
    public WeatherNativeGrid Native { get; }

    /// <summary>The normalised grid.</summary>
    public WeatherGrid Grid { get; }

    /// <summary>Whether the file's rows run north to south and are flipped.</summary>
    public bool RowsFlipped { get; }

    /// <summary>Whether the file's columns run west and are flipped.</summary>
    public bool ColumnsFlipped { get; }

    /// <summary>
    /// For a global grid, the number of columns (after any flip) moved from
    /// the east end to the west end so the grid starts at −180°; otherwise 0.
    /// </summary>
    public int ColumnRotation { get; }

    /// <summary>
    /// The number of repeated wrap-around columns dropped from a global grid
    /// whose last column repeats its first (for example 0° and 360°).
    /// </summary>
    public int DroppedColumns => Native.Columns - Grid.Columns;

    /// <summary>
    /// Works out the normalised grid for <paramref name="native"/>.
    /// </summary>
    /// <param name="native">The grid as the file stores it.</param>
    /// <returns>The layout.</returns>
    /// <exception cref="UnsupportedWeatherGridException">
    /// The grid has no points, a zero or non-finite increment, latitudes
    /// outside ±90°, or crosses the antimeridian without being global.
    /// </exception>
    public static WeatherGridLayout Create(WeatherNativeGrid native)
    {
        ArgumentNullException.ThrowIfNull(native);

        if (native.Rows < 1 || native.Columns < 1)
        {
            throw new UnsupportedWeatherGridException(
                Format($"The weather grid has {native.Rows} rows and {native.Columns} columns; it needs at least one of each."));
        }

        if (!double.IsFinite(native.FirstLatitude) || !double.IsFinite(native.FirstLongitude) ||
            !IsUsableIncrement(native.LatitudeIncrement) || !IsUsableIncrement(native.LongitudeIncrement))
        {
            throw new UnsupportedWeatherGridException(
                "The weather grid's first point or increments aren't finite, non-zero numbers, so it isn't a regular latitude/longitude grid.");
        }

        // Rows: south-west origin, rows running north.
        bool rowsFlipped = native.LatitudeIncrement < 0;
        double latitudeSpacing = Math.Abs(native.LatitudeIncrement);
        double southLatitude = rowsFlipped
            ? native.FirstLatitude + (native.Rows - 1) * native.LatitudeIncrement
            : native.FirstLatitude;
        double northLatitude = southLatitude + (native.Rows - 1) * latitudeSpacing;
        if (southLatitude < -90 - Tolerance || northLatitude > 90 + Tolerance)
        {
            throw new UnsupportedWeatherGridException(
                Format($"The weather grid spans latitudes {southLatitude}° to {northLatitude}°, outside −90° to 90°."));
        }

        // Columns: running east from the western column.
        bool columnsFlipped = native.LongitudeIncrement < 0;
        double longitudeSpacing = Math.Abs(native.LongitudeIncrement);
        double westLongitude = columnsFlipped
            ? native.FirstLongitude + (native.Columns - 1) * native.LongitudeIncrement
            : native.FirstLongitude;

        int columns = native.Columns;
        bool global = columns * longitudeSpacing >= 360 - Tolerance;
        if (global && columns * longitudeSpacing > 360 + Tolerance)
        {
            // A repeated wrap-around column (0° and 360°) or more: keep one turn.
            int oneTurn = (int)Math.Round(360 / longitudeSpacing);
            if (Math.Abs(oneTurn * longitudeSpacing - 360) > Tolerance || oneTurn > columns)
            {
                throw new UnsupportedWeatherGridException(
                    Format($"The weather grid has {columns} columns {longitudeSpacing}° apart, which overlaps itself by more than one turn of longitude without a whole number of columns per turn."));
            }

            columns = oneTurn;
        }

        double west = WrapLongitude(westLongitude);
        double east = west + (columns - 1) * longitudeSpacing;
        int rotation = 0;
        double originLongitude = west;
        if (east > 180 + Tolerance)
        {
            if (!global)
            {
                throw new UnsupportedWeatherGridException(
                    Format($"The weather grid spans longitudes {westLongitude}° to {westLongitude + (native.Columns - 1) * longitudeSpacing}°, which crosses the antimeridian (180°). Grids that cross 180° aren't supported yet; use a subset that lies on one side of it."));
            }

            // Move the columns at 180° and above to the west end.
            rotation = (int)Math.Ceiling((180 - west) / longitudeSpacing - Tolerance);
            originLongitude = west + rotation * longitudeSpacing - 360;
        }

        var columnMap = new int[columns];
        for (int c = 0; c < columns; c++)
        {
            int eastward = (c + rotation) % columns;
            columnMap[c] = columnsFlipped ? native.Columns - 1 - eastward : eastward;
        }

        var grid = new WeatherGrid(
            native.Rows,
            columns,
            southLatitude,
            originLongitude,
            latitudeSpacing,
            longitudeSpacing);
        return new WeatherGridLayout(native, grid, rowsFlipped, columnsFlipped, rotation, columnMap);
    }

    /// <summary>
    /// Reorders one decoded step from the file's order into the normalised
    /// grid's order, replacing missing values with <see cref="float.NaN"/>.
    /// </summary>
    /// <param name="nativeValues">
    /// The step's values in the file's row-major order, of length
    /// <see cref="WeatherNativeGrid.Rows"/> × <see cref="WeatherNativeGrid.Columns"/>.
    /// </param>
    /// <param name="missingValues">
    /// Sentinel values that mark missing points, for example a CF
    /// <c>_FillValue</c> and <c>missing_value</c>. NaN is always missing.
    /// </param>
    /// <returns>A new array of length <see cref="WeatherGrid.CellCount"/>.</returns>
    /// <exception cref="ArgumentException"><paramref name="nativeValues"/> has the wrong length.</exception>
    public float[] Normalize(ReadOnlySpan<float> nativeValues, IReadOnlyList<float>? missingValues = null)
    {
        int nativeColumns = Native.Columns;
        if (nativeValues.Length != Native.Rows * nativeColumns)
        {
            throw new ArgumentException(
                Format($"A decoded step has {nativeValues.Length} values; the grid has {Native.Rows} × {nativeColumns} = {Native.Rows * nativeColumns} points."),
                nameof(nativeValues));
        }

        int rows = Grid.Rows;
        int columns = Grid.Columns;
        var result = new float[rows * columns];
        IReadOnlyList<float> sentinels = missingValues ?? [];
        bool hasSentinels = sentinels.Count > 0;
        for (int r = 0; r < rows; r++)
        {
            int nativeRow = RowsFlipped ? rows - 1 - r : r;
            var source = nativeValues.Slice(nativeRow * nativeColumns, nativeColumns);
            var destination = result.AsSpan(r * columns, columns);
            for (int c = 0; c < columns; c++)
            {
                float value = source[_columnMap[c]];
                if (hasSentinels && IsSentinel(value, sentinels))
                    value = float.NaN;
                destination[c] = value;
            }
        }

        return result;
    }

    private static bool IsSentinel(float value, IReadOnlyList<float> missingValues)
    {
        for (int i = 0; i < missingValues.Count; i++)
        {
            if (value == missingValues[i])
                return true;
        }

        return false;
    }

    private static bool IsUsableIncrement(double increment) =>
        double.IsFinite(increment) && increment != 0;

    // Wraps a longitude into [-180, 180).
    private static double WrapLongitude(double longitude)
    {
        double wrapped = ((longitude + 180) % 360 + 360) % 360 - 180;
        // Snap values a rounding error short of 180 back to -180 rather than
        // leave them at 179.9999999.
        return wrapped > 180 - Tolerance ? wrapped - 360 : wrapped;
    }

    private static string Format(FormattableString message) => message.ToString(CultureInfo.InvariantCulture);
}
