using System.Globalization;
using EncDotNet.S100.Datasets.Weather;
using EncDotNet.S100.Interoperability;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Portrayals.Application;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services.Legends;

namespace EncDotNet.S100.Viewer.Services.Tides;

/// <summary>
/// The <b>Current stations · predicted</b> section of the Legend tab (tide
/// predictions design D16, Q2a′): S-111's speed bands and the slack bar, with
/// the caption "Speed, kn · arrow points where the current flows". Shown
/// while the overlay is on.
/// </summary>
internal sealed class CurrentStationLegendSource : ILegendSectionSource, IDisposable
{
    /// <summary>The section id, which the Layer stack row's Legend link shows.</summary>
    public const string SectionId = "CurrentStations";

    /// <summary>The group key.</summary>
    public const string GroupKey = "tides.currents";

    private readonly TideStationsOverlay _overlay;
    private readonly Func<PaletteType> _palette;
    private readonly Func<TimeFormat> _timeFormat;
    private readonly Func<DateTime> _viewTime;
    private string _signature = string.Empty;

    /// <summary>Creates the source.</summary>
    /// <param name="overlay">The station overlays.</param>
    /// <param name="palette">The palette the glyphs are drawn in.</param>
    /// <param name="timeFormat">The Local/UTC setting of the time summary.</param>
    /// <param name="viewTime">The view time (UTC).</param>
    public CurrentStationLegendSource(
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
        _overlay.Currents.Refreshed += OnRefreshed;
    }

    /// <inheritdoc />
    public IReadOnlyList<LegendSection> Sections => _overlay.Currents.IsActive ? [Build()] : [];

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public void Dispose() => _overlay.Currents.Refreshed -= OnRefreshed;

    private void OnRefreshed(object? sender, EventArgs e)
    {
        var signature = _overlay.Currents.IsActive
            ? string.Create(CultureInfo.InvariantCulture, $"{_overlay.Currents.StationsInView.Count}|{TimeFormatting.FormatClock(_viewTime(), _timeFormat())}")
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
        var legend = new LegendBuilder(WeatherPalette.Palette, _palette(), CurrentStationPortrayal.ResolveSymbol);
        var rows = new List<Datasets.Pipelines.Portrayal.LegendEntry>();
        var bands = CurrentArrowStyle.Bands;
        for (var i = 0; i < bands.Count; i++)
        {
            rows.Add(legend.Symbol(CurrentStationPortrayal.ArrowSymbol(i + 1), bands[i].Label ?? string.Empty));
        }

        rows.Add(legend.Symbol(CurrentStationPortrayal.SlackSymbol, Strings.Tides_Currents_LegendSlack));
        return new LegendSection
        {
            Id = SectionId,
            Spec = Strings.Tides_Currents_LegendSpec,
            Title = Strings.Tides_CurrentsLayer,
            Source = string.Format(CultureInfo.CurrentCulture, Strings.Tides_LegendSourceFormat, _overlay.Currents.StationsInView.Count),
            DatasetIds = [],
            TimeSummary = string.Format(
                CultureInfo.CurrentCulture,
                Strings.Tides_LegendTimeFormat,
                TimeFormatting.FormatClock(_viewTime(), _timeFormat())),
            Caption = Strings.Tides_Currents_LegendCaption,
            IsInView = true,
            IsExpanded = true,
            Plane = S98DisplayPlane.OtherChartOverlays,
            Groups = [new LegendGroup(GroupKey, Strings.Tides_CurrentsLayer, rows)],
        };
    }
}
