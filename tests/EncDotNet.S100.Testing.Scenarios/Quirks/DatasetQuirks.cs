using EncDotNet.S100.DataModel;
using EncDotNet.S100.Testing.Scenarios.World;

namespace EncDotNet.S100.Testing.Scenarios.Quirks;

/// <summary>
/// The quirks of one dataset, as the hooks a writer calls at each step:
/// attributes, positions, grid georeferencing, node values, record times.
/// With no quirks every hook returns its input, so a writer that calls them
/// writes the same bytes as before.
/// </summary>
/// <remarks>
/// To make a writer quirk-aware, take a <see cref="DatasetQuirks"/> and pass
/// what it writes through the hooks that apply to its product.
/// </remarks>
public sealed class DatasetQuirks
{
    /// <summary>No quirks.</summary>
    public static DatasetQuirks None { get; } = new([]);

    /// <summary>Wraps a dataset's quirks.</summary>
    /// <param name="quirks">The quirks, applied in order.</param>
    public DatasetQuirks(IReadOnlyList<ProducerQuirk> quirks)
    {
        ArgumentNullException.ThrowIfNull(quirks);
        All = quirks;
    }

    /// <summary>The quirks.</summary>
    public IReadOnlyList<ProducerQuirk> All { get; }

    /// <summary>The first quirk of a kind, or <c>null</c>.</summary>
    /// <typeparam name="T">The quirk type.</typeparam>
    /// <returns>The quirk.</returns>
    public T? Get<T>()
        where T : ProducerQuirk => All.OfType<T>().FirstOrDefault();

    /// <summary>Whether the dataset has a quirk of a kind.</summary>
    /// <typeparam name="T">The quirk type.</typeparam>
    /// <returns><c>true</c> if it does.</returns>
    public bool Has<T>()
        where T : ProducerQuirk => Get<T>() is not null;

    /// <summary>
    /// Applies the quirks to HDF5 root attributes: vertical datum, horizontal
    /// CRS, and the bounding box's longitude frame.
    /// </summary>
    /// <param name="root">The root attributes, changed in place.</param>
    public void ApplyToHdf5Root(IDictionary<string, object> root)
    {
        ArgumentNullException.ThrowIfNull(root);
        if (Get<DeclaredVerticalDatum>() is { } datum)
        {
            if (datum.Code is { } code)
            {
                root["verticalDatum"] = code;
            }
            else
            {
                root.Remove("verticalDatum");
            }
        }

        if (Get<DeclaredHorizontalCrs>() is { } crs)
        {
            root["horizontalCRS"] = crs.Epsg;
        }

        if (Has<LongitudeFrame0To360>())
        {
            foreach (var bound in new[] { "westBoundLongitude", "eastBoundLongitude" })
            {
                if (root.TryGetValue(bound, out var value) && value is double longitude)
                {
                    root[bound] = Longitude(longitude);
                }
            }
        }
    }

    /// <summary>A longitude in the frame the quirks write.</summary>
    /// <param name="longitude">The longitude, −180…180°.</param>
    /// <returns>The longitude as written.</returns>
    public double Longitude(double longitude) =>
        Has<LongitudeFrame0To360>() && longitude < 0 ? longitude + 360 : longitude;

    /// <summary>A position as written: longitude frame, then axis order.</summary>
    /// <param name="latitude">The latitude.</param>
    /// <param name="longitude">The longitude.</param>
    /// <returns>The values written in the latitude and longitude places.</returns>
    public (double Latitude, double Longitude) Position(double latitude, double longitude)
    {
        longitude = Longitude(longitude);
        return Has<SwappedAxisOrder>() ? (longitude, latitude) : (latitude, longitude);
    }

    /// <summary>
    /// A regular grid's georeferencing as written: longitude frame, then axis
    /// order (origin, spacing and node counts exchanged).
    /// </summary>
    /// <param name="grid">The true grid.</param>
    /// <returns>The values written in the latitude and longitude places.</returns>
    public (double OriginLatitude, double OriginLongitude, double SpacingLatitudinal, double SpacingLongitudinal, int Rows, int Columns) Grid(
        (double OriginLatitude, double OriginLongitude, double SpacingLatitudinal, double SpacingLongitudinal, int Rows, int Columns) grid)
    {
        var (lat, lon) = Position(grid.OriginLatitude, grid.OriginLongitude);
        return Has<SwappedAxisOrder>()
            ? (lat, lon, grid.SpacingLongitudinal, grid.SpacingLatitudinal, grid.Columns, grid.Rows)
            : (lat, lon, grid.SpacingLatitudinal, grid.SpacingLongitudinal, grid.Rows, grid.Columns);
    }

    /// <summary>Whether a grid node at local <paramref name="x"/>, <paramref name="y"/> is in a no-data hole.</summary>
    /// <param name="x">Metres east.</param>
    /// <param name="y">Metres north.</param>
    /// <returns><c>true</c> if the node is written as the fill value.</returns>
    public bool IsNoData(double x, double y) =>
        Get<NoDataHoles>() is { } holes && holes.Holes.Any(h => h.Contains(x, y));

    /// <summary>Whether a record time falls in a gap.</summary>
    /// <param name="utc">The record time.</param>
    /// <returns><c>true</c> if the record is missing.</returns>
    public bool IsInGap(DateTime utc) =>
        All.OfType<RecordGap>().Any(g => utc >= g.From && utc <= g.To);

    /// <summary>The last record time written: <paramref name="end"/>, or earlier if the run ends sooner.</summary>
    /// <param name="end">The scenario's last record time.</param>
    /// <returns>The last record time.</returns>
    public DateTime LastRecord(DateTime end) =>
        Get<ForecastRunEnds>() is { } run && run.LastRecord < end ? run.LastRecord : end;

    /// <summary>The S-101 <c>DataCoverage</c> display scales to write; <c>null</c> omits one.</summary>
    /// <param name="minimum">The cell's minimum display scale.</param>
    /// <param name="maximum">The cell's maximum display scale.</param>
    /// <returns>The scales to write.</returns>
    public (int? Minimum, int? Maximum) DisplayScales(int minimum, int maximum) =>
        Get<DeclaredDisplayScales>() is { } declared ? (declared.Minimum, declared.Maximum) : (minimum, maximum);

    /// <summary>How much shoaler than the world an S-101 cell charts, in metres.</summary>
    public double ShoalBias => All.OfType<ShoalBias>().Sum(b => b.Metres);

    /// <summary>The spiked areas to add to an S-101 cell.</summary>
    public IEnumerable<SpikedArea> SpikedAreas => All.OfType<SpikedArea>();

    /// <summary>
    /// A spiked area's exterior ring, clockwise: the rectangle with a
    /// zero-width spike out of the middle of its north edge.
    /// </summary>
    /// <param name="frame">The world frame.</param>
    /// <param name="area">The spiked area.</param>
    /// <returns>The closed ring.</returns>
    public static IReadOnlyList<GeoPosition> SpikedRing(WorldFrame frame, SpikedArea area)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(area);
        var r = area.Area;
        var middle = (r.West + r.East) / 2;
        return
        [
            frame.ToGeo(r.West, r.South),
            frame.ToGeo(r.West, r.North),
            frame.ToGeo(middle, r.North),
            frame.ToGeo(middle, r.North + area.SpikeLength),
            frame.ToGeo(middle, r.North),
            frame.ToGeo(r.East, r.North),
            frame.ToGeo(r.East, r.South),
            frame.ToGeo(r.West, r.South),
        ];
    }
}
