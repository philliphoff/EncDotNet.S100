using System.Runtime.CompilerServices;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.S101;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;

namespace EncDotNet.S100.Datasets.Pipelines.Spec;

/// <summary>
/// Where a loaded chart cell (S-101, S-401, or S-57 translated to either) ranks
/// against overlapping cells and which places it covers: its
/// <see cref="CellScaleRanking"/> rank and its <c>DataCoverage</c> areas. The
/// route check reads cells the same way, so a consumer that picks the finest
/// cell at a place with <see cref="CellScaleRanking.FinestBand{T}"/> agrees
/// with it and with the display.
/// </summary>
public sealed class ChartCellExtent
{
    private static readonly ConditionalWeakTable<S101DatasetData, ChartCellExtent> Cache = new();

    private readonly BoundingBox _bounds;
    private readonly IReadOnlyList<CoverageArea> _coverage;

    private ChartCellExtent(BoundingBox bounds, CellRank? rank, IReadOnlyList<CoverageArea> coverage)
    {
        _bounds = bounds;
        Rank = rank;
        _coverage = coverage;
    }

    /// <summary>
    /// The cell's rank: a translated S-57 cell's compilation scale, else its
    /// <c>DataCoverage</c> minimum display scale with the maximum display scale
    /// as the tie-break; <see langword="null"/> when it declares neither.
    /// </summary>
    public CellRank? Rank { get; }

    /// <summary>
    /// The extent of a chart cell, or <see langword="null"/> when
    /// <paramref name="dataset"/> isn't one. Built once per cell, on first use,
    /// from the cell's features.
    /// </summary>
    /// <param name="dataset">The loaded dataset.</param>
    /// <returns>The cell's extent, or <see langword="null"/>.</returns>
    public static ChartCellExtent? Of(LoadedDataset dataset)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        if (dataset.Data is not S101DatasetData s101)
        {
            return null;
        }

        var bounds = dataset.Bounds;
        return Cache.GetValue(s101, data => Build(bounds, data));
    }

    /// <summary>
    /// Whether the cell's <c>DataCoverage</c> contains a WGS-84 point. A cell
    /// that declares no coverage covers its bounding box.
    /// </summary>
    /// <param name="latitude">Latitude in decimal degrees.</param>
    /// <param name="longitude">Longitude in decimal degrees.</param>
    /// <returns><see langword="true"/> when the cell covers the point.</returns>
    public bool Covers(double latitude, double longitude)
    {
        if (_coverage.Count == 0)
        {
            return latitude >= _bounds.SouthLatitude && latitude <= _bounds.NorthLatitude
                && longitude >= _bounds.WestLongitude && longitude <= _bounds.EastLongitude;
        }

        foreach (var area in _coverage)
        {
            if (area.Contains(latitude, longitude))
            {
                return true;
            }
        }

        return false;
    }

    private static ChartCellExtent Build(BoundingBox bounds, S101DatasetData data)
    {
        IReadOnlyCollection<Feature> features = new S101VectorSource(data.Dataset).GetFeatures().ToList();
        return new ChartCellExtent(
            bounds,
            S101DatasetProcessor.ResolveCellRank(features, data.CompilationScale),
            CoverageAreaResolver.Resolve(features));
    }
}
