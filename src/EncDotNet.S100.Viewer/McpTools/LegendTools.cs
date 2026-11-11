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

/// <summary>What the legend shows.</summary>
[Description("The viewer's legends: one section per product drawn, in draw order, top first.")]
internal sealed record LegendDto(
    [property: Description("Whether the right dock's Legend tab is showing.")] bool TabShown,
    [property: Description("The palette the rows are in: Day, Dusk or Night.")] string Palette,
    [property: Description("The sections, in draw order, top first: only those in the map view unless all was asked for.")] IReadOnlyList<LegendSectionDto> Sections,
    [property: Description("How many loaded products with a legend are not in the map view.")] int NotInViewCount);

/// <summary>Reads the legend (MCP <c>get_legend</c>); headless, it reads the service, not the view.</summary>
internal sealed class GetLegendTool
{
    /// <summary>The MCP tool name.</summary>
    public const string Name = "get_legend";

    private readonly LegendService _service;
    private readonly IRouteEditInvoker _invoker;

    public GetLegendTool(LegendService service, IRouteEditInvoker invoker)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(invoker);
        _service = service;
        _invoker = invoker;
    }

    /// <summary>Takes the current view, rebuilds the sections on the UI thread and returns them.</summary>
    /// <param name="all">True for every loaded product with a legend; false (default) for those in view.</param>
    /// <param name="ct">Cancels the call.</param>
    public async Task<ToolResult<LegendDto>> InvokeAsync(bool all = false, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var dto = await _invoker.InvokeAsync(() =>
        {
            _service.Refresh();
            return ToDto(_service, all);
        }).ConfigureAwait(false);
        return ToolResult<LegendDto>.Ok(dto);
    }

    /// <summary>The wire shape of the service's state.</summary>
    internal static LegendDto ToDto(LegendService service, bool all)
    {
        ArgumentNullException.ThrowIfNull(service);
        var sections = service.Sections;
        return new LegendDto(
            service.IsTabShown,
            service.Palette.ToString(),
            [.. sections.Where(s => all || s.IsInView).Select(Section)],
            sections.Count(s => !s.IsInView));
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

/// <summary>Request payload for <see cref="SetLegendTool"/>.</summary>
/// <param name="Open">True shows the Legend tab, false closes it; null leaves it.</param>
/// <param name="InView">True for the tab's In view scope, false for All loaded; null leaves it.</param>
/// <param name="Section">The section id to expand or collapse (and, with <paramref name="Open"/> true, scroll to).</param>
/// <param name="Expanded">True expands <paramref name="Section"/>, false collapses it; null leaves it.</param>
[Description("What set_legend changes; omitted fields are left as they are.")]
internal sealed record SetLegendRequest(
    [property: Description("True shows the Legend tab, false closes it.")] bool? Open = null,
    [property: Description("True for the tab's In view scope, false for All loaded.")] bool? InView = null,
    [property: Description("The section id to expand or collapse, and with open: true to scroll to.")] string? Section = null,
    [property: Description("True expands the section, false collapses it.")] bool? Expanded = null);

/// <summary>What <see cref="SetLegendTool"/> leaves.</summary>
[Description("The legend after set_legend: the tab's scope and what get_legend returns for it.")]
internal sealed record SetLegendResult(
    [property: Description("The Legend tab's scope: 'in_view' or 'all_loaded'.")] string Scope,
    [property: Description("The legend as get_legend returns it, listing the sections the tab's scope shows.")] LegendDto Legend);

/// <summary>
/// Changes the Legend tab (MCP <c>set_legend</c>, legends design note D12):
/// opens or closes it, sets In view or All loaded, and expands or collapses a
/// section. Replaces <c>set_map_legend</c> (#1408).
/// </summary>
internal sealed class SetLegendTool
{
    /// <summary>The MCP tool name.</summary>
    public const string Name = "set_legend";

    private readonly LegendService _service;
    private readonly ViewModels.Legends.LegendViewModel _tab;
    private readonly Services.IViewerUiControllerAccessor _ui;
    private readonly IRouteEditInvoker _invoker;

    public SetLegendTool(LegendService service, ViewModels.Legends.LegendViewModel tab, Services.IViewerUiControllerAccessor ui, IRouteEditInvoker invoker)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(tab);
        ArgumentNullException.ThrowIfNull(ui);
        ArgumentNullException.ThrowIfNull(invoker);
        _service = service;
        _tab = tab;
        _ui = ui;
        _invoker = invoker;
    }

    /// <summary>
    /// Applies the request: the section's expanded state and the scope first,
    /// then the tab (opening at the section when one is named and not
    /// collapsed).
    /// </summary>
    public async Task<ToolResult<SetLegendResult>> InvokeAsync(SetLegendRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();
        var section = request.Section?.Trim();
        if (section is { Length: 0 })
            section = null;
        if (request.Expanded is not null && section is null)
            return ToolResult<SetLegendResult>.Err(new InvalidArgument("section", "expanded needs the section id to expand or collapse"));

        var known = await _invoker.InvokeAsync(() =>
        {
            _service.Refresh();
            if (section is not null && !_service.Sections.Any(s => string.Equals(s.Id, section, StringComparison.Ordinal)))
                return false;
            if (section is not null && request.Expanded is { } expanded)
            {
                _service.SetExpanded(section, expanded);
                _service.Refresh();
            }

            if (request.InView is { } inView)
                _tab.IsAllLoaded = !inView;
            return true;
        }).ConfigureAwait(false);
        if (!known)
        {
            return ToolResult<SetLegendResult>.Err(new InvalidArgument(
                "section", $"no legend section '{section}' is loaded; call get_legend with all: true for the ids"));
        }

        if (request.Open is { } open)
        {
            if (_ui.Current is not { } controller)
                return ToolResult<SetLegendResult>.Err(new UiNotReady("the viewer's UI controller has not been initialised yet"));
            var outcome = await controller.SetPanelVisibilityAsync(ViewModels.ShellLayoutViewModel.LegendTabId, open, ct).ConfigureAwait(false);
            if (open && !outcome.Available)
                return ToolResult<SetLegendResult>.Err(new PanelUnavailable(ViewModels.ShellLayoutViewModel.LegendTabId));
        }

        var result = await _invoker.InvokeAsync(() =>
        {
            if (request.Open == true && section is not null && request.Expanded != false)
                _tab.ShowSection(section);
            _service.Refresh();
            return new SetLegendResult(
                _tab.IsAllLoaded ? "all_loaded" : "in_view",
                GetLegendTool.ToDto(_service, _tab.IsAllLoaded));
        }).ConfigureAwait(false);
        return ToolResult<SetLegendResult>.Ok(result);
    }
}

/// <summary>Wraps the legend tools as MCP server tools.</summary>
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
                + "many loaded products are out of view. Read-only; viewer-injected tool.",
            SerializerOptions = McpAdapterShared.Options,
        });
    }

    /// <summary>Creates <c>set_legend</c>.</summary>
    public static McpServerTool Create(SetLegendTool inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        var del = (
            [Description("True to show the right dock's Legend tab (opening the dock), false to close it (the dock goes back to the tab it showed before, or closes); omit to leave it.")] bool? open = null,
            [Description("True for the tab's In view scope (products in the map view), false for All loaded; omit to leave it.")] bool? inView = null,
            [Description("A section id from get_legend (\"RouteCheck\", \"S-111\", \"weather.wave.height\") to expand or collapse; with open: true the tab also scrolls to it, switching to All loaded or clearing the filter if they hide it.")] string? section = null,
            [Description("True to expand the section, false to collapse it (remembered per section); needs section.")] bool? expanded = null,
            CancellationToken ct = default) =>
            McpAdapterShared.DispatchAsync(() => inner.InvokeAsync(new SetLegendRequest(open, inView, section, expanded), ct));
        return McpServerTool.Create(del, new McpServerToolCreateOptions
        {
            Name = SetLegendTool.Name,
            Description =
                "Changes the live viewer's Legend tab: opens or closes it, sets In view or All loaded, and expands or "
                + "collapses a section. Every argument is optional; omitted ones are left as they are. While the tab is "
                + "shown the Check route map card hides (and the map's legend reserve drops to 0); the card shows the "
                + "same 'RouteCheck' section while Check route is on. Returns the tab's scope and what get_legend returns "
                + "for it. Replaces set_map_legend. Viewer-injected tool — mutates live state.",
            SerializerOptions = McpAdapterShared.Options,
        });
    }
}
