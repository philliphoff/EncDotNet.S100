using System.ComponentModel;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Viewer.Services.Legends;
using ModelContextProtocol.Server;

namespace EncDotNet.S100.Viewer.McpTools;

// Viewer-only tool that reads the legend service (#1403, docs/design/legends.md D12).

/// <summary>One row of a legend section.</summary>
[Description("One legend row: a glyph and its label.")]
internal sealed record LegendRowDto(
    [property: Description("What the glyph shows: 'swatch' (a colour band), 'area' (a swatch in an outline), 'symbol' or 'line'.")] string Kind,
    [property: Description("The row's label, also its accessible name, e.g. \"0.5–1 m\" or \"Cold front\".")] string Label,
    [property: Description("A swatch or area row's colour as #RRGGBB in the current palette; null for a symbol or line row.")] string? Color);

/// <summary>The rows of one kind of sub-layer in a legend section.</summary>
[Description("The rows of one kind of sub-layer in a section, such as a tropical cyclone's track or an AML family.")]
internal sealed record LegendGroupDto(
    [property: Description("The sub-layer key, e.g. \"weather.tc.track\".")] string Key,
    [property: Description("The sub-layer's name, e.g. \"Tropical cyclone track\".")] string Title,
    [property: Description("The rows, in order.")] IReadOnlyList<LegendRowDto> Rows);

/// <summary>One product's legend.</summary>
[Description("The legend of one product or overlay. Several datasets of a product (seven S-102 tiles) make one section.")]
internal sealed record LegendSectionDto(
    [property: Description("The section id: the product spec (\"S-111\", \"AML\", \"TC\"), or for a weather grid field its sub-layer key (\"weather.wave.height\").")] string Id,
    [property: Description("The product chip: the spec the datasets load under (\"S-111\", \"GRIB2\").")] string Spec,
    [property: Description("The product's name, e.g. \"Surface currents\" or \"Wave height\".")] string Title,
    [property: Description("Where the rows come from: the dataset's name, or \"7 datasets\".")] string Source,
    [property: Description("The session ids of the datasets behind the section, top of the stack first.")] IReadOnlyList<string> Datasets,
    [property: Description("'none' for a product that doesn't vary with time, 'data' when it draws data at the view time, 'no_data' when none of its datasets has data then.")] string TimeState,
    [property: Description("What a time-aware product draws at the view time, as its Datasets row says it (\"08:00Z · T+20 h\", \"no data · last 18:00Z, 6 h earlier\"); null otherwise.")] string? Time,
    [property: Description("A one-line caption on the unit or rule; null for none.")] string? Caption,
    [property: Description("The Mariner settings the rows follow; null for none.")] string? MarinerSettings,
    [property: Description("Whether one of its datasets meets the map view (updated on pan end).")] bool InView,
    [property: Description("Whether the section shows its rows.")] bool Expanded,
    [property: Description("The rows, one group per kind of sub-layer, top of the stack first.")] IReadOnlyList<LegendGroupDto> Groups);

/// <summary>The chart cell at the view centre.</summary>
[Description("The chart cell at the view centre: the finest drawn cell whose coverage contains it, the topmost of peers.")]
internal sealed record PositionLegendCellDto(
    [property: Description("The dataset's session id.")] string DatasetId,
    [property: Description("The cell's name, as the Datasets panel shows it.")] string Name,
    [property: Description("The product it loads under, e.g. \"S-101\" or \"S-57\".")] string Spec,
    [property: Description("The edition number; null when the cell declares none.")] int? Edition,
    [property: Description("The last update applied (0 for none); null with no edition.")] int? Update,
    [property: Description("The issue date (S-57 ISDT, S-101 reference date) as yyyy-MM-dd; null when not declared.")] string? IssueDate,
    [property: Description("The compilation scale denominator: S-57 DSPM CSCL, or for S-101 the DataCoverage maximumDisplayScale; null when not declared.")] int? CompilationScale);

/// <summary>The S-98 position legend: the Legend tab's fixed first row (D17).</summary>
[Description("The S-98 position legend at the view centre (Legend tab's first row): units, datum, safety contour, then the cell there.")]
internal sealed record PositionLegendDto(
    [property: Description("The view centre latitude; null before the map has a view.")] double? Latitude,
    [property: Description("The view centre longitude (-180..180); null before the map has a view.")] double? Longitude,
    [property: Description("The display scale denominator at the centre, as the status bar computes it, rounded; null before the map has a view.")] double? DisplayScale,
    [property: Description("The Mariner depth unit: Metres, Feet, FathomsFeet or Fathoms.")] string DepthUnit,
    [property: Description("The Mariner safety contour in metres.")] double SafetyContourMetres,
    [property: Description("The S-100 vertical datum code at the centre: the cell's SoundingDatum, or with no cell the one datum every depth source in view shares; null when not declared or not known.")] int? DatumCode,
    [property: Description("The datum's name as the row shows it, \"Datum not declared\", or null when the row shows no datum.")] string? Datum,
    [property: Description("The cell at the view centre; omitted over no chart (the row says \"No chart at the view centre\").")] PositionLegendCellDto? Cell,
    [property: Description("How many times finer the display is than the cell's compilation scale; null when not overscaled.")] double? OverscaleFactor,
    [property: Description("The row's two lines as drawn.")] IReadOnlyList<string> Lines);

/// <summary>What the legend shows.</summary>
[Description("The viewer's legends: one section per product drawn, in draw order, top first.")]
internal sealed record LegendDto(
    [property: Description("Whether the right dock's Legend tab is showing.")] bool TabShown,
    [property: Description("The palette the rows are in: Day, Dusk or Night.")] string Palette,
    [property: Description("The S-98 position legend at the view centre, the tab's fixed first row (updated on pan end).")] PositionLegendDto Position,
    [property: Description("The sections, in draw order, top first: only those in the map view unless all was asked for.")] IReadOnlyList<LegendSectionDto> Sections,
    [property: Description("How many loaded products with a legend are not in the map view.")] int NotInViewCount);

/// <summary>Reads the legend (MCP <c>get_legend</c>); headless, it reads the service, not the view.</summary>
internal sealed class GetLegendTool
{
    /// <summary>The MCP tool name.</summary>
    public const string Name = "get_legend";

    private readonly LegendService _service;
    private readonly PositionLegendService _position;
    private readonly IRouteEditInvoker _invoker;

    public GetLegendTool(LegendService service, PositionLegendService position, IRouteEditInvoker invoker)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(position);
        ArgumentNullException.ThrowIfNull(invoker);
        _service = service;
        _position = position;
        _invoker = invoker;
    }

    /// <summary>Takes the current view, rebuilds the sections and the position row on the UI thread and returns them.</summary>
    /// <param name="all">True for every loaded product with a legend; false (default) for those in view.</param>
    /// <param name="ct">Cancels the call.</param>
    public async Task<ToolResult<LegendDto>> InvokeAsync(bool all = false, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var position = await _invoker.InvokeAsync(() =>
        {
            _service.Refresh();
            return _position.RefreshAsync();
        }).ConfigureAwait(false);
        await position.WaitAsync(ct).ConfigureAwait(false);
        var dto = await _invoker.InvokeAsync(() => ToDto(_service, _position.Current, all)).ConfigureAwait(false);
        return ToolResult<LegendDto>.Ok(dto);
    }

    /// <summary>The wire shape of the service's state.</summary>
    internal static LegendDto ToDto(LegendService service, PositionLegend position, bool all)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(position);
        var sections = service.Sections;
        return new LegendDto(
            service.IsTabShown,
            service.Palette.ToString(),
            Position(position),
            [.. sections.Where(s => all || s.IsInView).Select(Section)],
            sections.Count(s => !s.IsInView));
    }

    /// <summary>The wire shape of the position row.</summary>
    internal static PositionLegendDto Position(PositionLegend legend)
    {
        ArgumentNullException.ThrowIfNull(legend);
        return new PositionLegendDto(
            legend.Latitude is double lat ? Math.Round(lat, 6) : null,
            legend.Longitude is double lon ? Math.Round(lon, 6) : null,
            legend.DisplayScale is double scale ? Math.Round(scale) : null,
            legend.DepthUnit.ToString(),
            legend.SafetyContourMetres,
            legend.DatumCode,
            legend.HasDatum ? PositionLegendText.Datum(legend.DatumCode) : null,
            legend.Cell is { } cell
                ? new PositionLegendCellDto(
                    cell.DatasetId,
                    cell.Name,
                    cell.Spec,
                    cell.Edition?.Number,
                    cell.Edition?.Update,
                    cell.Edition?.IssueDate?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                    cell.CompilationScale)
                : null,
            legend.OverscaleFactor is double factor ? Math.Round(factor, 2) : null,
            [PositionLegendText.SettingsLine(legend), PositionLegendText.CellLine(legend)]);
    }

    private static LegendSectionDto Section(LegendSection section) => new(
        section.Id,
        section.Spec,
        section.Title,
        section.Source,
        section.DatasetIds,
        section.TimeState switch
        {
            LegendTimeState.Data => "data",
            LegendTimeState.NoData => "no_data",
            _ => "none",
        },
        section.TimeSummary,
        section.Caption,
        section.MarinerSettings,
        section.IsInView,
        section.IsExpanded,
        [.. section.Groups.Select(g => new LegendGroupDto(g.Key, g.Title, [.. g.Rows.Select(Row)]))]);

    private static LegendRowDto Row(LegendEntry entry) => new(
        LegendRows.KindOf(entry) switch
        {
            LegendRowKind.Swatch => "swatch",
            LegendRowKind.Area => "area",
            LegendRowKind.Symbol => "symbol",
            _ => "line",
        },
        entry.Label,
        entry is SwatchLegendEntry swatch ? swatch.Color : null);
}

/// <summary>Wraps <see cref="GetLegendTool"/> as an MCP server tool.</summary>
internal static class LegendMcpAdapter
{
    /// <summary>Creates <c>get_legend</c>.</summary>
    public static McpServerTool Create(GetLegendTool inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        var del = (
            [Description("True to list every loaded product with a legend, in view or not (the Legend tab's All loaded); false or omitted for those in the map view.")] bool all = false,
            CancellationToken ct = default) =>
            McpAdapterShared.DispatchAsync(() => inner.InvokeAsync(all, ct));
        return McpServerTool.Create(del, new McpServerToolCreateOptions
        {
            Name = GetLegendTool.Name,
            Description =
                "Reads the live viewer's legends: one section per product or overlay drawn (several datasets of a product, "
                + "such as seven S-102 tiles, make one section; each weather grid field is its own section), in draw order, "
                + "top first. Each section gives its id, spec, title, source, datasets, whether it is in the map view, whether "
                + "it is expanded, its time state at the view time ('none', 'data' or 'no_data') with the Datasets row's time "
                + "text, and its rows (kind, label, colour) in groups per sub-layer, in the current palette. By default only "
                + "sections in view; all: true lists every loaded one. Also returns whether the Legend tab is shown and how "
                + "many loaded products are out of view, and the S-98 position legend at the view centre (the tab's fixed first "
                + "row): depth unit, datum, safety contour, and the finest chart cell covering the centre with its edition, "
                + "compilation scale, the display scale and any overscale; cell is omitted over no chart. Read-only; viewer-injected tool.",
            SerializerOptions = McpAdapterShared.Options,
        });
    }
}
