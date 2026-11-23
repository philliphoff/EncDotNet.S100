using System.Globalization;
using EncDotNet.S100.DataModel;

namespace EncDotNet.S100.Tides.Stations.Mirror;

/// <summary>
/// Checks a newly downloaded catalogue before a mirror publishes it, so a
/// bad day at the provider (a short station list, missing constants) doesn't
/// replace a good catalogue with a broken one.
/// </summary>
/// <remarks>
/// A catalogue fails when it's undated or empty, from another provider than
/// expected, older than the published one, or when its stations, the tide
/// stations it can predict on MLLW, or its current stations or those it can
/// predict fall by more than <see cref="DefaultMaxDropFraction"/> from the
/// published catalogue's. Stations removed by the provider count against
/// the same limit.
/// </remarks>
public static class TideCataloguePublishCheck
{
    /// <summary>The largest share of stations a new catalogue may lose: 5%.</summary>
    public const double DefaultMaxDropFraction = 0.05;

    /// <summary>Finds what's wrong with a catalogue a mirror is about to publish.</summary>
    /// <param name="previous">The catalogue the mirror publishes now; <c>null</c> on the first publication.</param>
    /// <param name="candidate">The catalogue to publish.</param>
    /// <param name="expectedProvider">The provider the catalogue must name, such as <c>NOAA CO-OPS</c>; <c>null</c> for any.</param>
    /// <param name="maxDropFraction">The largest share of stations it may lose, from 0 to 1.</param>
    /// <returns>Each problem, in a sentence; empty when the catalogue may be published.</returns>
    public static IReadOnlyList<string> FindProblems(
        TideStationCatalogue? previous,
        TideStationCatalogue candidate,
        string? expectedProvider = null,
        double maxDropFraction = DefaultMaxDropFraction)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentOutOfRangeException.ThrowIfNegative(maxDropFraction);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxDropFraction, 1);

        var problems = new List<string>();
        if (candidate.Retrieved is not { } retrieved)
        {
            problems.Add("The catalogue doesn't say when its stations were retrieved.");
        }
        else if (previous?.Retrieved is { } was && retrieved < was)
        {
            problems.Add($"The catalogue was retrieved {Format(retrieved)}, before the published one ({Format(was)}).");
        }

        if (expectedProvider is not null && !string.Equals(candidate.Provider, expectedProvider, StringComparison.Ordinal))
        {
            problems.Add($"The catalogue's provider is '{candidate.Provider ?? "unknown"}', not '{expectedProvider}'.");
        }

        if (candidate.Stations.Count == 0)
        {
            problems.Add("The catalogue has no tide stations.");
        }

        if (previous is null)
        {
            return problems;
        }

        CheckDrop(problems, "tide stations", previous.Stations.Count, candidate.Stations.Count, maxDropFraction);
        CheckDrop(problems, "tide stations predictable on MLLW", PredictableOnMllw(previous), PredictableOnMllw(candidate), maxDropFraction);
        CheckDrop(problems, "current stations", previous.CurrentStations.Count, candidate.CurrentStations.Count, maxDropFraction);
        CheckDrop(problems, "predictable current stations", PredictableCurrents(previous), PredictableCurrents(candidate), maxDropFraction);

        var comparison = TideCatalogueComparison.Compare(previous, candidate);
        CheckRemoved(problems, "tide stations", comparison.RemovedStationIds.Count, previous.Stations.Count, maxDropFraction);
        CheckRemoved(problems, "current stations", comparison.RemovedCurrentStationIds.Count, previous.CurrentStations.Count, maxDropFraction);
        return problems;
    }

    private static int PredictableOnMllw(TideStationCatalogue catalogue) =>
        catalogue.Stations.Count(s => catalogue.TryCreatePrediction(s.Id, VerticalDatum.MeanLowerLowWater) is not null);

    private static int PredictableCurrents(TideStationCatalogue catalogue) =>
        catalogue.CurrentStations.Count(s => catalogue.TryCreateCurrentPrediction(s.Id) is not null);

    private static void CheckDrop(List<string> problems, string what, int previous, int candidate, double maxDropFraction)
    {
        if (candidate < previous * (1 - maxDropFraction))
        {
            problems.Add($"The catalogue has {candidate} {what}, down from {previous}: more than {Percent(maxDropFraction)} fewer.");
        }
    }

    private static void CheckRemoved(List<string> problems, string what, int removed, int previous, double maxDropFraction)
    {
        if (removed > 0 && removed > previous * maxDropFraction)
        {
            problems.Add($"{removed} of the published catalogue's {previous} {what} are missing: more than {Percent(maxDropFraction)}.");
        }
    }

    private static string Percent(double fraction) => (fraction * 100).ToString("0.#", CultureInfo.InvariantCulture) + "%";

    private static string Format(DateTimeOffset time) => time.UtcDateTime.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);
}
