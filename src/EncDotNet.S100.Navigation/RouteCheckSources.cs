using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.Navigation;

/// <summary>
/// Everything the route checks read from loaded data, as one bundle: the
/// charted features, charted depth, water level, regulated areas and
/// navigational warnings. A host builds one per set of loaded datasets
/// (<c>CatalogRouteCheckSources</c> in <c>EncDotNet.S100.Datasets.Pipelines</c>)
/// and checks as often as the route changes.
/// </summary>
/// <remarks>
/// <para>
/// A source left <c>null</c> means there's nothing of that kind to check
/// against.
/// </para>
/// <para>
/// Every source must be safe to call from several threads at once, so one
/// bundle can serve concurrent checks without a lock around them.
/// </para>
/// </remarks>
public sealed class RouteCheckSources
{
    /// <summary>A bundle with no sources.</summary>
    public static RouteCheckSources Empty { get; } = new();

    /// <summary>
    /// S-101 cells (and S-401, and translated S-57) with their land,
    /// coastlines, depth, dredged and drying areas, dangers and soundings,
    /// for the chart check.
    /// </summary>
    public IRouteChartSource? Charts { get; init; }

    /// <summary>
    /// Charted depth: S-102, with S-101 as the fallback. The clearance check
    /// reads the whole chain; the chart check reads S-102 only.
    /// </summary>
    public IChartedDepthSource? Depth { get; init; }

    /// <summary>S-104 water level, for the clearance check.</summary>
    public IWaterLevelSource? WaterLevel { get; init; }

    /// <summary>S-101 regulated areas and S-122 protected areas, for the area check.</summary>
    public IRouteAreaSource? Areas { get; init; }

    /// <summary>S-124 navigational warnings, for the warning check.</summary>
    public IRouteWarningSource? Warnings { get; init; }
}

/// <summary>
/// Supplies the navigational warnings a <see cref="WarningCheck"/> checks
/// a route against. <c>EncDotNet.S100.Datasets.Pipelines</c> implements it over
/// loaded S-124 datasets; <see cref="RouteWarningList"/> implements it over a
/// list a host already has.
/// </summary>
/// <remarks>Implementations must be safe to call from several threads at once.</remarks>
public interface IRouteWarningSource
{
    /// <summary>The warning parts whose shapes overlap a box, in a stable order.</summary>
    /// <param name="extent">The box, in WGS-84.</param>
    /// <returns>The warning parts.</returns>
    IReadOnlyList<RouteWarning> WarningsNear(BoundingBox extent);

    /// <summary>
    /// Every loaded warning, including cancellations and in-force bulletins
    /// with no parts, which may end the warnings near the route.
    /// </summary>
    NavigationalWarningSet Set { get; }
}

/// <summary>An <see cref="IRouteWarningSource"/> over a list of warning parts.</summary>
public sealed class RouteWarningList : IRouteWarningSource
{
    private readonly (RouteWarning Warning, BoundingBox? Extent)[] _warnings;

    /// <summary>Creates the source.</summary>
    /// <param name="warnings">The warning parts.</param>
    /// <param name="set">
    /// Every loaded warning, including cancellations and in-force bulletins
    /// with no parts; <c>null</c> builds it from <paramref name="warnings"/>.
    /// </param>
    public RouteWarningList(IReadOnlyList<RouteWarning> warnings, NavigationalWarningSet? set = null)
    {
        ArgumentNullException.ThrowIfNull(warnings);
        _warnings = warnings
            .Select(w => (w ?? throw new ArgumentException("A warning is null.", nameof(warnings)), Extent(w.Shapes)))
            .ToArray();
        Set = set ?? new NavigationalWarningSet(warnings.Select(w => w.Info));
    }

    /// <inheritdoc/>
    public NavigationalWarningSet Set { get; }

    /// <summary>Every warning part, in the order given.</summary>
    public IReadOnlyList<RouteWarning> All => _warnings.Select(w => w.Warning).ToArray();

    /// <inheritdoc/>
    public IReadOnlyList<RouteWarning> WarningsNear(BoundingBox extent)
    {
        ArgumentNullException.ThrowIfNull(extent);
        var result = new List<RouteWarning>();
        foreach (var (warning, box) in _warnings)
        {
            if (box is { } b
                && b.SouthLatitude <= extent.NorthLatitude && b.NorthLatitude >= extent.SouthLatitude
                && b.WestLongitude <= extent.EastLongitude && b.EastLongitude >= extent.WestLongitude)
            {
                result.Add(warning);
            }
        }

        return result;
    }

    // The box round every point, curve and surface of a part; null when it has none.
    private static BoundingBox? Extent(IReadOnlyList<S100FeatureGeometry> shapes)
    {
        double south = double.PositiveInfinity, west = double.PositiveInfinity;
        double north = double.NegativeInfinity, east = double.NegativeInfinity;
        void Add(GeoPosition p)
        {
            south = Math.Min(south, p.Latitude);
            north = Math.Max(north, p.Latitude);
            west = Math.Min(west, p.Longitude);
            east = Math.Max(east, p.Longitude);
        }

        foreach (var shape in shapes)
        {
            foreach (var p in shape.Points)
            {
                Add(p);
            }

            foreach (var curve in shape.Curves)
            {
                foreach (var p in curve)
                {
                    Add(p);
                }
            }

            foreach (var p in shape.ExteriorRing)
            {
                Add(p);
            }
        }

        return double.IsFinite(south) ? new BoundingBox(south, west, north, east) : null;
    }
}
