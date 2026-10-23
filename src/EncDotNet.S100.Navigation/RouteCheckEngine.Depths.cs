using EncDotNet.S100.DataModel;
using EncDotNet.S100.Quantities;

namespace EncDotNet.S100.Navigation;

// The safety-contour and safety-depth checks (#955).
public sealed partial class RouteCheckEngine
{
    private sealed partial class LegSweep
    {
        // The depth checks, run when the options carry depths. The features
        // reach DangerNearbyMeters beyond the corridor.
        private void CheckDepths(IReadOnlyList<RouteChartFeature> features, LegDepths depths)
        {
            var shallow = CheckSafetyContour(features, depths);
            CheckSoundings(features, depths, shallow);
            CheckBathymetry(depths);
            CheckDangers(features, depths);
        }

        // Depth, dredged and drying areas shallower than the safety contour
        // are one kind of area, like the safety contour an ECDIS draws
        // (DEPCNT03): the contour is the edge of the water shallower than it,
        // so the track crossing it, or such water reaching into the
        // corridor, is what matters. Drying ground is shallower than any
        // contour, so a bank with a drying core is one span. Returns the
        // shallow areas, for the soundings check.
        private List<RouteChartFeature> CheckSafetyContour(IReadOnlyList<RouteChartFeature> features, LegDepths depths)
        {
            var contour = depths.SafetyContourMeters;
            var shallow = new List<RouteChartFeature>();
            foreach (var feature in features)
            {
                if (feature.Kind is RouteChartFeatureKind.DepthArea or RouteChartFeatureKind.DredgedArea or RouteChartFeatureKind.DryingArea
                    && feature.DepthMeters is { } depth && depth < contour)
                {
                    shallow.Add(feature.Kind == RouteChartFeatureKind.DepthArea ? feature : feature with { Kind = RouteChartFeatureKind.DepthArea });
                }
            }

            if (shallow.Count > 0)
            {
                CheckAreas(shallow, RouteChartFeatureKind.DepthArea, RouteFindingCodes.CrossesSafetyContour, RouteFindingCodes.SafetyContourInCorridor,
                    f => new ShoalDetails
                    {
                        LeastDepth = Depth.FromMetres(f.DepthMeters!.Value),
                        ThresholdKind = RouteFindingThreshold.SafetyContour,
                        Threshold = Depth.FromMetres(contour),
                    });
            }

            return shallow;
        }

        // Soundings shallower than the safety depth inside the cross-track
        // limits, from the finest cell where they are. A sounding inside an
        // area already shallower than the safety contour adds nothing to
        // that area's finding, so only soundings in deeper water count.
        // Soundings closer together along the route than
        // ShoalMergeGapMeters make one finding.
        private void CheckSoundings(IReadOnlyList<RouteChartFeature> features, LegDepths depths, List<RouteChartFeature> shallow)
        {
            var safetyDepth = depths.SafetyDepthMeters;
            var hits = new List<(double Distance, RouteChartFeature Feature)>();
            foreach (var feature in features)
            {
                if (feature.Kind != RouteChartFeatureKind.Sounding || feature.Points.Count == 0
                    || feature.DepthMeters is not { } depth || !(depth < safetyDepth))
                {
                    continue;
                }

                var position = feature.Points[0];
                var projection = Project(position);
                if (!InsideLimits(projection.Offset) || !InBand(_cellIndex[feature.Cell], Band(position)) || InShallowArea(position, shallow))
                {
                    continue;
                }

                hits.Add((projection.Distance, feature));
            }

            hits.Sort((a, b) => a.Distance.CompareTo(b.Distance));
            foreach (var group in Groups(hits, h => h.Distance, _engine._options.ShoalMergeGapMeters))
            {
                var start = group[0].Distance;
                var end = group[^1].Distance;
                var least = group.MinBy(h => h.Feature.DepthMeters!.Value).Feature;
                _findings.Add(RouteFindingCodes.SoundingsBelowSafetyDepth, _legIndex, start,
                    group.Count == 1 ? RouteFindingLocation.Point(least.Points[0], start) : Location(start, end),
                    least,
                    new ShoalDetails
                    {
                        LeastDepth = Depth.FromMetres(least.DepthMeters!.Value),
                        ThresholdKind = RouteFindingThreshold.SafetyDepth,
                        Threshold = Depth.FromMetres(safetyDepth),
                        SoundingCount = group.Count,
                    });
            }
        }

        private bool InShallowArea(GeoPosition position, List<RouteChartFeature> shallow)
        {
            IReadOnlyList<int>? band = null;
            foreach (var area in shallow)
            {
                if (PlanarGeometry.Contains(area.Surfaces, position) && InBand(_cellIndex[area.Cell], band ??= Band(position)))
                {
                    return true;
                }
            }

            return false;
        }

        // S-102 cells shallower than the safety contour, sampled the way the
        // passage check (#900) samples them: at half the finest grid cell
        // along the track, and across the corridor at the same spacing, so
        // no cell the corridor covers is stepped over. Samples on the track
        // make crossings (MUST REVIEW); samples only off it make corridor
        // findings (CHECK). Shoal samples closer together along the route
        // than ShoalMergeGapMeters make one finding. Chunks of the leg that
        // no S-102 surface overlaps aren't sampled.
        private void CheckBathymetry(LegDepths depths)
        {
            if (_engine._bathymetry is not { } bathymetry
                || bathymetry.FinestGridSpacingMeters(_extent) is not { } grid
                || !(grid > 0))
            {
                return;
            }

            var contour = depths.SafetyContourMeters;
            var options = _engine._options;
            var length = _stations[^1].Distance - _stations[0].Distance;
            var step = Math.Max(grid / 2, length / Math.Max(1, options.MaxStationsPerLeg));
            var across = Math.Clamp((int)Math.Ceiling((_port + _starboard) / step), 1, MaxSamplesAcross);
            var onTrack = new List<(double Distance, ChartedDepth Depth)>();
            var offTrack = new List<(double Distance, ChartedDepth Depth)>();
            var covered = new bool?[_chunks.Length];
            for (var k = 0; k < _stations.Count - 1; k++)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                var chunk = k / ChunkSize;
                var box = _chunks[chunk];
                covered[chunk] ??= bathymetry.FinestGridSpacingMeters(box) is not null;
                if (covered[chunk] != true)
                {
                    continue;
                }

                var a = _stations[k];
                var b = _stations[k + 1];
                var n = Math.Max(1, (int)Math.Ceiling((b.Distance - a.Distance) / step));
                for (var t = k == 0 ? 0 : 1; t <= n; t++)
                {
                    var u = (double)t / n;
                    var distance = Lerp(a.Distance, b.Distance, u);
                    var centre = Lerp(a.Centre, b.Centre, u);
                    if (Shoal(bathymetry, centre, contour) is { } depth)
                    {
                        onTrack.Add((distance, depth));
                        continue;
                    }

                    // From the port limit to the starboard limit.
                    var port = _port > 0 ? Lerp(a.Port, b.Port, u) : centre;
                    var starboard = _starboard > 0 ? Lerp(a.Starboard, b.Starboard, u) : centre;
                    ChartedDepth? shoalest = null;
                    for (var c = 0; c <= across; c++)
                    {
                        var f = (double)c / across;
                        if (Shoal(bathymetry, Lerp(port, starboard, f), contour) is { } d && (shoalest is null || d.DepthMeters < shoalest.DepthMeters))
                        {
                            shoalest = d;
                        }
                    }

                    if (shoalest is not null)
                    {
                        offTrack.Add((distance, shoalest));
                    }
                }
            }

            var gap = Math.Max(options.ShoalMergeGapMeters, 2 * step);
            var crossings = new List<(double Start, double End)>();
            foreach (var group in Groups(onTrack, h => h.Distance, gap))
            {
                var (start, end) = SampleSpan(group[0].Distance, group[^1].Distance, step);
                crossings.Add((start, end));
                AddBathymetry(RouteFindingCodes.CrossesS102Shoal, start, end, group, contour);
            }

            // Off-track shoals next to a crossing are the same shoal seen from the limits.
            offTrack.RemoveAll(h => crossings.Any(c => h.Distance >= c.Start - gap && h.Distance <= c.End + gap));
            foreach (var group in Groups(offTrack, h => h.Distance, gap))
            {
                var (start, end) = SampleSpan(group[0].Distance, group[^1].Distance, step);
                AddBathymetry(RouteFindingCodes.S102ShoalInCorridor, start, end, group, contour);
            }
        }

        private void AddBathymetry(RouteFindingCode code, double start, double end, List<(double Distance, ChartedDepth Depth)> samples, double contour)
        {
            var least = samples.MinBy(s => s.Depth.DepthMeters).Depth;
            _findings.Add(code, _legIndex, start, Location(start, end), feature: null, new ShoalDetails
            {
                LeastDepth = Depth.FromMetres(least.DepthMeters),
                ThresholdKind = RouteFindingThreshold.SafetyContour,
                Threshold = Depth.FromMetres(contour),
                Uncertainty = least.UncertaintyMeters is { } uncertainty ? Depth.FromMetres(uncertainty) : null,
                DatasetId = least.DatasetId,
                Cell = least.Reference,
            });
        }

        // A sample stands for half a step either side of it, within the leg.
        private (double Start, double End) SampleSpan(double first, double last, double step) =>
            (Math.Max(_stations[0].Distance, first - step / 2), Math.Min(_stations[^1].Distance, last + step / 2));

        private static ChartedDepth? Shoal(IChartedDepthSource bathymetry, GeoPosition position, double contour) =>
            bathymetry.SampleBathymetry(position) is { } depth && depth.DepthMeters < contour
                ? depth
                : null;

        // Runs of items, in order, whose keys are no more than gap apart.
        private static IEnumerable<List<T>> Groups<T>(List<T> items, Func<T, double> key, double gap)
        {
            List<T>? group = null;
            foreach (var item in items)
            {
                if (group is not null && key(item) - key(group[^1]) > gap)
                {
                    yield return group;
                    group = null;
                }

                (group ??= new List<T>()).Add(item);
            }

            if (group is not null)
            {
                yield return group;
            }
        }
    }

    // How many samples the S-102 check takes across a corridor at most.
    private const int MaxSamplesAcross = 64;
}
