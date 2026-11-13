using System.Globalization;
using EncDotNet.S100.Datasets.S104;
using EncDotNet.S100.Interoperability;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Portrayals.Application;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services.Legends;

namespace EncDotNet.S100.Viewer.Services.Tides;

/// <summary>
/// The <b>Tide stations · predicted</b> section of the Legend tab (tide
/// predictions design D4, legends D1 and D4): rising, falling and turning,
/// with the caption that a dashed frame means predicted. Shown while the
/// overlay is on, so MCP <c>get_legend</c> lists it too.
/// </summary>
internal sealed class TideStationLegendSource : ILegendSectionSource, IDisposable
{
    /// <summary>The section id, which the Layer stack row's Legend link shows.</summary>
    public const string SectionId = "TideStations";

    /// <summary>The group key.</summary>
    public const string GroupKey = "tides.stations";

    private readonly TideStationsOverlay _overlay;
    private readonly Func<PaletteType> _palette;
    private readonly Func<TimeFormat> _timeFormat;
    private readonly Func<DateTime> _viewTime;
    private string _signature = string.Empty;

    /// <summary>Creates the source.</summary>
    /// <param name="overlay">The station overlay.</param>
    /// <param name="palette">The palette the glyphs are drawn in.</param>
    /// <param name="timeFormat">The Local/UTC setting of the time summary.</param>
    /// <param name="viewTime">The view time (UTC).</param>
    public TideStationLegendSource(
        TideStationsOverlay overlay,
        Func<PaletteType> palette,
        Func<TimeFormat> timeFormat,
        Func<DateTime> viewTime)
    {
        ArgumentNullException.ThrowIfNull(overlay);
        ArgumentNullException.ThrowIfNull(palette);
        ArgumentNullException.ThrowIfNull(timeFormat);
        ArgumentNullException.ThrowIfNull(viewTime);
        _overlay = overlay;
        _palette = palette;
        _timeFormat = timeFormat;
        _viewTime = viewTime;
        _overlay.Source.Refreshed += OnRefreshed;
    }

    /// <inheritdoc />
    public IReadOnlyList<LegendSection> Sections => _overlay.Source.IsActive ? [Build()] : [];

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public void Dispose() => _overlay.Source.Refreshed -= OnRefreshed;

    private void OnRefreshed(object? sender, EventArgs e)
    {
        // Raise only when what the section shows changed, not on every tick.
        var signature = _overlay.Source.IsActive
            ? string.Create(CultureInfo.InvariantCulture, $"{_overlay.Source.StationsInView.Count}|{TimeFormatting.FormatClock(_viewTime(), _timeFormat())}")
            : string.Empty;
        if (signature == _signature)
        {
            return;
        }

        _signature = signature;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private LegendSection Build()
    {
        var legend = new LegendBuilder(S104CoverageStyle.Palette, _palette(), TideStationPortrayal.ResolveSymbol);
        var count = _overlay.Source.StationsInView.Count;
        return new LegendSection
        {
            Id = SectionId,
            Spec = Strings.Tides_LegendSpec,
            Title = Strings.Tides_StationsLayer,
            Source = string.Format(CultureInfo.CurrentCulture, Strings.Tides_LegendSourceFormat, count),
            DatasetIds = [],
            TimeSummary = string.Format(
                CultureInfo.CurrentCulture,
                Strings.Tides_LegendTimeFormat,
                TimeFormatting.FormatClock(_viewTime(), _timeFormat())),
            Caption = Strings.Tides_LegendCaption,
            // App state: shown whatever the view (legends D5).
            IsInView = true,
            IsExpanded = true,
            Plane = S98DisplayPlane.OtherChartOverlays,
            Groups =
            [
                new LegendGroup(GroupKey, Strings.Tides_StationsLayer,
                [
                    legend.Symbol(TideStationPortrayal.RisingSymbol, Strings.Tides_Trend_RisingLegend),
                    legend.Symbol(TideStationPortrayal.FallingSymbol, Strings.Tides_Trend_FallingLegend),
                    legend.Symbol(TideStationPortrayal.TurningSymbol, Strings.Tides_Trend_TurningLegend),
                ]),
            ],
        };
    }
}
