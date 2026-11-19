using ModelContextProtocol.Server;

namespace EncDotNet.S100.Mcp;

/// <summary>
/// The named sets of MCP tools a host can expose (#1608), so a client sees only
/// the tools its task needs: every tool's description and schema sits in the
/// agent's context before it does anything.
/// </summary>
/// <remarks>
/// <para>
/// Every tool belongs to exactly one toolset. The code that creates a tool
/// declares it with <see cref="Assign(string, IEnumerable{McpServerTool})"/>;
/// <see cref="Select"/>, which every host runs over its tools, refuses a tool
/// with none. The toolset travels in the tool's in-process
/// <see cref="McpServerTool.Metadata"/> only, so it adds nothing to
/// <c>tools/list</c>.
/// </para>
/// <para>
/// Hosts take a selection as toolset and profile names (<see cref="TryParse(string?, out IReadOnlyList{string}?, out string?)"/>);
/// no selection means every toolset.
/// </para>
/// </remarks>
public static class McpToolsets
{
    /// <summary>Read the loaded datasets: list, describe, find, identify, query, search and sample.</summary>
    public const string Query = "query";

    /// <summary>Open and close datasets by path, and add live data sources.</summary>
    public const string Datasets = "datasets";

    /// <summary>Palette, display category and mode, time step, viewport and render to an image.</summary>
    public const string Presentation = "presentation";

    /// <summary>Check routes, and in SoundCharts create, edit, import and export them.</summary>
    public const string Routes = "routes";

    /// <summary>SoundCharts' passage check: check mode, departure, findings, clearance and route weather settings.</summary>
    public const string Passage = "passage";

    /// <summary>Tide and current stations and their predictions.</summary>
    public const string Tides = "tides";

    /// <summary>The Library: sources, items, downloads and SECOM services.</summary>
    public const string Library = "library";

    /// <summary>SoundCharts' navigational warnings panel and display.</summary>
    public const string Warnings = "warnings";

    /// <summary>SoundCharts' display settings: weather, bathymetry, AML, tides, depth basis, display combinations and the legend.</summary>
    public const string Display = "display";

    /// <summary>SoundCharts' map: viewport, pick, timeline, dataset state, notifications, own ship, measure and range/bearing.</summary>
    public const string Map = "map";

    /// <summary>SoundCharts' window: panels, app screenshots and, with test hooks on, UI automation and the test clock.</summary>
    public const string Ui = "ui";

    /// <summary>SoundCharts' render and performance diagnostics and recent logs.</summary>
    public const string Diagnostics = "diagnostics";

    /// <summary>The name that selects every toolset.</summary>
    public const string AllName = "all";

    /// <summary>The route-planning profile: what an agent planning and checking routes needs.</summary>
    public const string RoutePlanningProfile = "route-planning";

    /// <summary>The visual-check profile: what an agent checking how the chart looks needs.</summary>
    public const string VisualCheckProfile = "visual-check";

    /// <summary>Every toolset, in display order, with a one-line summary.</summary>
    public static IReadOnlyList<McpToolsetInfo> All { get; } =
    [
        new(Query, "Read the loaded datasets: list, describe, find, identify, query, search and sample."),
        new(Datasets, "Open and close datasets by path, and add live data sources."),
        new(Presentation, "Palette, display category and mode, time step, viewport and render to an image."),
        new(Routes, "Check routes; in SoundCharts also create, edit, import and export them."),
        new(Passage, "SoundCharts' passage check: check mode, departure, findings, clearance, route weather settings."),
        new(Tides, "Tide and current stations and their predictions."),
        new(Library, "The Library: sources, items, downloads and SECOM services."),
        new(Warnings, "SoundCharts' navigational warnings panel and display."),
        new(Display, "SoundCharts' display settings: weather, bathymetry, AML, tides, depth basis, display combinations, legend."),
        new(Map, "SoundCharts' map: viewport, pick, timeline, dataset state, notifications, own ship, measure, range/bearing."),
        new(Ui, "SoundCharts' window: panels, app screenshots, and with test hooks UI automation and the test clock."),
        new(Diagnostics, "SoundCharts' render and performance diagnostics and recent logs."),
    ];

    /// <summary>Named selections of toolsets, accepted wherever toolset names are.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> Profiles { get; } =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [RoutePlanningProfile] = [Query, Datasets, Routes, Passage, Tides],
            [VisualCheckProfile] = [Query, Datasets, Presentation, Display, Map, Ui],
        };

    private static readonly HashSet<string> Names = new(All.Select(t => t.Name), StringComparer.Ordinal);

    /// <summary>True when <paramref name="name"/> is a toolset name (not a profile).</summary>
    /// <param name="name">The name to test.</param>
    public static bool IsToolset(string name) => Names.Contains(name);

    /// <summary>Declares that <paramref name="tools"/> belong to <paramref name="toolset"/>.</summary>
    /// <param name="toolset">One of the toolset names on this type.</param>
    /// <param name="tools">The tools.</param>
    /// <returns>The tools, each carrying the toolset.</returns>
    /// <exception cref="ArgumentException"><paramref name="toolset"/> is not a toolset name.</exception>
    public static IReadOnlyList<McpServerTool> Assign(string toolset, IEnumerable<McpServerTool> tools)
    {
        ArgumentNullException.ThrowIfNull(tools);
        return [.. tools.Select(tool => Assign(toolset, tool))];
    }

    /// <summary>Declares that <paramref name="tool"/> belongs to <paramref name="toolset"/>.</summary>
    /// <param name="toolset">One of the toolset names on this type.</param>
    /// <param name="tool">The tool.</param>
    /// <returns>The tool, carrying the toolset; a tool that already has one gets the new one.</returns>
    /// <exception cref="ArgumentException"><paramref name="toolset"/> is not a toolset name.</exception>
    public static McpServerTool Assign(string toolset, McpServerTool tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        if (!IsToolset(toolset))
            throw new ArgumentException($"'{toolset}' is not an MCP toolset.", nameof(toolset));
        return new ToolsetTool(tool is ToolsetTool tagged ? tagged.Inner : tool, toolset);
    }

    /// <summary>
    /// Declares that those of <paramref name="tools"/> that don't yet declare a
    /// toolset belong to <paramref name="toolset"/>: a group's default, with
    /// per-tool exceptions assigned first.
    /// </summary>
    /// <param name="toolset">One of the toolset names on this type.</param>
    /// <param name="tools">The tools.</param>
    /// <returns>The tools, each carrying a toolset.</returns>
    /// <exception cref="ArgumentException"><paramref name="toolset"/> is not a toolset name.</exception>
    public static IEnumerable<McpServerTool> AssignDefault(string toolset, IEnumerable<McpServerTool> tools)
    {
        ArgumentNullException.ThrowIfNull(tools);
        if (!IsToolset(toolset))
            throw new ArgumentException($"'{toolset}' is not an MCP toolset.", nameof(toolset));
        return tools.Select(tool => Of(tool) is null ? Assign(toolset, tool) : tool);
    }

    /// <summary>The toolset <paramref name="tool"/> was declared in, or <see langword="null"/>.</summary>
    /// <param name="tool">The tool.</param>
    public static string? Of(McpServerTool tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        return tool.Metadata.OfType<McpToolsetMarker>().FirstOrDefault()?.Toolset;
    }

    /// <summary>
    /// Keeps the tools in the selected toolsets, checking first that every tool
    /// declares one.
    /// </summary>
    /// <param name="tools">Every tool the host could serve.</param>
    /// <param name="toolsets">The selected toolsets; <see langword="null"/> for all of them.</param>
    /// <returns>The selected tools, in their original order.</returns>
    /// <exception cref="InvalidOperationException">A tool declares no toolset.</exception>
    public static IReadOnlyList<McpServerTool> Select(IEnumerable<McpServerTool> tools, IReadOnlyCollection<string>? toolsets)
    {
        ArgumentNullException.ThrowIfNull(tools);
        var list = tools.ToList();
        var undeclared = list.Where(t => Of(t) is null).Select(t => t.ProtocolTool.Name).ToList();
        if (undeclared.Count > 0)
            throw new InvalidOperationException(
                $"MCP tools without a toolset: {string.Join(", ", undeclared)}. Declare one with McpToolsets.Assign.");
        if (toolsets is null)
            return list;
        var selected = new HashSet<string>(toolsets, StringComparer.Ordinal);
        return [.. list.Where(t => selected.Contains(Of(t)!))];
    }

    /// <summary>
    /// Reads a comma-separated list of toolset and profile names, such as
    /// <c>query,routes,tides</c> or <c>route-planning,library</c>.
    /// </summary>
    /// <param name="text">The list; names are case-insensitive, and <c>all</c> selects every toolset.</param>
    /// <param name="toolsets">The toolsets selected, in display order; <see langword="null"/> for all.</param>
    /// <param name="error">Why the list was refused.</param>
    /// <returns>True when every name is known.</returns>
    public static bool TryParse(string? text, out IReadOnlyList<string>? toolsets, out string? error) =>
        TryParse(text?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [], out toolsets, out error);

    /// <summary>Reads toolset and profile names.</summary>
    /// <param name="names">The names; case-insensitive, and <c>all</c> selects every toolset.</param>
    /// <param name="toolsets">The toolsets selected, in display order; <see langword="null"/> for all.</param>
    /// <param name="error">Why the names were refused.</param>
    /// <returns>True when every name is known and at least one was given.</returns>
    public static bool TryParse(IEnumerable<string> names, out IReadOnlyList<string>? toolsets, out string? error)
    {
        ArgumentNullException.ThrowIfNull(names);
        toolsets = null;
        error = null;
        var selected = new HashSet<string>(StringComparer.Ordinal);
        var any = false;
        foreach (var raw in names)
        {
            var name = raw.Trim().ToLowerInvariant();
            if (name.Length == 0)
                continue;
            any = true;
            if (name == AllName)
                return true;
            if (IsToolset(name))
                selected.Add(name);
            else if (Profiles.TryGetValue(name, out var profile))
                selected.UnionWith(profile);
            else
            {
                error = $"Unknown MCP toolset '{raw.Trim()}'. Toolsets: {string.Join(", ", All.Select(t => t.Name))}; profiles: {string.Join(", ", Profiles.Keys)}; or {AllName}.";
                return false;
            }
        }

        if (!any)
        {
            error = "Name at least one MCP toolset or profile, or all.";
            return false;
        }

        toolsets = [.. All.Select(t => t.Name).Where(selected.Contains)];
        return true;
    }

    private sealed record McpToolsetMarker(string Toolset);

    private sealed class ToolsetTool : DelegatingMcpServerTool
    {
        private readonly IReadOnlyList<object> _metadata;

        public ToolsetTool(McpServerTool inner, string toolset)
            : base(inner)
        {
            Inner = inner;
            _metadata = [.. inner.Metadata.Where(m => m is not McpToolsetMarker), new McpToolsetMarker(toolset)];
        }

        public McpServerTool Inner { get; }

        public override IReadOnlyList<object> Metadata => _metadata;
    }
}

/// <summary>A toolset's name and a one-line summary of what its tools do.</summary>
/// <param name="Name">The name hosts select it by.</param>
/// <param name="Summary">What its tools do.</param>
public sealed record McpToolsetInfo(string Name, string Summary);
