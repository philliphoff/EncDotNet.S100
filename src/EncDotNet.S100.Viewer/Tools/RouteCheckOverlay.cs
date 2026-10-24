using System.Globalization;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Routing;
using EncDotNet.S100.Viewer.Services.PassageCheck;
using SkiaSharp;

namespace EncDotNet.S100.Viewer.Tools;

/// <summary>
/// What the route overlay draws in Check passage mode (#901, handoff C1 and
/// C6–C10): the active route coloured by UKC state at ETA, the minimum-UKC
/// marker, the ghost vessel at the cursor and the wash, plus the route-check
/// map layer (#959 slice 3). The legend is a control over the map
/// (<c>Views.RouteCheckLegendView</c>).
/// </summary>
/// <param name="Segments">Runs of samples with one state and confidence, in passage order; a run never crosses a leg boundary.</param>
/// <param name="IsStale">True while a recompute is running: the route draws grey and dashed (C10).</param>
/// <param name="Minimum">The minimum-UKC marker, or <c>null</c>.</param>
/// <param name="Ghost">The ghost vessel at the cursor, or <c>null</c>.</param>
/// <param name="Colors">The state colours for the palette.</param>
/// <param name="Finding">The selected finding's outline and chip (#905, F3), or <c>null</c>.</param>
/// <param name="FindingMap">The route-check map layer (#959 slice 3): corridor, sleeves and markers; <c>null</c> draws none.</param>
/// <param name="HasClearance">Whether the route is coloured by UKC state, so the "UKC at ETA" legend is drawn; false without a clearance result (no draft, or none yet).</param>
/// <param name="NoDraft">Whether the route has no draft, so the line keeps the active-route accent instead of UKC colours (#959, A3).</param>
internal sealed record RouteCheckOverlay(
    IReadOnlyList<RouteCheckOverlay.Segment> Segments,
    bool IsStale,
    RouteCheckOverlay.Marker? Minimum,
    RouteCheckOverlay.GhostVessel? Ghost,
    RouteCheckColors Colors,
    RouteCheckOverlay.FindingFocus? Finding = null,
    RouteCheckMap? FindingMap = null,
    bool HasClearance = true,
    bool NoDraft = false)
{
    /// <summary>The route-check map layer, never <c>null</c>.</summary>
    public RouteCheckMap Map => FindingMap ?? RouteCheckMap.Empty;

    /// <summary>
    /// How far the legend (<c>Map.RouteCheckLegend</c>) reaches up from the
    /// map's bottom edge, so the finding chip sits above it; 0 when the
    /// legend isn't shown.
    /// </summary>
    public float LegendReserve { get; init; }

    /// <summary>
    /// A run of the route in one state. A lower-confidence run keeps its
    /// band's colour and is drawn dashed (#939).
    /// </summary>
    internal sealed record Segment(IReadOnlyList<GeoPosition> Path, UkcState State, bool IsLowConfidence = false);

    /// <summary>The min-UKC ring and pill (C8).</summary>
    internal sealed record Marker(GeoPosition Position, string Label, UkcState State);

    /// <summary>The ghost vessel (C9).</summary>
    internal sealed record GhostVessel(GeoPosition Position, double BearingDegrees);

    /// <summary>
    /// The selected finding (F3): its area outlined dashed in the finding's
    /// tone, and the chip "Zoomed to finding · leg 6 → 7 · 1:31 300" (the
    /// renderer appends the live scale).
    /// </summary>
    /// <param name="Path">One position for a point, or the span's positions.</param>
    /// <param name="Tone">The finding's tone colour.</param>
    /// <param name="ChipText">The chip text without the scale.</param>
    /// <param name="IsOutlined">Whether the area is outlined; false for a route finding, which the map layer rings instead (#959 C7).</param>
    internal sealed record FindingFocus(IReadOnlyList<GeoPosition> Path, SKColor Tone, string ChipText, bool IsOutlined = true);

    /// <summary>
    /// Builds the overlay for a check, or returns <c>null</c> when check mode
    /// is off. Without a clearance result yet the route draws grey and
    /// dashed; without a draft it keeps the active-route accent (A3). Either
    /// way it gets the route-check map layer.
    /// </summary>
    /// <param name="findings">The findings list, drawn as sleeves and markers (#959 C2, C4); <c>null</c> draws none.</param>
    /// <param name="hasDraft">Whether the checked route has a draft; without one there is no clearance to colour the line by.</param>
    /// <param name="route">The checked route, for the corridor and the markers' places; <c>null</c> draws no map layer.</param>
    /// <param name="viewTime">The time the pill's ETA runs from when the route has no departure.</param>
    public static RouteCheckOverlay? Build(
        bool isActive,
        PassageCheckSnapshot? snapshot,
        bool isStale,
        PassageCursor? cursor,
        MeasureOverlayAppearance appearance,
        DepthUnit depthUnit,
        TimeFormat timeFormat,
        RouteFinding? selectedFinding = null,
        IReadOnlyList<RouteFinding>? findings = null,
        bool hasDraft = true,
        Route? route = null,
        DateTimeOffset? viewTime = null)
    {
        if (!isActive)
            return null;

        var colors = RouteCheckColors.For(appearance.Palette);
        var markers = RouteCheckMap.Build(
            route,
            findings,
            selectedFinding,
            colors,
            RouteOverlayColors.For(appearance).Edit,
            appearance.Palette,
            viewTime ?? snapshot?.Result.Departure ?? DateTimeOffset.UtcNow,
            timeFormat,
            RouteCheckOptions.Default.DefaultLimit.TotalMetres);
        if (!hasDraft)
            return new RouteCheckOverlay([], false, null, null, colors, Focus(null, selectedFinding, colors, markers), markers, HasClearance: false, NoDraft: true);
        if (snapshot is null)
            return new RouteCheckOverlay([], true, null, null, colors, Focus(null, selectedFinding, colors, markers), markers, HasClearance: false);

        var result = snapshot.Result;
        var segments = new List<Segment>();
        var samples = result.Samples;
        var start = 0;
        for (var i = 1; i <= samples.Count; i++)
        {
            if (i < samples.Count && samples[i].State == samples[start].State && samples[i].LegIndex == samples[start].LegIndex
                && IsLowConfidence(samples[i]) == IsLowConfidence(samples[start]))
                continue;
            var first = samples[start];
            var last = samples[i - 1];
            segments.Add(new Segment(
                PathAlong(snapshot, first.LegIndex, first.StartDistanceMeters, last.EndDistanceMeters),
                first.State,
                IsLowConfidence(first)));
            start = i;
        }

        Marker? minimum = null;
        if (result.Minimum is { } min)
        {
            var label = string.Format(
                CultureInfo.CurrentCulture,
                Strings.Check_MinUkcPillFormat,
                PassageFormatting.Format(min.ClearanceMeters, PassageQuantity.Depth, depthUnit),
                PassageFormatting.FormatEta(min.Time, result.Departure, timeFormat));
            minimum = new Marker(min.Position, label, min.State);
        }

        var ghost = cursor is { } c ? new GhostVessel(c.Position, c.BearingDegrees) : null;
        return new RouteCheckOverlay(segments, isStale, minimum, ghost, colors, Focus(snapshot, selectedFinding, colors, markers), markers);
    }

    /// <summary>The colour of a group, from the state tones (Part G): red only for must review (F5).</summary>
    internal static SKColor ToneOf(RouteFindingGroup group, RouteCheckColors colors) => group switch
    {
        RouteFindingGroup.MustReview => colors.Below,
        RouteFindingGroup.Check => colors.Tight,
        _ => colors.Info,
    };

    // No data is already grey and dashed; confidence only qualifies a band.
    private static bool IsLowConfidence(UkcSample sample) => sample.State != UkcState.NoData && sample.IsLowConfidence;

    private static FindingFocus? Focus(PassageCheckSnapshot? snapshot, RouteFinding? finding, RouteCheckColors colors, RouteCheckMap map)
    {
        if (finding?.Location is not { Path.Count: > 0 } location)
            return null;

        var tone = ViewModels.RouteFindingRowViewModel.ToneOf(finding) switch
        {
            ViewModels.CheckTone.Destructive => colors.Below,
            ViewModels.CheckTone.Warning => colors.Tight,
            _ => colors.Info,
        };
        var chip = Strings.Findings_ZoomedChip;
        if (finding.LegIndex is { } first)
        {
            var last = location.IsPoint || snapshot is null
                ? first
                : Math.Max(first, PassageCheckService.CursorAt(snapshot, location.EndDistanceMeters).LegIndex);
            chip += " · " + (first == last
                ? string.Format(CultureInfo.CurrentCulture, Strings.Findings_ChipLegFormat, first + 1, first + 2)
                : string.Format(CultureInfo.CurrentCulture, Strings.Findings_ChipLegsFormat, first + 1, last + 2));
        }

        // A route finding on the map layer gets its marker's ring and pill
        // instead of the dashed outline (C7).
        var outlined = map.SelectedMark is null && map.SelectedSleeve is null;
        return new FindingFocus(location.Path, tone, chip, outlined);
    }

    // The positions along one leg between two distances from the start of the passage.
    internal static IReadOnlyList<GeoPosition> PathAlong(PassageCheckSnapshot snapshot, int legIndex, double from, double to)
    {
        var leg = snapshot.Legs[legIndex];
        var summary = snapshot.Result.Legs[legIndex];
        var length = summary.DistanceMeters;
        double Fraction(double d) => length > 0 ? Math.Clamp((d - summary.StartDistanceMeters) / length, 0, 1) : 0;
        var a = Fraction(from);
        var b = Fraction(to);
        var steps = leg.Geometry == LegGeometry.GreatCircle ? Math.Max(1, (int)Math.Ceiling((b - a) * RouteOverlayScene.GeodesicSegments)) : 1;
        var path = new GeoPosition[steps + 1];
        for (var k = 0; k <= steps; k++)
            path[k] = PassageGeodesy.Interpolate(leg.Points[0], leg.Points[^1], a + ((b - a) * k / steps), leg.Geometry);
        return path;
    }
}

/// <summary>
/// The UKC state colours (handoff C6), per chart palette, which the route
/// findings reuse (#959 Part G): Must review is Below, Check is Tight, Info is
/// the Low confidence grey.
/// </summary>
/// <remarks>
/// <para>
/// Day is the #887 handoff's, with Tight darkened to <c>#B07D00</c> so it keeps
/// 3:1 on its white casing (#934). Dusk keeps the palette pass's values
/// (#933): between Day and Night in hue, on the black casing that Dusk shares
/// with Night, and no brighter than S-101 Dusk's <c>CHWHT</c>. Night drops
/// every state to about 0,10 relative luminance (from 0,15–0,22), so the route
/// is no brighter than it must be to keep 3:1 on its black casing (#934).
/// </para>
/// <para>
/// Lower confidence has no colour of its own on the line (#939): a
/// lower-confidence run keeps its band's colour and is dashed 6/4, so meets
/// and tight stay visible where S-102 has no declared datum or S-101 stands
/// in. The Low confidence grey colours Info findings. No data is grey and
/// dotted 1,5/3,5, fainter than every band colour.
/// </para>
/// <para>
/// Red is reserved for Below your UKC setting and Must review findings; Below
/// is also drawn wider, so it doesn't rely on colour alone.
/// </para>
/// </remarks>
/// <param name="Info">Info findings: the handoff's Low confidence grey.</param>
internal readonly record struct RouteCheckColors(
    SKColor Meets,
    SKColor Tight,
    SKColor Below,
    SKColor Info,
    SKColor NoData,
    SKColor Stale,
    SKColor Casing,
    SKColor Wash,
    SKColor Ink,
    SKColor PillFill)
{
    private static readonly RouteCheckColors Day = new(
        new SKColor(0x13, 0x89, 0x5B),
        new SKColor(0xB0, 0x7D, 0x00),
        new SKColor(0xD4, 0x2A, 0x3A),
        new SKColor(0x5E, 0x6B, 0x7E),
        new SKColor(0x8C, 0x95, 0xA3),
        new SKColor(0x8C, 0x95, 0xA3),
        SKColors.White,
        SKColors.White.WithAlpha(77),
        new SKColor(0x0B, 0x12, 0x20),
        SKColors.White);

    private static readonly RouteCheckColors Dusk = new(
        new SKColor(0x20, 0x89, 0x5C),
        new SKColor(0xC1, 0x8D, 0x12),
        new SKColor(0xCA, 0x32, 0x40),
        new SKColor(0x6A, 0x75, 0x84),
        new SKColor(0x58, 0x61, 0x6E),
        new SKColor(0x58, 0x61, 0x6E),
        SKColors.Black,
        SKColors.Black.WithAlpha(77),
        new SKColor(0x9A, 0xA3, 0xAE),
        new SKColor(0x14, 0x1A, 0x22));

    private static readonly RouteCheckColors Night = new(
        new SKColor(0x22, 0x67, 0x46),
        new SKColor(0x73, 0x56, 0x14),
        new SKColor(0xA4, 0x32, 0x3C),
        new SKColor(0x57, 0x60, 0x6F),
        new SKColor(0x51, 0x5A, 0x65),
        new SKColor(0x51, 0x5A, 0x65),
        SKColors.Black,
        SKColors.Black.WithAlpha(77),
        new SKColor(0x8A, 0x94, 0xA0),
        new SKColor(0x0E, 0x14, 0x1B));

    public static RouteCheckColors For(PaletteType palette) => palette switch
    {
        PaletteType.Night => Night,
        PaletteType.Dusk => Dusk,
        _ => Day,
    };

    /// <summary>The colour of a state.</summary>
    public SKColor Of(UkcState state) => state switch
    {
        UkcState.MeetsSettings => Meets,
        UkcState.Tight => Tight,
        UkcState.BelowSetting => Below,
        _ => NoData,
    };
}
