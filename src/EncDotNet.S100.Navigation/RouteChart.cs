using EncDotNet.S100.DataModel;
using EncDotNet.S100.Features;
using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.Navigation;

/// <summary>
/// What a charted feature means to the route check. Restricted and protected
/// areas (#956) and S-124 warnings (#957) have their own checks
/// (<see cref="AreaCheck"/>, <see cref="WarningCheck"/>).
/// </summary>
public enum RouteChartFeatureKind
{
    /// <summary>A <c>LandArea</c>.</summary>
    Land,

    /// <summary>A <c>Coastline</c> curve.</summary>
    Coastline,

    /// <summary>A drying <c>DepthArea</c> (see <see cref="SkinOfTheEarth.IsDrying"/>).</summary>
    DryingArea,

    /// <summary>
    /// A <c>DepthArea</c> that doesn't dry, with its minimum depth (DRVAL1)
    /// in <see cref="RouteChartFeature.DepthMeters"/> (#955).
    /// </summary>
    DepthArea,

    /// <summary>
    /// A <c>DredgedArea</c>, with its maintained depth (DRVAL1) in
    /// <see cref="RouteChartFeature.DepthMeters"/> (#955).
    /// </summary>
    DredgedArea,

    /// <summary>
    /// A charted danger: a <c>Wreck</c>, <c>Obstruction</c> or
    /// <c>UnderwaterAwashRock</c>, with its <c>valueOfSounding</c> in
    /// <see cref="RouteChartFeature.DepthMeters"/> when it has one and its
    /// <c>waterLevelEffect</c> in <see cref="RouteChartFeature.WaterLevelEffect"/> (#955).
    /// </summary>
    Danger,

    /// <summary>
    /// One sounding: a single point of a <c>Sounding</c> feature, with its
    /// depth in <see cref="RouteChartFeature.DepthMeters"/> (#955).
    /// </summary>
    Sounding,

    /// <summary>
    /// Any other feature whose portrayal raises a <c>NavHazard</c> or
    /// <c>SafetyContour</c> alert (<see cref="RouteChartFeature.Alerts"/>),
    /// such as a buoy, a pile or a pontoon (#1127).
    /// </summary>
    Alerted,
}

/// <summary>
/// An alert in the S-101 alert catalogue: what a feature's portrayal says a
/// route crossing it should raise (S-100 Part 9 <c>alertReference</c>,
/// S-98 §12.10).
/// </summary>
public enum RouteFindingAlert
{
    /// <summary><c>SafetyContour</c>: water no deeper than the safety contour, or something that stands in it.</summary>
    SafetyContour,

    /// <summary><c>ProhAre</c>: a prohibited area or an area with special conditions.</summary>
    ProhAre,

    /// <summary><c>NavHazard</c>: a danger or aid to navigation to pass close to.</summary>
    NavHazard,
}

/// <summary>An alert a charted feature's portrayal raises (#1127).</summary>
/// <param name="Alert">The alert.</param>
/// <param name="AreaKind">
/// For <see cref="RouteFindingAlert.ProhAre"/>, the kind of area: the viewing
/// group of its highlight (53012–53024 in S-101), or <c>null</c>.
/// </param>
/// <param name="Position">
/// Where it's raised, when that's one point of the feature (an S-101
/// sounding raises one per depth); <c>null</c> for the whole feature.
/// </param>
public sealed record RouteChartAlert(RouteFindingAlert Alert, int? AreaKind = null, GeoPosition? Position = null);

/// <summary>
/// What a chart source needs to know to portray a corridor's features as
/// the check sees them: the depths the S-101 alert rules compare against.
/// </summary>
/// <param name="SafetyContourMeters">The safety contour, or <c>null</c> for the portrayal's default.</param>
/// <param name="SafetyDepthMeters">The safety depth, or <c>null</c> for the portrayal's default.</param>
public sealed record RouteChartQuery(double? SafetyContourMeters = null, double? SafetyDepthMeters = null)
{
    /// <summary>The portrayal's defaults.</summary>
    public static RouteChartQuery Default { get; } = new();
}

/// <summary>
/// A loaded chart cell, as the route check sees it: its scale rank and the
/// area it declares coverage for.
/// </summary>
public abstract class RouteChartCell
{
    /// <summary>Creates the cell.</summary>
    /// <param name="datasetId">The cell's catalog id.</param>
    protected RouteChartCell(string datasetId)
    {
        ArgumentNullException.ThrowIfNull(datasetId);
        DatasetId = datasetId;
    }

    /// <summary>The cell's catalog id.</summary>
    public string DatasetId { get; }

    /// <summary>The cell's <see cref="CellScaleRanking"/> scale; smaller is finer, <c>null</c> when unranked.</summary>
    public abstract int? RankingScale { get; }

    /// <summary>
    /// Whether the cell's data coverage (its S-101 <c>DataCoverage</c>, or
    /// its extent when it declares none) contains a point.
    /// </summary>
    /// <param name="position">The point.</param>
    /// <returns><c>true</c> when the cell covers the point.</returns>
    public abstract bool Covers(GeoPosition position);
}

/// <summary>
/// A charted feature near a route leg: its kind, the cell it comes from,
/// its geometry in WGS-84, and the depth it charts.
/// </summary>
/// <param name="Kind">What the feature means to the check.</param>
/// <param name="Cell">The cell that charts it.</param>
/// <param name="FeatureId">The feature's id within the cell.</param>
/// <param name="FeatureType">The feature type, such as <c>"LandArea"</c>.</param>
/// <param name="Surfaces">The feature's surfaces, for an area; empty otherwise.</param>
/// <param name="Curves">The feature's curves, for a line; empty otherwise.</param>
public sealed record RouteChartFeature(
    RouteChartFeatureKind Kind,
    RouteChartCell Cell,
    string FeatureId,
    string FeatureType,
    IReadOnlyList<SurfacePart> Surfaces,
    IReadOnlyList<IReadOnlyList<GeoPosition>> Curves)
{
    /// <summary>The feature's points, for a point feature (a danger or a sounding); empty otherwise.</summary>
    public IReadOnlyList<GeoPosition> Points { get; init; } = [];

    /// <summary>
    /// The least depth the feature charts, in metres below the vertical
    /// datum (negative for a drying height): DRVAL1 for a depth, dredged or
    /// drying area, <c>valueOfSounding</c> for a danger, the depth of a
    /// sounding. <c>null</c> when it charts none, such as a danger of
    /// unknown depth.
    /// </summary>
    public double? DepthMeters { get; init; }

    /// <summary>
    /// The S-101 <c>waterLevelEffect</c> code of a danger (1 partly
    /// submerged at high water, 2 always dry, 3 always under water, 4 covers
    /// and uncovers, 5 awash, 6 subject to inundation or flooding, 7
    /// floating), or <c>null</c> when it isn't given.
    /// </summary>
    public int? WaterLevelEffect { get; init; }

    /// <summary>
    /// The alerts the feature's portrayal raises at the query's depths
    /// (#1127), in emission order; empty when it raises none or the source
    /// can't portray it.
    /// </summary>
    public IReadOnlyList<RouteChartAlert> Alerts { get; init; } = [];
}

/// <summary>
/// Supplies the charted features a <see cref="ChartCheck"/> checks a
/// route against. <c>EncDotNet.S100.Datasets.Pipelines</c> implements it over
/// loaded S-101 cells (<c>CatalogRouteCheckSources</c>).
/// </summary>
/// <remarks>Implementations must be safe to call from several threads at once.</remarks>
public interface IRouteChartSource
{
    /// <summary>The cells whose extent overlaps a box.</summary>
    /// <param name="extent">The box, in WGS-84.</param>
    /// <returns>The cells, in a stable order.</returns>
    IReadOnlyList<RouteChartCell> CellsNear(BoundingBox extent);

    /// <summary>
    /// The features of the kinds in <see cref="RouteChartFeatureKind"/> whose
    /// geometry truly intersects a leg's corridor, from every cell, with the
    /// alerts their portrayal raises at the query's depths. The check applies
    /// the finest-cell rule itself, so a source returns the features of every
    /// overlapping cell.
    /// </summary>
    /// <param name="corridor">The corridor's closed ring, in WGS-84.</param>
    /// <param name="extent">The ring's bounding box.</param>
    /// <param name="query">The depths the alerts are judged at.</param>
    /// <returns>The features.</returns>
    IReadOnlyList<RouteChartFeature> FeaturesInCorridor(IReadOnlyList<GeoPosition> corridor, BoundingBox extent, RouteChartQuery query);
}
