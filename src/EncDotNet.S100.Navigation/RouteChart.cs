using EncDotNet.S100.DataModel;
using EncDotNet.S100.Features;
using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.Navigation;

/// <summary>
/// What a charted feature means to the route check. Restricted and protected
/// areas (#956) and S-124 warnings (#957) have their own checks
/// (<see cref="RouteAreaCheck"/>, <see cref="RouteWarningCheck"/>).
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
}

/// <summary>
/// Supplies the charted features a <see cref="RouteCheckEngine"/> checks a
/// route against. <c>EncDotNet.S100.Datasets.Pipelines</c> implements it over
/// loaded S-101 cells (<c>RouteCheckDataSources</c>).
/// </summary>
public interface IRouteChartSource
{
    /// <summary>The cells whose extent overlaps a box.</summary>
    /// <param name="extent">The box, in WGS-84.</param>
    /// <returns>The cells, in a stable order.</returns>
    IReadOnlyList<RouteChartCell> CellsNear(BoundingBox extent);

    /// <summary>
    /// The features of the kinds in <see cref="RouteChartFeatureKind"/> whose
    /// geometry truly intersects a leg's corridor, from every cell. The
    /// engine applies the finest-cell rule itself, so a source returns the
    /// features of every overlapping cell.
    /// </summary>
    /// <param name="corridor">The corridor's closed ring, in WGS-84.</param>
    /// <param name="extent">The ring's bounding box.</param>
    /// <returns>The features.</returns>
    IReadOnlyList<RouteChartFeature> FeaturesInCorridor(IReadOnlyList<GeoPosition> corridor, BoundingBox extent);
}
