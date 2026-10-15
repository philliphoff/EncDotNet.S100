using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.S101;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Services.Depth;

/// <summary>
/// Orchestrates the location depth-assimilation pipeline against the currently
/// loaded datasets: it live-samples S-102 bathymetry, the nearest S-101
/// sounding and every overlapping S-104 tide grid at a picked point, then runs
/// the <see cref="WaterLandClassifier"/>, <see cref="BaseDepthResolver"/> and
/// <see cref="DepthAssimilationService"/> to produce a single
/// <see cref="LocationDepthResult"/> and a water/land classification.
/// </summary>
/// <remarks>
/// Where S-101 cells overlap, only the finest cells whose <c>DataCoverage</c>
/// contains the point supply the base depth (area hits and the nearest
/// sounding), ranked with <see cref="CellScaleRanking"/> as the display ranks
/// them; among cells of that scale the shoalest area wins. This is the same rule
/// the passage check's depth source applies (issue #938).
/// </remarks>
internal sealed class LocationDepthProbe
{
    private readonly WaterLandClassifier _classifier = new();
    private readonly BaseDepthResolver _baseResolver = new();
    private readonly DepthAssimilationService _assimilation = new();

    /// <summary>
    /// Result of probing a picked location: the water/land classification (which
    /// gates card visibility) and the assimilated depth result, if any.
    /// </summary>
    /// <param name="Class">The water/land classification at the point.</param>
    /// <param name="Depth">
    /// The assimilated depth result, or <c>null</c> when no base depth could be
    /// resolved (no bathymetry, area or sounding overlaps the point).
    /// </param>
    public readonly record struct Probe(WaterLandClass Class, LocationDepthResult? Depth);

    /// <summary>
    /// Probes the loaded datasets at a WGS-84 location.
    /// </summary>
    /// <param name="processors">The loaded dataset processors keyed by entry.</param>
    /// <param name="hits">The resolved pick hits (searched for S-101 area depths).</param>
    /// <param name="latitude">Pick latitude in decimal degrees (WGS-84).</param>
    /// <param name="longitude">Pick longitude in decimal degrees (WGS-84).</param>
    /// <returns>The probe outcome.</returns>
    public Probe Evaluate(
        IEnumerable<KeyValuePair<DatasetEntry, IDatasetProcessor>> processors,
        IReadOnlyList<PickHit> hits,
        double latitude,
        double longitude)
    {
        ArgumentNullException.ThrowIfNull(processors);
        ArgumentNullException.ThrowIfNull(hits);

        S102DepthSample? bathymetry = null;
        string? bathymetryDatasetId = null;
        S101SoundingSample? nearestSounding = null;
        string? soundingDatasetId = null;
        var tideCandidates = new List<S104TideCandidate>();
        var entries = processors as IReadOnlyCollection<KeyValuePair<DatasetEntry, IDatasetProcessor>> ?? processors.ToList();
        var finest = FinestCoveringCells(entries.Select(p => p.Value).OfType<S101DatasetProcessor>(), latitude, longitude);

        foreach (var (entry, processor) in entries)
        {
            switch (processor)
            {
                case S102DatasetProcessor s102 when bathymetry is null:
                    if (TrySampleBathymetry(s102, latitude, longitude) is { } sample)
                    {
                        bathymetry = sample;
                        bathymetryDatasetId = entry.DisplayName;
                    }
                    break;

                case S101DatasetProcessor s101 when finest is null || finest.Contains(s101):
                    var sounding = SafeSampleSounding(s101, latitude, longitude);
                    if (sounding is { } s && (nearestSounding is not { } best || s.DistanceMeters < best.DistanceMeters))
                    {
                        nearestSounding = s;
                        soundingDatasetId = entry.DisplayName;
                    }
                    break;

                case S104DatasetProcessor s104:
                    if (SafeSampleTide(s104, latitude, longitude) is { } probe)
                    {
                        tideCandidates.Add(new S104TideCandidate(
                            entry.DisplayName,
                            probe.SpacingDegrees,
                            probe.IssueDate,
                            probe.VerticalDatumCode,
                            probe.Series));
                    }
                    break;
            }
        }

        var waterLand = _classifier.Classify(hits, s102CoversPoint: bathymetry is not null);
        var depthHits = finest is null
            ? hits
            : hits.Where(h => h.OwningProcessor is not S101DatasetProcessor s101 || finest.Contains(s101)).ToList();
        var baseDepth = _baseResolver.Resolve(
            bathymetry, depthHits, nearestSounding, bathymetryDatasetId, soundingDatasetId);
        var result = _assimilation.Assimilate(baseDepth, tideCandidates);

        return new Probe(waterLand, result);
    }

    /// <summary>
    /// The S-101 cells at the finest scale among those covering the point, or
    /// <see langword="null"/> when none covers it (no cell is then preferred).
    /// </summary>
    internal static HashSet<S101DatasetProcessor>? FinestCoveringCells(
        IEnumerable<S101DatasetProcessor> cells, double latitude, double longitude)
    {
        var covering = new List<(S101DatasetProcessor Cell, int? Rank)>();
        foreach (var cell in cells)
        {
            try
            {
                if (cell.CoversPoint(latitude, longitude))
                {
                    covering.Add((cell, cell.CellRankingScale));
                }
            }
            catch
            {
                // A cell whose features can't be read takes no part in the ranking.
            }
        }

        return covering.Count == 0
            ? null
            : CellScaleRanking.FinestBand(covering, c => c.Rank).Select(c => c.Cell).ToHashSet();
    }

    private static S102DepthSample? TrySampleBathymetry(S102DatasetProcessor processor, double latitude, double longitude)
    {
        try
        {
            return processor.SampleBaseDepth(latitude, longitude) is { } p
                ? new S102DepthSample(p.DepthMetres, p.UncertaintyMetres, p.VerticalDatumCode)
                : null;
        }
        catch
        {
            // Malformed grids can throw during sampling; treat as "no coverage".
            return null;
        }
    }

    private static S101SoundingSample? SafeSampleSounding(S101DatasetProcessor processor, double latitude, double longitude)
    {
        try
        {
            return processor.SampleNearestSounding(latitude, longitude);
        }
        catch
        {
            return null;
        }
    }

    private static S104TideProbe? SafeSampleTide(S104DatasetProcessor processor, double latitude, double longitude)
    {
        try
        {
            return processor.SampleTide(latitude, longitude, from: null, to: null);
        }
        catch
        {
            return null;
        }
    }
}
