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
/// <para>
/// A harmonic tide prediction (<see cref="WaterLevelSourceKind.Prediction"/>)
/// has no steps and no end, so its part of the curve is drawn every
/// <see cref="PredictedStep"/> for <see cref="PredictedHalfWindow"/> either
/// side of the view time (tide predictions design D8). S-104 steps are kept
/// where S-104 covers, as the water-level rule prefers it.
/// </para>
/// </remarks>
internal sealed class DepthAssimilationService
{
    /// <summary>How far either side of the view time a predicted curve is drawn.</summary>
    public static readonly TimeSpan PredictedHalfWindow = TimeSpan.FromHours(12);

    /// <summary>The step a predicted curve is drawn at.</summary>
    public static readonly TimeSpan PredictedStep = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Assimilates the base depth and the water-level series at a location.
    /// </summary>
    /// <param name="baseDepth">
    /// The resolved base depth, or <c>null</c> when none could be determined
    /// (the result is then <c>null</c>).
    /// </param>
    /// <param name="series">The water-level series at the point, most preferred first; may be empty.</param>
    /// <param name="applyUndeclaredDatum">Applies water level where either vertical datum isn't declared (#1132 Q2); otherwise it's refused.</param>
    /// <param name="viewTimeUtc">
    /// The Timeline's time, round which a predicted curve is drawn; <c>null</c>
    /// when there's none, and then the curve has only S-104's steps.
    /// </param>
    /// <returns>The assimilated result, or <c>null</c> when <paramref name="baseDepth"/> is <c>null</c>.</returns>
    public LocationDepthResult? Assimilate(
        BaseDepthResult? baseDepth,
        IReadOnlyList<IWaterLevelSeries> series,
        bool applyUndeclaredDatum = false,
        DateTime? viewTimeUtc = null)
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
            .Where(s => VerticalDatumCheck.Compare(baseDepth.VerticalDatumCode, s.Source.VerticalDatum) switch
            {
                DatumAgreement.Same => true,
                DatumAgreement.Unknown => applyUndeclaredDatum,
                _ => false,
            })
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
                    preferred.VerticalDatum)
                {
                    Predicted = preferred.IsPredicted,
                },
            };
        }

        var times = compatible
            .Where(s => !s.Source.IsPredicted)
            .SelectMany(s => s.StepsWithin(s.Start, s.End))
            .Concat(PredictedTimes(compatible, viewTimeUtc))
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
                    LocationDepthResult.CombineUncertainty(uncertainty, sample.UncertaintyMeters),
                    sample.Source.IsPredicted));
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
            uncertainty,
            DatumUnconfirmed: VerticalDatumCheck.Compare(baseDepth.VerticalDatumCode, first.VerticalDatum) != DatumAgreement.Same);
    }

    // Every PredictedStep across the window round the view time, when a
    // predicted series is among those applied.
    private static IEnumerable<DateTime> PredictedTimes(IReadOnlyList<IWaterLevelSeries> series, DateTime? viewTimeUtc)
    {
        if (viewTimeUtc is not { } view || !series.Any(s => s.Source.IsPredicted))
        {
            yield break;
        }

        view = view.Kind == DateTimeKind.Local ? view.ToUniversalTime() : DateTime.SpecifyKind(view, DateTimeKind.Utc);
        var ticks = PredictedStep.Ticks;
        var start = new DateTime((view - PredictedHalfWindow).Ticks / ticks * ticks, DateTimeKind.Utc);
        for (var t = start; t <= view + PredictedHalfWindow; t += PredictedStep)
        {
            yield return t;
        }
    }

    /// <summary>
    /// The water-level-free (static) result: the base depth alone, so the card
    /// shows "DEPTH (STATIC)".
    /// </summary>
    private static LocationDepthResult NoTide(BaseDepthResult baseDepth, double? uncertainty) =>
        new(baseDepth, Tide: null, DepthOverTime: [], uncertainty);
}
