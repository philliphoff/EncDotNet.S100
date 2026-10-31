using EncDotNet.S100.Navigation;

namespace EncDotNet.S100.Viewer.Services.Depth;

/// <summary>
/// Combines a picked location's base depth with the S-104 water level there
/// into a single <see cref="LocationDepthResult"/>.
/// </summary>
/// <remarks>
/// <para>
/// The water-level series come from the same <see cref="IWaterLevelSource"/>
/// the passage check reads (<c>CatalogWaterLevelSource</c>), in its
/// preference order: grids before stations, the finest grid, then the latest
/// issue, then the dataset id. Each time is read through
/// <see cref="WaterLevelSnapshot.Resolve(IReadOnlyList{IWaterLevelSeries}, DateTime, int?)"/>,
/// so the card and the passage check give the same adjusted depth for a point
/// and time. The adjusted depth is <c>base + h</c> in metres.
/// </para>
/// <para>
/// Water level on another vertical datum than the charted depth, or where
/// either datum isn't declared, is refused, not applied with a warning (S-98
/// Appendix D, S-164 IncompatibleDatums): the card shows the charted depth and
/// the reason. Only series on the charted depth's datum are applied.
/// </para>
/// </remarks>
internal sealed class DepthAssimilationService
{
    /// <summary>
    /// Assimilates the base depth and the water-level series at a location.
    /// </summary>
    /// <param name="baseDepth">
    /// The resolved base depth, or <c>null</c> when none could be determined
    /// (the result is then <c>null</c>).
    /// </param>
    /// <param name="series">The water-level series at the point, most preferred first; may be empty.</param>
    /// <returns>The assimilated result, or <c>null</c> when <paramref name="baseDepth"/> is <c>null</c>.</returns>
    public LocationDepthResult? Assimilate(BaseDepthResult? baseDepth, IReadOnlyList<IWaterLevelSeries> series)
    {
        ArgumentNullException.ThrowIfNull(series);

        if (baseDepth is null)
        {
            return null;
        }

        var uncertainty = baseDepth.Source == BaseDepthSource.Bathymetry
            ? baseDepth.UncertaintyMeters
            : null;

        if (series.Count == 0)
        {
            // Partial state: water + base depth, but no S-104 covers the point.
            return NoTide(baseDepth, uncertainty);
        }

        var compatible = series
            .Where(s => VerticalDatumCheck.Compare(baseDepth.VerticalDatumCode, s.Source.VerticalDatum) == DatumAgreement.Same)
            .ToList();
        if (compatible.Count == 0)
        {
            var preferred = series[0].Source;
            return NoTide(baseDepth, uncertainty) with
            {
                Refusal = new WaterLevelRefusal(
                    VerticalDatumCheck.Compare(baseDepth.VerticalDatumCode, preferred.VerticalDatum),
                    baseDepth.Source,
                    baseDepth.VerticalDatumCode,
                    preferred.DatasetId,
                    preferred.VerticalDatum),
            };
        }

        var times = compatible
            .SelectMany(s => s.StepsWithin(s.Start, s.End))
            .Distinct()
            .Order()
            .ToList();
        var curve = new List<DepthOverTimePoint>(times.Count);
        var hasUsableTide = false;
        foreach (var time in times)
        {
            var water = WaterLevelSnapshot.Resolve(compatible, time, baseDepth.VerticalDatumCode);
            if (water is { Status: WaterLevelStatus.Applied, Sample: { } sample })
            {
                hasUsableTide = true;
                curve.Add(new DepthOverTimePoint(
                    time,
                    baseDepth.DepthMeters + sample.HeightMeters,
                    LocationDepthResult.CombineUncertainty(uncertainty, sample.UncertaintyMeters)));
            }
            else
            {
                curve.Add(new DepthOverTimePoint(time, null));
            }
        }

        if (!hasUsableTide)
        {
            // The grid nominally overlaps, but every time step at this cell is
            // NoData (e.g. a land-masked cell at a quay edge). There is no
            // usable water level here, so present the base depth statically.
            return NoTide(baseDepth, uncertainty);
        }

        var first = compatible[0].Source;
        return new LocationDepthResult(
            baseDepth,
            new LocationTideSelection(first.DatasetId, first.VerticalDatum, compatible),
            curve,
            uncertainty);
    }

    /// <summary>
    /// The water-level-free (static) result: the base depth alone, so the card
    /// shows "DEPTH (STATIC)".
    /// </summary>
    private static LocationDepthResult NoTide(BaseDepthResult baseDepth, double? uncertainty) =>
        new(baseDepth, Tide: null, DepthOverTime: [], uncertainty);
}
