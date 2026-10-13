using System.Globalization;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Viewer.Geodesy;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Routing;
using SkiaSharp;

namespace EncDotNet.S100.Viewer.Tools;

/// <summary>
/// An immutable snapshot of everything the route overlay draws: each route's
/// waypoints and leg paths, the selection, whether the editor is on, and the
/// palette's colours. Built on the UI thread from the live
/// <see cref="RouteCollection"/>, then read by <see cref="RouteOverlayRenderer"/>
/// on the render thread, which lays it out for the current viewport each frame.
/// </summary>
/// <param name="Routes">Every route, in collection order.</param>
/// <param name="SelectedWaypointIndex">The active route's selected waypoint, or <c>null</c>.</param>
/// <param name="SelectedLegIndex">The active route's selected leg, or <c>null</c>.</param>
/// <param name="IsEditing">Whether the route editor is on: the active route gets the editing style.</param>
/// <param name="Colors">The colours for the current accent and chart palette.</param>
internal sealed record RouteOverlayScene(
    IReadOnlyList<RouteOverlayScene.SceneRoute> Routes,
    int? SelectedWaypointIndex,
    int? SelectedLegIndex,
    bool IsEditing,
    RouteOverlayColors Colors)
{
    /// <summary>Number of straight segments approximating a great-circle leg.</summary>
    internal const int GeodesicSegments = 24;

    /// <summary>An empty scene.</summary>
    public static RouteOverlayScene Empty { get; } =
        new([], null, null, false, RouteOverlayColors.For(MeasureOverlayAppearance.Default));

    /// <summary>The active route, or <c>null</c>.</summary>
    public SceneRoute? ActiveRoute => Routes.FirstOrDefault(r => r.IsActive);

    /// <summary>One route as drawn.</summary>
    /// <param name="IsActive">Whether this is the collection's active route.</param>
    /// <param name="Waypoints">Waypoint positions, in route order.</param>
    /// <param name="Legs">Legs, in route order; leg <c>i</c> joins waypoint <c>i</c> to <c>i+1</c>.</param>
    internal sealed record SceneRoute(bool IsActive, IReadOnlyList<GeoPosition> Waypoints, IReadOnlyList<SceneLeg> Legs)
    {
        /// <summary>Snapshots <paramref name="route"/>.</summary>
        public static SceneRoute From(Route route, bool isActive)
        {
            var waypoints = route.Waypoints.Select(w => w.Position).ToArray();
            var legs = new SceneLeg[route.Legs.Count];
            // Durations need only each leg's speed, so any departure will do.
            var etas = legs.Length > 0 ? RouteEtas.Compute(route, DateTimeOffset.UnixEpoch) : null;
            for (var i = 0; i < legs.Length; i++)
            {
                var a = waypoints[i];
                var b = waypoints[i + 1];
                IReadOnlyList<GeoPosition> path = route.Legs[i].GeometryType == RouteLegGeometryType.Geodesic
                    ? MarineGeodesy.GreatCircleIntermediatePoints(a.Latitude, a.Longitude, b.Latitude, b.Longitude, GeodesicSegments)
                    : [a, b];
                var metrics = route.ComputeLegMetrics(i);
                legs[i] = new SceneLeg(path, LegLabel(metrics.DistanceNm, metrics.InitialBearingDegrees, etas!.LegDuration(i)));
            }
            return new SceneRoute(isActive, waypoints, legs);
        }
    }

    /// <summary>One leg as drawn.</summary>
    /// <param name="Path">The leg's path from its start to its end waypoint:
    /// the two endpoints for a rhumb line, a densified arc for a great circle.</param>
    /// <param name="Label">The leg label, e.g. <c>2,5 NM · 320° · 12 min</c>.</param>
    internal sealed record SceneLeg(IReadOnlyList<GeoPosition> Path, string Label);

    /// <summary>Snapshots <paramref name="routes"/> for drawing.</summary>
    public static RouteOverlayScene Build(
        RouteCollection routes,
        int? selectedWaypointIndex,
        int? selectedLegIndex,
        bool isEditing,
        MeasureOverlayAppearance appearance)
    {
        ArgumentNullException.ThrowIfNull(routes);
        var scene = routes.Routes
            .Select(r => SceneRoute.From(r, ReferenceEquals(r, routes.ActiveRoute)))
            .ToArray();
        return new RouteOverlayScene(
            scene, selectedWaypointIndex, selectedLegIndex, isEditing, RouteOverlayColors.For(appearance));
    }

    /// <summary>
    /// The leg label (A7): distance and initial bearing in the app's locale,
    /// and the leg's duration when its speed is known.
    /// </summary>
    internal static string LegLabel(double distanceNm, double bearingDegrees, TimeSpan? duration = null)
    {
        var label = string.Format(CultureInfo.CurrentCulture, Strings.Route_LegLabel, distanceNm, bearingDegrees);
        return duration is { } d ? label + " · " + PassageFormatting.FormatDuration(d) : label;
    }
}

/// <summary>
/// The route overlay's colours for one accent and chart palette. The edit
/// colour is the app accent (<c>--selected</c>); the casing, waypoint fill,
/// text halo and inactive colours follow the Day / Dusk / Night chart palette.
/// </summary>
/// <param name="Edit">The route line, waypoint ring and insert-handle colour.</param>
/// <param name="ActiveFill">Waypoint fill on the active route while not editing.</param>
/// <param name="Inactive">Lines and waypoints of the other routes.</param>
/// <param name="Casing">The casing under the line being edited: white on Day, black on Dusk and Night.</param>
/// <param name="WaypointFill">The fill of waypoint discs and insert handles while editing.</param>
/// <param name="Text">Leg label text.</param>
/// <param name="TextHalo">The halo behind leg label text.</param>
internal readonly record struct RouteOverlayColors(
    SKColor Edit,
    SKColor ActiveFill,
    SKColor Inactive,
    SKColor Casing,
    SKColor WaypointFill,
    SKColor Text,
    SKColor TextHalo)
{
    /// <summary>The colours for <paramref name="appearance"/>.</summary>
    public static RouteOverlayColors For(MeasureOverlayAppearance appearance)
    {
        var (r, g, b) = appearance.Accent;
        var edit = new SKColor(r, g, b);
        var dark = appearance.Palette is PaletteType.Dusk or PaletteType.Night;
        return dark
            ? new RouteOverlayColors(
                edit,
                Mix(edit, new SKColor(0x0E, 0x14, 0x1B), 0.55f),
                new SKColor(0x3A, 0x44, 0x52),
                SKColors.Black,
                new SKColor(0x0E, 0x14, 0x1B),
                new SKColor(0xA9, 0xB2, 0xBE),
                SKColors.Black)
            : new RouteOverlayColors(
                edit,
                Mix(edit, SKColors.White, 0.55f),
                new SKColor(0x56, 0x65, 0x7A),
                SKColors.White,
                SKColors.White,
                new SKColor(0x0B, 0x12, 0x20),
                SKColors.White);
    }

    private static SKColor Mix(SKColor c, SKColor toward, float amount)
    {
        byte M(byte from, byte to) => (byte)(from + (to - from) * amount);
        return new SKColor(M(c.Red, toward.Red), M(c.Green, toward.Green), M(c.Blue, toward.Blue));
    }
}
