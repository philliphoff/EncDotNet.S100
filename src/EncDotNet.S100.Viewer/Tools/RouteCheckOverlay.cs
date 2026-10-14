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
/// The UKC state colours (handoff C6), per chart palette.
/// </summary>
/// <remarks>
/// <para>
/// Day and the Meets / Tight / Below hues on Night are the handoff's. Dusk
/// isn't mocked: it sits between Day and Night in hue, on the black casing
/// that Dusk shares with Night, with its brightest colour no brighter than
/// S-101 Dusk's (<c>CHWHT</c>). The palette pass (#906) set the greys so that
/// every state keeps at least 3:1 against its casing on Dusk and Night, and
/// No data stays fainter than Low confidence in every palette (the plain
/// Day/Night midpoint had them the wrong way round). Night ink is dimmed
/// towards S-101 Night's brightest colour.
/// </para>
/// <para>
/// Red is reserved for Below your UKC setting (and must-review findings);
/// Below is also drawn wider, so it doesn't rely on colour alone. Day's amber
/// Tight is 2.3:1 against its white casing: it's identified by hue, and the
/// casing outlines it against the chart.
/// </para>
/// </remarks>
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
        new SKColor(0x2E, 0x8A, 0x5E),
        new SKColor(0xA2, 0x7A, 0x1C),
        new SKColor(0xC0, 0x3A, 0x46),
        new SKColor(0x5C, 0x66, 0x76),
        new SKColor(0x52, 0x5B, 0x66),
        new SKColor(0x52, 0x5B, 0x66),
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
        UkcState.LowConfidence => LowConfidence,
        _ => NoData,
    };

}
