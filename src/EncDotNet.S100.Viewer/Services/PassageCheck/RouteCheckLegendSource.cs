using System.Globalization;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Renderers.Skia;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services.Legends;
using EncDotNet.S100.Viewer.Tools;
using SkiaSharp;

namespace EncDotNet.S100.Viewer.Services.PassageCheck;

/// <summary>
/// The route check's legend section (legends design note D5, D6, #1408): on
/// top of the Legend tab while Check route is on. Its groups are "UKC at ETA"
/// (when the checked route's clearance check ran, so the line is coloured by
/// clearance), "Findings" (the three marker shapes, a sleeve and the corridor
/// edge) and "Weather" (the storm at the ETA of a cyclone's closest
/// approach). The compact card at the map's bottom left
/// (<see cref="ViewModels.RouteCheckLegendViewModel"/>) draws the same section.
/// </summary>
/// <remarks>
/// The rows use the route's state tones for the legend's palette
/// (<see cref="RouteCheckColors"/>), as the map does. The sections are built
/// when read, so a palette change (which rebuilds the legend) recolours them.
/// </remarks>
internal sealed class RouteCheckLegendSource : ILegendSectionSource, IDisposable
{
    /// <summary>The section's <see cref="LegendSection.Id"/>.</summary>
    public const string SectionId = "RouteCheck";

    /// <summary>The "UKC at ETA" group's key.</summary>
    public const string UkcGroupKey = "routecheck.ukc";

    /// <summary>The "Findings" group's key.</summary>
    public const string FindingsGroupKey = "routecheck.findings";

    /// <summary>The "Weather" group's key.</summary>
    public const string WeatherGroupKey = "routecheck.weather";

    private readonly RoutesService _routes;
    private readonly PassageCheckService? _check;
    private readonly Func<PaletteType> _palette;

    /// <summary>Creates the source.</summary>
    /// <param name="routes">Whether Check route is on, and the checked route's name.</param>
    /// <param name="check">The check; <see langword="null"/> where the host has none, so there is no section.</param>
    /// <param name="palette">The palette the legend is drawn in.</param>
    public RouteCheckLegendSource(RoutesService routes, PassageCheckService? check, Func<PaletteType> palette)
    {
        ArgumentNullException.ThrowIfNull(routes);
        ArgumentNullException.ThrowIfNull(palette);
        _routes = routes;
        _check = check;
        _palette = palette;
        _routes.CheckModeChanged += OnChanged;
        if (_check is not null)
            _check.Changed += OnChanged;
    }

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public IReadOnlyList<LegendSection> Sections =>
        _routes.IsCheckModeActive && _check is not null ? [Build(_palette())] : [];

    /// <summary>
    /// Whether the line is coloured by clearance: the report's clearance check
    /// ran. Until the checked route's first report lands, whether it has a draft.
    /// </summary>
    internal bool HasClearance =>
        _check is { IsActive: true } check
        && (check.Report is { } report && check.ReportRouteId == check.Input?.RouteId
            ? report.Status(ClearanceCheck.CheckName) is { State: not RouteCheckState.Skipped }
            : check.HasDraft);

    /// <summary>Whether the checked route's report has a closest approach to a tropical cyclone (#1299).</summary>
    internal bool HasStormAtEta =>
        _check is { IsActive: true, Report: { } report } check
        && check.ReportRouteId == check.Input?.RouteId
        && report.Findings.Any(f => f.Code == RouteFindingCodes.CycloneClosestApproach);

    /// <inheritdoc />
    public void Dispose()
    {
        _routes.CheckModeChanged -= OnChanged;
        if (_check is not null)
            _check.Changed -= OnChanged;
    }

    private void OnChanged(object? sender, EventArgs e) => Changed?.Invoke(this, EventArgs.Empty);

    private LegendSection Build(PaletteType palette)
    {
        var colors = RouteCheckColors.For(palette);
        var groups = new List<LegendGroup>(3);
        bool clearance = HasClearance;
        if (clearance)
        {
            groups.Add(new LegendGroup(UkcGroupKey, Strings.Legend_RouteCheckUkc,
            [
                new LineLegendEntry(Line(colors.Meets, 4, []), Strings.Check_LegendMeets),
                new LineLegendEntry(Line(colors.Tight, 4, []), Strings.Check_LegendTight),
                new LineLegendEntry(Line(colors.Below, 6, []), Strings.Check_LegendBelow),
                new LineLegendEntry(Line(colors.Meets, 4, [6, 4]), Strings.Check_LegendLowConfidence),
                new LineLegendEntry(Line(colors.NoData, 4, [1.5, 3.5]), Strings.Check_LegendNoData),
            ]));
        }

        var (_, sleeve) = RouteOverlayRenderer.SleeveStyle(false);
        groups.Add(new LegendGroup(FindingsGroupKey, Strings.Legend_RouteCheckFindings,
        [
            new SymbolLegendEntry(MustReviewSvg(RouteCheckOverlay.ToneOf(RouteFindingGroup.MustReview, colors), colors.Casing), Strings.Map_LegendMustReview),
            new SymbolLegendEntry(CheckSvg(RouteCheckOverlay.ToneOf(RouteFindingGroup.Check, colors), colors.Casing), Strings.Map_LegendCheck),
            new SymbolLegendEntry(InfoSvg(RouteCheckOverlay.ToneOf(RouteFindingGroup.Info, colors), colors.Casing), Strings.Map_LegendInfo),
            new SwatchLegendEntry(Hex(colors.Below.WithAlpha((byte)Math.Round(255 * sleeve))), Strings.Map_LegendSleeve),
            new LineLegendEntry(Line(colors.Ink, 1.5, [1.5, 3]), Strings.Map_LegendCorridor),
        ]));

        if (HasStormAtEta)
        {
            groups.Add(new LegendGroup(WeatherGroupKey, Strings.Legend_RouteCheckWeather,
                [new SymbolLegendEntry(StormSvg(colors.Ink), Strings.Legend_StormAtEta)]));
        }

        return new LegendSection
        {
            Id = SectionId,
            Spec = Strings.Legend_RouteCheckSpec,
            Title = Strings.Legend_RouteCheckTitle,
            Source = _routes.Routes.ActiveRoute?.Name ?? string.Empty,
            DatasetIds = [],
            Caption = clearance ? null : Strings.Map_LegendNoDraft,
            // App state: shown whatever the view (D5).
            IsInView = true,
            IsExpanded = true,
            Plane = Interoperability.S98DisplayPlane.MarinerOverlay,
            Groups = groups,
        };
    }

    // A screen-space pen in the legend's pixels (the glyph renderer's scale),
    // as the map card draws it.
    private static LegendLineSample Line(SKColor color, double width, IReadOnlyList<double> dashPattern)
    {
        const double mm = LegendGlyphRenderer.MillimetresPerPixel;
        var dashes = new List<(double Start, double Length)>();
        double position = 0;
        for (int i = 0; i + 1 < dashPattern.Count; i += 2)
        {
            dashes.Add((position * mm, dashPattern[i] * mm));
            position += dashPattern[i] + dashPattern[i + 1];
        }

        return new LegendLineSample
        {
            Color = Hex(color),
            Width = width * mm,
            Dashes = dashes,
            IntervalLength = dashes.Count == 0 ? 0 : position * mm,
        };
    }

    private static string Hex(SKColor c) => c.Alpha == 255
        ? new RgbaColor(c.Red, c.Green, c.Blue).ToHex()
        : string.Create(CultureInfo.InvariantCulture, $"#{c.Alpha:X2}{c.Red:X2}{c.Green:X2}{c.Blue:X2}");

    // The markers as the map draws them: the shape carries the group, the
    // glyph is in the casing colour (D11: never colour alone).
    private static string MustReviewSvg(SKColor fill, SKColor casing) => Svg(
        $"<path d=\"M5.6,1 L12.4,1 L17,5.6 L17,12.4 L12.4,17 L5.6,17 L1,12.4 L1,5.6 Z\" fill=\"{Hex(fill)}\" stroke=\"{Hex(casing)}\" stroke-width=\"1.6\"/>"
        + Bang(casing, 4.2, 10.4, 11.9));

    private static string CheckSvg(SKColor fill, SKColor casing) => Svg(
        $"<path d=\"M9,1.2 L17.2,16.6 L0.8,16.6 Z\" fill=\"{Hex(fill)}\" stroke=\"{Hex(casing)}\" stroke-width=\"1.6\"/>"
        + Bang(casing, 6.2, 11.6, 13));

    private static string InfoSvg(SKColor fill, SKColor casing) => Svg(
        $"<circle cx=\"9\" cy=\"9\" r=\"7.5\" fill=\"{Hex(fill)}\" stroke=\"{Hex(casing)}\" stroke-width=\"1.6\"/>"
        + $"<rect x=\"8.1\" y=\"4.4\" width=\"1.8\" height=\"1.8\" fill=\"{Hex(casing)}\"/>"
        + $"<rect x=\"8.1\" y=\"7.4\" width=\"1.8\" height=\"6.2\" fill=\"{Hex(casing)}\"/>");

    // The storm at ETA: its class letter in a dashed ring.
    private static string StormSvg(SKColor ink) => Svg(
        $"<circle cx=\"9\" cy=\"9\" r=\"7.6\" fill=\"none\" stroke=\"{Hex(ink)}\" stroke-width=\"1.4\" stroke-dasharray=\"2,1.5\"/>"
        + $"<rect x=\"5.6\" y=\"5\" width=\"1.7\" height=\"8\" fill=\"{Hex(ink)}\"/>"
        + $"<rect x=\"10.7\" y=\"5\" width=\"1.7\" height=\"8\" fill=\"{Hex(ink)}\"/>"
        + $"<rect x=\"5.6\" y=\"8.2\" width=\"6.8\" height=\"1.6\" fill=\"{Hex(ink)}\"/>");

    private static string Bang(SKColor ink, double top, double stemBottom, double dotTop) =>
        string.Create(CultureInfo.InvariantCulture,
            $"<rect x=\"8.1\" y=\"{top}\" width=\"1.8\" height=\"{stemBottom - top}\" fill=\"{Hex(ink)}\"/>"
            + $"<rect x=\"8.1\" y=\"{dotTop}\" width=\"1.8\" height=\"1.8\" fill=\"{Hex(ink)}\"/>");

    private static string Svg(string body) =>
        "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"18\" height=\"18\" viewBox=\"0 0 18 18\">" + body + "</svg>";
}
