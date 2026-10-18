using EncDotNet.S100.DataModel;
using EncDotNet.S100.Features;
using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.Navigation;

/// <summary>
/// What a charted feature means to the route check. More kinds join as the
/// checks grow (charted dangers, restricted areas and S-124 warnings, #955–#957).
/// </summary>
public enum RouteChartFeatureKind
{
    /// <summary>A <c>LandArea</c>.</summary>
    Land,

    /// <summary>A <c>Coastline</c> curve.</summary>
    Coastline,

    /// <summary>A drying <c>DepthArea</c> (see <see cref="SkinOfTheEarth.IsDrying"/>).</summary>
    DryingArea,
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
/// and its geometry in WGS-84.
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
    IReadOnlyList<IReadOnlyList<GeoPosition>> Curves);

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
