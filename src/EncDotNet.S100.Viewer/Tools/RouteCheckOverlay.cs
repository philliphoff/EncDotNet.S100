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
/// marker, the ghost vessel at the cursor, the wash and the legend.
/// </summary>
/// <param name="Segments">Runs of samples with one state, in passage order; a run never crosses a leg boundary.</param>
/// <param name="IsStale">True while a recompute is running: the route draws grey and dashed (C10).</param>
/// <param name="Minimum">The minimum-UKC marker, or <c>null</c>.</param>
/// <param name="Ghost">The ghost vessel at the cursor, or <c>null</c>.</param>
/// <param name="Colors">The state colours for the palette.</param>
/// <param name="Finding">The selected finding's outline and chip (#905, F3), or <c>null</c>.</param>
internal sealed record RouteCheckOverlay(
    IReadOnlyList<RouteCheckOverlay.Segment> Segments,
    bool IsStale,
    RouteCheckOverlay.Marker? Minimum,
    RouteCheckOverlay.GhostVessel? Ghost,
    RouteCheckColors Colors,
    RouteCheckOverlay.FindingFocus? Finding = null)
{
    /// <summary>A run of the route in one state.</summary>
    internal sealed record Segment(IReadOnlyList<GeoPosition> Path, UkcState State);

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
    internal sealed record FindingFocus(IReadOnlyList<GeoPosition> Path, SKColor Tone, string ChipText);

    /// <summary>
    /// Builds the overlay for a check, or returns <c>null</c> when there is
    /// nothing to draw (check mode off, or no result yet).
    /// </summary>
    public static RouteCheckOverlay? Build(
        bool isActive,
        PassageCheckSnapshot? snapshot,
        bool isStale,
        PassageCursor? cursor,
        MeasureOverlayAppearance appearance,
        DepthUnit depthUnit,
        TimeFormat timeFormat,
        RouteFinding? selectedFinding = null)
    {
        if (!isActive)
            return null;

        var colors = RouteCheckColors.For(appearance.Palette);
        if (snapshot is null)
            return new RouteCheckOverlay([], true, null, null, colors);

        var result = snapshot.Result;
        var segments = new List<Segment>();
        var samples = result.Samples;
        var start = 0;
        for (var i = 1; i <= samples.Count; i++)
        {
            if (i < samples.Count && samples[i].State == samples[start].State && samples[i].LegIndex == samples[start].LegIndex)
                continue;
            var first = samples[start];
            var last = samples[i - 1];
            segments.Add(new Segment(
                PathAlong(snapshot, first.LegIndex, first.StartDistanceMeters, last.EndDistanceMeters),
                first.State));
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
        return new RouteCheckOverlay(segments, isStale, minimum, ghost, colors, Focus(snapshot, selectedFinding, colors));
    }

    private static FindingFocus? Focus(PassageCheckSnapshot snapshot, RouteFinding? finding, RouteCheckColors colors)
    {
        if (finding?.Location is not { Path.Count: > 0 } location)
            return null;

        var tone = ViewModels.RouteFindingRowViewModel.ToneOf(finding) switch
        {
            ViewModels.CheckTone.Destructive => colors.Below,
            ViewModels.CheckTone.Warning => colors.Tight,
            _ => colors.Ink,
        };
        var chip = Strings.Findings_ZoomedChip;
        if (finding.LegIndex is { } first)
        {
            var last = location.IsPoint ? first : Math.Max(first, PassageCheckService.CursorAt(snapshot, location.EndDistanceMeters).LegIndex);
            chip += " · " + (first == last
                ? string.Format(CultureInfo.CurrentCulture, Strings.Findings_ChipLegFormat, first + 1, first + 2)
                : string.Format(CultureInfo.CurrentCulture, Strings.Findings_ChipLegsFormat, first + 1, last + 2));
        }

        return new FindingFocus(location.Path, tone, chip);
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
/// The UKC state colours (handoff C6). Dusk isn't specified; it sits half
/// way between Day and Night until the palette pass (#906).
/// </summary>
internal readonly record struct RouteCheckColors(
    SKColor Meets,
    SKColor Tight,
    SKColor Below,
    SKColor LowConfidence,
    SKColor NoData,
    SKColor Stale,
    SKColor Casing,
    SKColor Wash,
    SKColor Ink,
    SKColor PillFill)
{
    private static readonly RouteCheckColors Day = new(
        new SKColor(0x13, 0x89, 0x5B),
        new SKColor(0xE0, 0xA0, 0x08),
        new SKColor(0xD4, 0x2A, 0x3A),
        new SKColor(0x5E, 0x6B, 0x7E),
        new SKColor(0x8C, 0x95, 0xA3),
        new SKColor(0x8C, 0x95, 0xA3),
        SKColors.White,
        SKColors.White.WithAlpha(77),
        new SKColor(0x0B, 0x12, 0x20),
        SKColors.White);

    private static readonly RouteCheckColors Night = new(
        new SKColor(0x2E, 0x8A, 0x5E),
        new SKColor(0xA2, 0x7A, 0x1C),
        new SKColor(0xC0, 0x3A, 0x46),
        new SKColor(0x56, 0x60, 0x6E),
        new SKColor(0x4A, 0x51, 0x5C),
        new SKColor(0x4A, 0x51, 0x5C),
        SKColors.Black,
        SKColors.Black.WithAlpha(77),
        new SKColor(0xA9, 0xB2, 0xBE),
        new SKColor(0x0E, 0x14, 0x1B));

    public static RouteCheckColors For(PaletteType palette) => palette switch
    {
        PaletteType.Night => Night,
        PaletteType.Dusk => Mix(Day, Night),
        _ => Day,
    };

    /// <summary>The colour of a state.</summary>
    public SKColor Of(UkcState state) => state switch
    {
        UkcState.MeetsSettings => Meets,
        UkcState.Tight => Tight,
        UkcState.BelowSetting => Below,
        UkcState.LowConfidence => LowConfidence,
        _ => NoData,
    };

    private static RouteCheckColors Mix(RouteCheckColors a, RouteCheckColors b)
    {
        static SKColor M(SKColor x, SKColor y) => new(
            (byte)((x.Red + y.Red) / 2), (byte)((x.Green + y.Green) / 2), (byte)((x.Blue + y.Blue) / 2), (byte)((x.Alpha + y.Alpha) / 2));
        return new RouteCheckColors(
            M(a.Meets, b.Meets), M(a.Tight, b.Tight), M(a.Below, b.Below), M(a.LowConfidence, b.LowConfidence),
            M(a.NoData, b.NoData), M(a.Stale, b.Stale), b.Casing, b.Wash, b.Ink, b.PillFill);
    }
}
