namespace EncDotNet.S100.Viewer.Services.Depth;

/// <summary>
/// Orchestrates depth assimilation for a picked location: combines the chosen
/// base depth, the deterministically selected S-104 tide series, and a
/// vertical-datum comparison into a single <see cref="LocationDepthResult"/>.
/// </summary>
/// <remarks>
/// <para>
/// The base depth (from <see cref="BaseDepthResolver"/>) and the per-candidate
/// tide series (from <c>S104TimeSeriesSampler</c>) are produced by the caller
/// and injected, so this service is pure composition/selection logic and is
/// independently unit-testable.
/// </para>
/// <para>
/// Tide selection follows the design: when several S-104 grids overlap, pick
/// the finest resolution, breaking ties by the latest issuance. The
/// tide-adjusted depth is <c>base + tide(t)</c> in metres (total available
/// water depth). A datum caveat is raised — per the "assume + warn" policy —
/// when the S-102 base and S-104 tide datums cannot be confirmed identical;
/// the curve shape stays valid, only the absolute baseline is caveated.
/// </para>
/// </remarks>
internal sealed class DepthAssimilationService
{
    /// <summary>
    /// Assimilates the base depth and tide candidates for a location.
    /// </summary>
    /// <param name="baseDepth">
    /// The resolved base depth, or <c>null</c> when none could be determined
    /// (in which case there is nothing to assimilate and the result is
    /// <c>null</c>).
    /// </param>
    /// <param name="tideCandidates">
    /// The S-104 tide candidates sampled at the point; may be empty.
    /// </param>
    /// <returns>
    /// The assimilated result, or <c>null</c> when <paramref name="baseDepth"/>
    /// is <c>null</c>.
    /// </returns>
    public LocationDepthResult? Assimilate(
        BaseDepthResult? baseDepth,
        IReadOnlyList<S104TideCandidate> tideCandidates)
    {
        ArgumentNullException.ThrowIfNull(tideCandidates);

        if (baseDepth is null)
        {
            return null;
        }

        var uncertainty = baseDepth.Source == BaseDepthSource.Bathymetry
            ? baseDepth.UncertaintyMeters
            : null;

        var selected = SelectTide(tideCandidates);
        if (selected?.Series is null)
        {
            // Partial state: water + base depth, but no overlapping S-104 grid.
            return NoTide(baseDepth, uncertainty);
        }

        var curve = new List<DepthOverTimePoint>(selected.Series.Points.Count);
        var hasUsableTide = false;
        foreach (var point in selected.Series.Points)
        {
            double? depth = null;
            if (point.HeightMeters is { } height)
            {
                depth = baseDepth.DepthMeters + height;
                hasUsableTide = true;
            }

            curve.Add(new DepthOverTimePoint(point.Time, depth));
        }

        if (!hasUsableTide)
        {
            // The grid nominally overlaps, but every time-step at this cell is
            // NODATA (e.g. a land-masked cell at a quay edge). There is no
            // usable tide correction here, so present the base depth statically
            // rather than a "DEPTH NOW" of n/a.
            return NoTide(baseDepth, uncertainty);
        }

        return new LocationDepthResult(
            baseDepth,
            new LocationTideSelection(selected.DatasetId, selected.VerticalDatumCode),
            curve,
            uncertainty,
            IsDatumMismatch(baseDepth, selected));
    }

    /// <summary>
    /// Builds the tide-less (static) result: the base depth stands alone with
    /// no time series, so the card shows "DEPTH (STATIC)" and the no-tide
    /// info-bar. Used both when no S-104 grid overlaps and when the overlapping
    /// grid yields only NODATA at the picked cell.
    /// </summary>
    private static LocationDepthResult NoTide(BaseDepthResult baseDepth, double? uncertainty) =>
        new(
            baseDepth,
            Tide: null,
            DepthOverTime: [],
            uncertainty,
            DatumsNotReconciled: false);

    /// <summary>
    /// Selects the best tide candidate: finest resolution (smallest spacing),
    /// then latest issuance. Only candidates that were sampled in-bounds (a
    /// non-<c>null</c> <see cref="S104TideCandidate.Series"/>) are eligible.
    /// </summary>
    private static S104TideCandidate? SelectTide(IReadOnlyList<S104TideCandidate> candidates)
    {
        S104TideCandidate? best = null;
        foreach (var candidate in candidates)
        {
            if (candidate.Series is null)
            {
                continue;
            }

            if (best is null || IsPreferred(candidate, best))
            {
                best = candidate;
            }
        }

        return best;
    }

    /// <summary>
    /// Determines whether <paramref name="candidate"/> is preferred over
    /// <paramref name="incumbent"/>: finer resolution wins; on equal
    /// resolution the later issuance wins; a dated candidate beats an undated
    /// one. On a full tie (equal resolution and issuance) the ordinally-smaller
    /// <see cref="S104TideCandidate.DatasetId"/> wins, so selection is stable
    /// regardless of the order datasets were enumerated.
    /// </summary>
    private static bool IsPreferred(S104TideCandidate candidate, S104TideCandidate incumbent)
    {
        if (candidate.SpacingDegrees < incumbent.SpacingDegrees)
        {
            return true;
        }

        if (candidate.SpacingDegrees > incumbent.SpacingDegrees)
        {
            return false;
        }

        switch (candidate.IssueDate, incumbent.IssueDate)
        {
            case ({ } c, { } i) when c != i:
                return c > i;
            case (not null, null):
                return true;
            case (null, not null):
                return false;
            default:
                // Equal resolution and issuance: break the tie deterministically
                // on the dataset id so a true tie never depends on input order.
                return string.CompareOrdinal(candidate.DatasetId, incumbent.DatasetId) < 0;
        }
    }

    /// <summary>
    /// Applies the "assume + warn" datum policy: a mismatch is flagged only for
    /// an S-102 base, when the base and tide datums cannot be confirmed as the
    /// same known register code.
    /// </summary>
    private static bool IsDatumMismatch(BaseDepthResult baseDepth, S104TideCandidate tide)
    {
        if (baseDepth.Source != BaseDepthSource.Bathymetry)
        {
            return false;
        }

        // Warn when either datum is unknown (cannot confirm alignment) or the
        // known codes differ.
        return baseDepth.VerticalDatumCode is not { } baseDatum
            || tide.VerticalDatumCode is not { } tideDatum
            || baseDatum != tideDatum;
    }
}
