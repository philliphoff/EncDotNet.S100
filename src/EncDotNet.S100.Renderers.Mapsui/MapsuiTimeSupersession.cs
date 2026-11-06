using EncDotNet.S100.Datasets.Pipelines;

namespace EncDotNet.S100.Renderers.Mapsui;

/// <summary>
/// One time-aware dataset's step at a map time, for
/// <see cref="MapsuiTimeSupersession"/>.
/// </summary>
/// <param name="Id">The dataset's id.</param>
/// <param name="Supersession">Its <see cref="TimeSemantics.Supersession"/> key, or null.</param>
/// <param name="Kind">What its steps are.</param>
/// <param name="Sample">The step it would draw at the map time, or null for none.</param>
public readonly record struct MapsuiTimeCandidate(string Id, string? Supersession, TimeStepKind Kind, DateTime? Sample);

/// <summary>
/// The rule for datasets sharing a <see cref="TimeSemantics.Supersession"/> key
/// (design note <c>vector-weather.md</c> V30): of those with a step at the map
/// time, only one draws — the one whose step is latest; for the same step an
/// analysis or observation before a forecast; then the greater id, so the
/// choice is stable. The renderer and the panels that describe layer times
/// both use it, so they agree.
/// </summary>
public static class MapsuiTimeSupersession
{
    /// <summary>
    /// True when <paramref name="candidate"/> has a step but another of
    /// <paramref name="all"/> with the same key takes over from it.
    /// </summary>
    /// <param name="candidate">The dataset asked about.</param>
    /// <param name="all">Every time-aware dataset (it may include <paramref name="candidate"/>).</param>
    public static bool IsSuperseded(MapsuiTimeCandidate candidate, IEnumerable<MapsuiTimeCandidate> all)
    {
        ArgumentNullException.ThrowIfNull(all);
        if (candidate.Supersession is not { } key || candidate.Sample is not { } sample)
            return false;
        foreach (var rival in all)
        {
            if (rival.Sample is not { } other
                || !string.Equals(rival.Supersession, key, StringComparison.Ordinal)
                || string.Equals(rival.Id, candidate.Id, StringComparison.Ordinal))
            {
                continue;
            }

            if (other > sample)
                return true;
            if (other < sample)
                continue;
            var rank = Rank(rival.Kind).CompareTo(Rank(candidate.Kind));
            if (rank > 0 || (rank == 0 && string.CompareOrdinal(rival.Id, candidate.Id) > 0))
                return true;
        }

        return false;
    }

    /// <summary>
    /// How old <paramref name="sample"/> is at <paramref name="time"/>, to the
    /// nearest hour as the panels round a layer's offset, once past
    /// <paramref name="staleAfter"/>; else null (also when there is no limit or
    /// no sample).
    /// </summary>
    /// <param name="sample">The step drawn.</param>
    /// <param name="time">The map time.</param>
    /// <param name="staleAfter">The dataset's <see cref="TimeSemantics.StaleAfter"/>.</param>
    public static TimeSpan? StaleAge(DateTime? sample, DateTime time, TimeSpan? staleAfter)
    {
        if (sample is not { } drawn || staleAfter is not { } limit)
            return null;
        var age = time - drawn;
        return age > limit ? TimeSpan.FromHours(Math.Round(age.TotalHours, MidpointRounding.AwayFromZero)) : null;
    }

    /// <summary>
    /// How old a dataset issued at <paramref name="issued"/> is when looked at
    /// at <paramref name="time"/>, to the nearest hour, once past
    /// <paramref name="staleAfterIssue"/> (<see cref="TimeSemantics.StaleAfterIssue"/>);
    /// else null. The age is taken at <paramref name="time"/>, or at
    /// <paramref name="now"/> when that's earlier, so looking ahead through a
    /// forecast doesn't make the dataset older than it is.
    /// </summary>
    /// <param name="issued">When the dataset was issued, if known.</param>
    /// <param name="time">The map time.</param>
    /// <param name="now">The host's clock, or null to take the age at the map time.</param>
    /// <param name="staleAfterIssue">The limit.</param>
    public static TimeSpan? StaleAgeFromIssue(DateTime? issued, DateTime time, DateTime? now, TimeSpan? staleAfterIssue)
    {
        if (issued is not { } issue || staleAfterIssue is not { } limit)
            return null;
        var at = now is { } clock && clock < time ? clock : time;
        var age = at - issue;
        return age > limit ? TimeSpan.FromHours(Math.Round(age.TotalHours, MidpointRounding.AwayFromZero)) : null;
    }

    private static int Rank(TimeStepKind kind) => kind == TimeStepKind.Forecast ? 0 : 1;
}
