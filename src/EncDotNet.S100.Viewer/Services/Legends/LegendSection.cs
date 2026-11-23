using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Interoperability;

namespace EncDotNet.S100.Viewer.Services.Legends;

/// <summary>
/// The legend of one product or overlay drawn on the map (legends design note
/// D1, D4): seven S-102 tiles make one section. Built by
/// <see cref="LegendService"/>; the Legend tab, the route-check map card and
/// MCP <c>get_legend</c> all read it.
/// </summary>
internal sealed record LegendSection
{
    /// <summary>
    /// The section's stable id: the product spec (<c>"S-111"</c>,
    /// <c>"AML"</c>, <c>"TC"</c>), or for a weather grid, which carries several
    /// unrelated fields under one spec, the field's sub-layer key
    /// (<c>"weather.wave.height"</c>).
    /// </summary>
    public required string Id { get; init; }

    /// <summary>The product chip in the header: the spec the datasets load under (<c>"S-111"</c>, <c>"GRIB2"</c>).</summary>
    public required string Spec { get; init; }

    /// <summary>The product's name: "Surface currents", "Wave height", "Tropical cyclones".</summary>
    public required string Title { get; init; }

    /// <summary>Where the rows come from: the dataset's name, or "7 datasets".</summary>
    public required string Source { get; init; }

    /// <summary>The session ids of the datasets behind the section, top of the stack first.</summary>
    public required IReadOnlyList<string> DatasetIds { get; init; }

    /// <summary>
    /// What a time-aware product draws at the view time, as its Datasets row
    /// says it ("08:00Z · T+20 h", "no data · last 18:00Z, 6 h earlier");
    /// <see langword="null"/> for a product that doesn't vary with time.
    /// </summary>
    public string? TimeSummary { get; init; }

    /// <summary>Whether the product varies with time and, if so, whether it has data at the view time (D5).</summary>
    public LegendTimeState TimeState { get; init; }

    /// <summary>
    /// How the product's values were produced (S-104 and S-111
    /// <c>dataDynamicity</c>; #1505), from the dataset drawing at the view
    /// time; <see langword="null"/> when it doesn't say.
    /// </summary>
    public EncDotNet.S100.Datasets.Pipelines.DataDynamicity? DataDynamicity { get; init; }

    /// <summary>When that dataset was issued (UTC): a forecast's run time; <see langword="null"/> when unknown.</summary>
    public DateTime? IssueTime { get; init; }

    /// <summary>A one-line caption where the unit or rule needs it ("Speed, kn · longer arrow = faster"); null for none.</summary>
    public string? Caption { get; init; }

    /// <summary>
    /// For a catalogue key that lists fewer symbols than it draws (D7,
    /// "12 of 54 symbols drawn in view"), the portrayal spec its
    /// <b>Show all in Chart reference</b> link opens the activity at
    /// (<c>S-101</c>, also for S-57 cells); null when there is no link.
    /// </summary>
    public string? ChartReferenceSpec { get; init; }

    /// <summary>
    /// For a catalogue key (D7), the portrayal spec whose catalogue its
    /// symbols come from (<c>S-101</c>, also for S-57 cells): each row's
    /// label is a Chart reference term in it (symbol dictionary R8, #1651).
    /// Null for any other section.
    /// </summary>
    public string? CatalogueSpec { get; init; }

    /// <summary>The Mariner settings the rows follow ("Safety contour 10 m"); null when they follow none.</summary>
    public string? MarinerSettings { get; init; }

    /// <summary>
    /// Whether any of the section's datasets meets the map view, by the same
    /// footprint test the Datasets panel and Timeline use. Updated on pan end,
    /// never under the pointer (D5). True while the view or a footprint isn't
    /// known yet.
    /// </summary>
    public required bool IsInView { get; init; }

    /// <summary>Whether the section shows its rows; remembered per section id.</summary>
    public required bool IsExpanded { get; init; }

    /// <summary>The topmost S-98 display plane the section draws in.</summary>
    public required S98DisplayPlane Plane { get; init; }

    /// <summary>
    /// The rows (<see cref="LegendGroup"/>, shared with <c>s100 tiles serve</c>
    /// through <see cref="ProductLegends"/>), one group per kind of sub-layer, top of the stack first: a
    /// tropical cyclone's track and its areas, or an AML cell's families. A
    /// section of one group shows no group title.
    /// </summary>
    public required IReadOnlyList<LegendGroup> Groups { get; init; }

    /// <summary>Every row of every group, in order.</summary>
    public IEnumerable<LegendEntry> Rows => Groups.SelectMany(g => g.Rows);

    /// <summary>True when the product varies with time and has no data at the view time.</summary>
    public bool HasNoDataAtViewTime => TimeState == LegendTimeState.NoData;
}

/// <summary>What a section's product draws at the view time.</summary>
internal enum LegendTimeState
{
    /// <summary>The product doesn't vary with time.</summary>
    None,

    /// <summary>At least one of its datasets draws data at the view time.</summary>
    Data,

    /// <summary>None of its datasets has data at the view time, so it draws nothing (D5).</summary>
    NoData,
}

/// <summary>What a legend row's glyph shows.</summary>
internal enum LegendRowKind
{
    /// <summary>A colour swatch: a band of a colour fill.</summary>
    Swatch,

    /// <summary>A swatch inside an outline: an area such as a cyclone's cone.</summary>
    Area,

    /// <summary>A point symbol.</summary>
    Symbol,

    /// <summary>A sample of a line style with its symbols.</summary>
    Line,

    /// <summary>An area fill pattern tiled in a swatch, such as a dredged area.</summary>
    Fill,
}

/// <summary>Helpers over <see cref="LegendEntry"/> rows.</summary>
internal static class LegendRows
{
    /// <summary>What the row's glyph shows.</summary>
    /// <param name="entry">The row.</param>
    public static LegendRowKind KindOf(LegendEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return entry switch
        {
            SwatchLegendEntry { Outline: null } => LegendRowKind.Swatch,
            SwatchLegendEntry => LegendRowKind.Area,
            SymbolLegendEntry => LegendRowKind.Symbol,
            FillLegendEntry => LegendRowKind.Fill,
            _ => LegendRowKind.Line,
        };
    }
}

/// <summary>A request to open the Legend tab (<see cref="LegendService.Show"/>).</summary>
/// <param name="SectionId">The section to scroll to and expand; null for the tab as it was.</param>
/// <param name="RowLabel">The label of the row to ring; null for none.</param>
internal sealed record LegendShowRequest(string? SectionId, string? RowLabel);

/// <summary>
/// Legend sections that don't come from the drawn layer stack: app state such
/// as the route check or the Library coverage outlines (legends design note
/// D5). Added with <see cref="LegendService.AddSource"/>; its sections go on
/// top.
/// </summary>
internal interface ILegendSectionSource
{
    /// <summary>The sections to show now, top first; empty while the state isn't on.</summary>
    IReadOnlyList<LegendSection> Sections { get; }

    /// <summary>Raised when <see cref="Sections"/> changes; the service rebuilds on the next UI frame.</summary>
    event EventHandler? Changed;
}
