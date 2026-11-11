using EncDotNet.S100.DataModel;

namespace EncDotNet.S100.Navigation;

// The navigational-hazards check (#1127): the features other than dangers
// whose S-101 portrayal raises the NavHazard alert (RouteChartFeatureKind.Alerted),
// such as buoys, beacons, piles, platforms and bridges.
//
// 1. A feature counts when its portrayal raises NavHazard at the check's
//    depths; RouteHazardClasses gives its class. Dangers (wrecks,
//    obstructions, rocks) keep their own depth rules (ChartCheck.Dangers.cs).
// 2. Its position is the true distance from the leg's centreline, as for a
//    danger: within the track clearance it's on the track, inside the side's
//    limit it's in the corridor, within the near band beyond it it's near.
// 3. The group follows the class and position: a structure on the track
//    must be reviewed; aids, structures elsewhere in the corridor and
//    overhead features are worth checking; anything near is information.
// 4. Only the finest cell counts (#947), and a hazard near the turn between
//    two legs is reported once, on the leg it's closest to.
public sealed partial class ChartCheck
{
    /// <summary>A hazard found near a leg, kept until every leg is swept.</summary>
    private sealed record HazardCandidate(
        RouteChartFeature Feature,
        int LegIndex,
        double Distance,
        double Offset,
        GeoPosition Position,
        RouteFindingCode Code,
        RouteFindingGroup Group,
        HazardDetails Details);

    /// <summary>The hazards found along the route: the closest leg's view of each.</summary>
    private sealed class HazardList
    {
        private readonly Dictionary<(string DatasetId, string FeatureId), HazardCandidate> _candidates = new();
        private readonly List<(string DatasetId, string FeatureId)> _order = new();

        public void Offer(HazardCandidate candidate)
        {
            var key = (candidate.Feature.Cell.DatasetId, candidate.Feature.FeatureId);
            if (!_candidates.TryGetValue(key, out var existing))
            {
                _order.Add(key);
                _candidates[key] = candidate;
            }
            else if (Math.Abs(candidate.Offset) < Math.Abs(existing.Offset))
            {
                _candidates[key] = candidate;
            }
        }

        public void AddTo(FindingList findings)
        {
            foreach (var key in _order)
            {
                var c = _candidates[key];
                findings.Add(c.Code, c.LegIndex, c.Distance, RouteFindingLocation.Point(c.Position, c.Distance), c.Feature, c.Details, c.Group);
            }
        }
    }

    private sealed partial class LegSweep
    {
        private void CheckHazards(IReadOnlyList<RouteChartFeature> features)
        {
            foreach (var feature in features)
            {
                if (feature.Kind != RouteChartFeatureKind.Alerted
                    || !feature.Alerts.Any(a => a.Alert == RouteFindingAlert.NavHazard))
                {
                    continue;
                }

                _cancellationToken.ThrowIfCancellationRequested();
                // Dangers and soundings have their own depth rules.
                var hazardClass = RouteHazardClasses.Of(feature.FeatureType);
                if (hazardClass == RouteHazardClass.Danger)
                {
                    continue;
                }

                var (projection, position) = Nearest(feature);
                var limit = projection.Offset > 0 ? _port : _starboard;
                var offset = Math.Abs(projection.Offset);
                if (offset > limit + _nearby || !InBand(_cellIndex[feature.Cell], Band(position)))
                {
                    continue;
                }

                var where = offset <= _trackClearance ? RouteFindingPosition.OnTrack
                    : offset <= limit ? RouteFindingPosition.InCorridor
                    : RouteFindingPosition.Near;
                var (code, group) = Classify(hazardClass, where);
                var details = new HazardDetails
                {
                    FeatureType = feature.FeatureType,
                    HazardClass = hazardClass,
                    Position = where,
                    Side = offset < 0.5 ? RouteFindingSide.OnTrack : projection.Offset > 0 ? RouteFindingSide.Port : RouteFindingSide.Starboard,
                    Offset = Quantities.Length.FromMetres(offset),
                };
                _hazards.Offer(new HazardCandidate(feature, _legIndex, projection.Distance, projection.Offset, position, code, group, details));
            }
        }

        // Rule 3.
        private static (RouteFindingCode Code, RouteFindingGroup Group) Classify(RouteHazardClass hazardClass, RouteFindingPosition position) =>
            (hazardClass, position) switch
            {
                (_, RouteFindingPosition.Near) => (CodeOf(hazardClass), RouteFindingGroup.Info),
                (RouteHazardClass.AidToNavigation, _) => (RouteFindingCodes.AidToNavigation, RouteFindingGroup.Check),
                (RouteHazardClass.Overhead, _) => (RouteFindingCodes.OverheadOnRoute, RouteFindingGroup.Check),
                (_, RouteFindingPosition.OnTrack) => (RouteFindingCodes.StructureOnTrack, RouteFindingGroup.MustReview),
                _ => (RouteFindingCodes.StructureInCorridor, RouteFindingGroup.Check),
            };

        private static RouteFindingCode CodeOf(RouteHazardClass hazardClass) => hazardClass switch
        {
            RouteHazardClass.AidToNavigation => RouteFindingCodes.AidToNavigation,
            RouteHazardClass.Overhead => RouteFindingCodes.OverheadOnRoute,
            _ => RouteFindingCodes.StructureInCorridor,
        };
    }
}
