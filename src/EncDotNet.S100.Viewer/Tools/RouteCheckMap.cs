using System.Globalization;
using Avalonia;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Viewer.Routing;
using EncDotNet.S100.Viewer.ViewModels;
using SkiaSharp;

namespace EncDotNet.S100.Viewer.Tools;

/// <summary>
/// The route-check map layer (#959 slice 3, handoff C1–C7): the corridor's
/// edges, the corridor fill on the selected finding's leg, a sleeve along the
/// route for each span finding, and a shaped marker for each point finding
/// and at each span's start. Built on the UI thread in geographic
/// coordinates; <see cref="RouteCheckMapLayout"/> places it on screen each
/// frame.
/// </summary>
/// <remarks>
/// <para>
/// The line keeps its UKC colours (decision 5): only route checks and S-124
/// warnings get sleeves and markers. The clearance check's own findings
/// (below the setting, confidence and data) are on the line already, so the
/// selected one keeps the #905 dashed outline instead.
/// </para>
/// <para>
/// A quiet map (decision 6): Info markers (and sleeves) only when selected;
/// one pill, on the selected finding.
/// </para>
/// </remarks>
/// <param name="Corridor">Each leg's port and starboard cross-track limits.</param>
/// <param name="Sleeves">Span findings, in list order.</param>
/// <param name="Marks">Point findings and span starts, in list order; Info only when selected.</param>
/// <param name="FilledLegs">The legs whose corridor is filled: the selected route finding's legs, else none.</param>
/// <param name="CorridorEdge">The edge colour (ink at 45 % Day, 55 % Dusk, 60 % Night).</param>
/// <param name="CorridorFill">The fill colour (accent at 8 % Day, 12 % Dusk and Night).</param>
internal sealed record RouteCheckMap(
    IReadOnlyList<RouteCheckMap.CorridorLeg> Corridor,
    IReadOnlyList<RouteCheckMap.Sleeve> Sleeves,
    IReadOnlyList<RouteCheckMap.Mark> Marks,
    IReadOnlyList<int> FilledLegs,
    SKColor CorridorEdge,
    SKColor CorridorFill)
{
    /// <summary>An empty layer.</summary>
    public static RouteCheckMap Empty { get; } = new([], [], [], [], SKColors.Transparent, SKColors.Transparent);

    /// <summary>A feature further than this from the route's centreline gets a leader (C5), in metres.</summary>
    internal const double LeaderMinMeters = 40;

    /// <summary>One leg's cross-track limits (C1).</summary>
    /// <param name="Index">The leg.</param>
    /// <param name="Port">The port limit, from the leg's start to its end.</param>
    /// <param name="Starboard">The starboard limit, from the leg's start to its end.</param>
    internal sealed record CorridorLeg(int Index, IReadOnlyList<GeoPosition> Port, IReadOnlyList<GeoPosition> Starboard);

    /// <summary>A span finding's band along the route (C2).</summary>
    internal sealed record Sleeve(string FindingId, IReadOnlyList<GeoPosition> Path, RouteFindingGroup Group, SKColor Tone, bool IsSelected);

    /// <summary>
    /// A finding's marker (C4): on the route at <paramref name="Anchor"/>,
    /// with a leader to <paramref name="Feature"/> when that is off the
    /// centreline (C5).
    /// </summary>
    /// <param name="FindingId">The finding.</param>
    /// <param name="Anchor">The nearest point on the route: the marker's stem stands on it.</param>
    /// <param name="Feature">The feature, when a leader runs to it; otherwise <c>null</c>.</param>
    /// <param name="Group">The finding's group: the marker's shape.</param>
    /// <param name="Tone">The group's tone.</param>
    /// <param name="IsSelected">Whether this is the selected finding (ring and pill, C7).</param>
    /// <param name="Pill">The pill text, "{title} · {HH:MM}", for the selected finding; otherwise <c>null</c>.</param>
    /// <param name="IsSpan">Whether the marker stands at a span's start.</param>
    internal sealed record Mark(
        string FindingId,
        GeoPosition Anchor,
        GeoPosition? Feature,
        RouteFindingGroup Group,
        SKColor Tone,
        bool IsSelected,
        string? Pill,
        bool IsSpan = false);

    /// <summary>The mark of the selected finding, or <c>null</c>.</summary>
    public Mark? SelectedMark => Marks.FirstOrDefault(m => m.IsSelected);

    /// <summary>The sleeve of the selected finding, or <c>null</c>.</summary>
    public Sleeve? SelectedSleeve => Sleeves.FirstOrDefault(s => s.IsSelected);

    /// <summary>
    /// Whether a finding is drawn by this layer (a route check or an S-124
    /// warning) rather than by the UKC line and the dashed outline.
    /// </summary>
    internal static bool Draws(RouteFinding finding) =>
        finding.Location is { Path.Count: > 0 } && RouteCheckVerdict.FilterOf(finding) != FindingsFilter.Clearance;

    /// <summary>Builds the layer for the checked route.</summary>
    /// <param name="route">The checked route; <c>null</c> draws nothing.</param>
    /// <param name="findings">The findings list.</param>
    /// <param name="selected">The selected finding, or <c>null</c>.</param>
    /// <param name="colors">The state colours.</param>
    /// <param name="accent">The route accent, for the corridor fill.</param>
    /// <param name="palette">The chart palette, for the corridor's opacities.</param>
    /// <param name="viewTime">The time ETAs run from when the route has no departure.</param>
    /// <param name="timeFormat">The pill's time format.</param>
    /// <param name="defaultHalfWidthMeters">The corridor's half-width on a side with no cross-track limit.</param>
    public static RouteCheckMap Build(
        Route? route,
        IReadOnlyList<RouteFinding>? findings,
        RouteFinding? selected,
        RouteCheckColors colors,
        SKColor accent,
        PaletteType palette,
        DateTimeOffset viewTime,
        TimeFormat timeFormat,
        double defaultHalfWidthMeters)
    {
        if (route is null || route.Legs.Count == 0)
            return Empty;

        var track = RouteTrack.For(route, viewTime);
        var corridor = new CorridorLeg[route.Legs.Count];
        var halfWidths = new (double Port, double Starboard)[route.Legs.Count];
        for (var i = 0; i < corridor.Length; i++)
        {
            var port = Math.Max(0, route.GetEffectiveLegValue(i, RoutePassageField.PortCrossTrack) ?? defaultHalfWidthMeters);
            var starboard = Math.Max(0, route.GetEffectiveLegValue(i, RoutePassageField.StarboardCrossTrack) ?? defaultHalfWidthMeters);
            halfWidths[i] = (port, starboard);
            var path = track.LegPath(i);
            corridor[i] = new CorridorLeg(i, Offset(path, -port), Offset(path, starboard));
        }

        var sleeves = new List<Sleeve>();
        var marks = new List<Mark>();
        var filled = new List<int>();
        foreach (var finding in findings ?? [])
        {
            if (!Draws(finding))
                continue;
            var location = finding.Location!;
            var isSelected = finding.Id == selected?.Id;
            var tone = RouteCheckOverlay.ToneOf(finding.Group, colors);
            if (isSelected)
            {
                var first = finding.LegIndex ?? track.LegAt(location.StartDistanceMeters);
                var last = location.IsPoint ? first : Math.Max(first, track.LegAt(location.EndDistanceMeters));
                for (var leg = first; leg <= last && leg < corridor.Length; leg++)
                    filled.Add(leg);
            }

            // Info only when selected (C4), its sleeve too.
            if (finding.Group == RouteFindingGroup.Info && !isSelected)
                continue;

            if (!location.IsPoint)
                sleeves.Add(new Sleeve(finding.Id, location.Path, finding.Group, tone, isSelected));

            GeoPosition anchor;
            GeoPosition? feature = null;
            if (location.IsPoint)
            {
                var distance = finding.Distance?.TotalMetres ?? location.StartDistanceMeters;
                anchor = track.PositionAt(distance);
                var at = location.Path[0];
                var offset = PassageGeodesy.DistanceMeters(anchor, at, LegGeometry.GreatCircle);
                var leg = halfWidths[Math.Clamp(track.LegAt(distance), 0, halfWidths.Length - 1)];
                // Beyond the corridor, or more than 40 m off the centreline (C5).
                if (offset > Math.Min(LeaderMinMeters, Math.Max(leg.Port, leg.Starboard)))
                    feature = at;
            }
            else
            {
                anchor = location.Path[0];
            }

            var pill = isSelected ? PillText(finding, track, timeFormat) : null;
            marks.Add(new Mark(finding.Id, anchor, feature, finding.Group, tone, isSelected, pill, !location.IsPoint));
        }

        var dark = palette != PaletteType.Day;
        var edge = colors.Ink.WithAlpha((byte)Math.Round(255 * (palette switch
        {
            PaletteType.Night => 0.60,
            PaletteType.Dusk => 0.55,
            _ => 0.45,
        })));
        var fill = accent.WithAlpha((byte)Math.Round(255 * (dark ? 0.12 : 0.08)));
        return new RouteCheckMap(corridor, sleeves, marks, filled, edge, fill);
    }

    // "Wreck 9,4 m · 07:52": the finding's title and, when known, the ETA there.
    private static string PillText(RouteFinding finding, RouteTrack track, TimeFormat timeFormat)
    {
        DateTimeOffset? eta = finding.Details is WarningDetails { AtEta: true } warning
            ? warning.From
            : track.EtaAt(finding.Distance?.TotalMetres ?? finding.Location!.StartDistanceMeters);
        return eta is { } t
            ? string.Format(CultureInfo.CurrentCulture, "{0} · {1}", finding.Title, PassageFormatting.FormatEta(t, track.Departure, timeFormat))
            : finding.Title;
    }

    // The path moved sideways by `meters` (negative to port), each point
    // along the local course.
    private static IReadOnlyList<GeoPosition> Offset(IReadOnlyList<GeoPosition> path, double meters)
    {
        var result = new GeoPosition[path.Count];
        for (var k = 0; k < path.Count; k++)
        {
            var (a, b) = k + 1 < path.Count ? (path[k], path[k + 1]) : (path[k - 1], path[k]);
            var bearing = Geodesy.MarineGeodesy.RhumbBearingDegrees(a.Latitude, a.Longitude, b.Latitude, b.Longitude);
            result[k] = Move(path[k], bearing + 90, meters);
        }

        return result;
    }

    // A point `meters` from `p` on `bearingDegrees` (short distances, spherical).
    private static GeoPosition Move(GeoPosition p, double bearingDegrees, double meters)
    {
        const double earthRadius = 6371008.8;
        var theta = bearingDegrees * Math.PI / 180;
        var dLat = meters * Math.Cos(theta) / earthRadius * 180 / Math.PI;
        var cosLat = Math.Max(1e-6, Math.Cos(p.Latitude * Math.PI / 180));
        var dLon = meters * Math.Sin(theta) / (earthRadius * cosLat) * 180 / Math.PI;
        return new GeoPosition(p.Latitude + dLat, p.Longitude + dLon);
    }

    /// <summary>The route's legs by distance from departure, as the route checks measure them.</summary>
    private sealed class RouteTrack
    {
        private readonly IReadOnlyList<PassageLeg> _legs;
        private readonly PassageSchedule _schedule;
        private readonly bool _hasEtas;

        private RouteTrack(IReadOnlyList<PassageLeg> legs, PassageSchedule schedule, bool hasEtas)
        {
            _legs = legs;
            _schedule = schedule;
            _hasEtas = hasEtas;
        }

        public DateTimeOffset Departure => _schedule.Departure;

        public static RouteTrack For(Route route, DateTimeOffset viewTime)
        {
            var etas = RouteEtas.Compute(route, viewTime);
            return new RouteTrack(RouteEtas.ToPassageLegs(route), etas.Schedule, etas.HasEtas);
        }

        public IReadOnlyList<GeoPosition> LegPath(int index)
        {
            var leg = _legs[index];
            if (leg.Geometry != LegGeometry.GreatCircle)
                return leg.Points;
            var steps = RouteOverlayScene.GeodesicSegments;
            var path = new GeoPosition[steps + 1];
            for (var k = 0; k <= steps; k++)
                path[k] = PassageGeodesy.Interpolate(leg.Points[0], leg.Points[^1], (double)k / steps, leg.Geometry);
            return path;
        }

        // The earlier leg at a waypoint, as the route checks do.
        public int LegAt(double distance)
        {
            for (var i = _schedule.Legs.Count - 1; i > 0; i--)
            {
                if (distance > _schedule.Legs[i].StartDistanceMeters)
                    return i;
            }

            return 0;
        }

        public GeoPosition PositionAt(double distance)
        {
            var index = LegAt(distance);
            var leg = _schedule.Legs[index];
            var fraction = leg.DistanceMeters > 0 ? Math.Clamp((distance - leg.StartDistanceMeters) / leg.DistanceMeters, 0, 1) : 0;
            var points = _legs[index].Points;
            return PassageGeodesy.Interpolate(points[0], points[^1], fraction, _legs[index].Geometry);
        }

        public DateTimeOffset? EtaAt(double distance)
        {
            if (!_hasEtas)
                return null;
            var index = LegAt(distance);
            return _schedule.EtaAt(index, distance - _schedule.Legs[index].StartDistanceMeters);
        }
    }
}

/// <summary>
/// Places a <see cref="RouteCheckMap"/>'s markers on screen (C4, C6): each
/// marker 19 px above its anchor on a stem, and markers within 24 px of each
/// other merged into a cluster. The renderer draws the result and the map
/// hit-tests it.
/// </summary>
internal static class RouteCheckMapLayout
{
    /// <summary>A marker's size, in pixels.</summary>
    public const float MarkerSize = 18f;

    /// <summary>How far a marker stands above the route, in pixels (18–20 px).</summary>
    public const float StemLength = 19f;

    /// <summary>Markers closer than this merge into a cluster, in pixels.</summary>
    public const float ClusterDistance = 24f;

    /// <summary>The selected marker's ring radius, in pixels.</summary>
    public const float SelectedRingRadius = 15f;

    /// <summary>A marker on screen.</summary>
    /// <param name="Anchor">The stem's foot, on the route.</param>
    /// <param name="Center">The marker's centre.</param>
    /// <param name="Feature">The leader's far end, or <c>null</c>.</param>
    /// <param name="Mark">The mark.</param>
    internal sealed record PlacedMarker(Point Anchor, Point Center, Point? Feature, RouteCheckMap.Mark Mark);

    /// <summary>Markers merged into one (C6): the worst group's shape, glyph = count.</summary>
    internal sealed record Cluster(Point Center, RouteFindingGroup Group, SKColor Tone, IReadOnlyList<PlacedMarker> Members);

    /// <summary>The markers and clusters on screen. The selected marker is never clustered.</summary>
    internal sealed record Placement(IReadOnlyList<PlacedMarker> Markers, IReadOnlyList<Cluster> Clusters)
    {
        public static Placement Empty { get; } = new([], []);
    }

    /// <summary>Places <paramref name="map"/>'s markers for the viewport.</summary>
    public static Placement Place(RouteCheckMap map, Func<GeoPosition, Point?> toScreen)
    {
        if (map.Marks.Count == 0)
            return Placement.Empty;

        var placed = new List<PlacedMarker>(map.Marks.Count);
        foreach (var mark in map.Marks)
        {
            if (toScreen(mark.Anchor) is not { } a)
                continue;
            var feature = mark.Feature is { } f ? toScreen(f) : null;
            placed.Add(new PlacedMarker(a, new Point(a.X, a.Y - StemLength), feature, mark));
        }

        // Worst first, then in list order, so a cluster's seed is its worst marker.
        var order = placed
            .Select((m, i) => (Marker: m, Index: i))
            .Where(x => !x.Marker.Mark.IsSelected)
            .OrderBy(x => x.Marker.Mark.Group)
            .ThenBy(x => x.Index)
            .Select(x => x.Marker)
            .ToList();
        var singles = new List<PlacedMarker>();
        var clusters = new List<Cluster>();
        var taken = new bool[order.Count];
        const double limit = ClusterDistance * ClusterDistance;
        for (var i = 0; i < order.Count; i++)
        {
            if (taken[i])
                continue;
            taken[i] = true;
            var members = new List<PlacedMarker> { order[i] };
            for (var j = i + 1; j < order.Count; j++)
            {
                if (taken[j])
                    continue;
                var dx = order[j].Center.X - order[i].Center.X;
                var dy = order[j].Center.Y - order[i].Center.Y;
                if ((dx * dx) + (dy * dy) <= limit)
                {
                    taken[j] = true;
                    members.Add(order[j]);
                }
            }

            if (members.Count == 1)
            {
                singles.Add(order[i]);
                continue;
            }

            var center = new Point(members.Average(m => m.Center.X), members.Average(m => m.Center.Y));
            clusters.Add(new Cluster(center, order[i].Mark.Group, order[i].Mark.Tone, members));
        }

        // The selected marker last, so it draws on top.
        singles.Reverse();
        singles.AddRange(placed.Where(m => m.Mark.IsSelected));
        return new Placement(singles, clusters);
    }

    /// <summary>What a click at a point hits: a marker, a cluster, or nothing.</summary>
    internal readonly record struct Hit(PlacedMarker? Marker, Cluster? Cluster);

    /// <summary>The marker or cluster under <paramref name="point"/>, the selected marker first.</summary>
    public static Hit? HitTest(Placement placement, Point point)
    {
        const double radius = (MarkerSize / 2) + 3;
        static bool Near(Point a, Point b) => ((a.X - b.X) * (a.X - b.X)) + ((a.Y - b.Y) * (a.Y - b.Y)) <= radius * radius;

        for (var i = placement.Markers.Count - 1; i >= 0; i--)
        {
            if (Near(placement.Markers[i].Center, point))
                return new Hit(placement.Markers[i], null);
        }

        foreach (var cluster in placement.Clusters)
        {
            if (Near(cluster.Center, point))
                return new Hit(null, cluster);
        }

        return null;
    }
}
