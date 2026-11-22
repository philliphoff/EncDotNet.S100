using System.ComponentModel;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Viewer.Services.ChartReference;
using ModelContextProtocol.Server;

namespace EncDotNet.S100.Viewer.McpTools;

// The Reference tab's MCP tool (#1650, docs/design/symbol-dictionary.md R7).

/// <summary>The result of <c>open_chart_reference</c>: the entry the Reference tab shows.</summary>
[Description("The entry the Reference tab shows, with its trail. key, name, kind and link are null before any entry is opened.")]
internal sealed record ChartReferenceShownDto(
    [property: Description("The entry's key, \"<spec>/<kind>/<code>\", e.g. \"S-101/FeatureType/LateralBeacon\".")] string? Key,
    [property: Description("The entry's catalogue name.")] string? Name,
    [property: Description("feature_type, symbol, line_style, area_fill, attribute or display_setting.")] string? Kind,
    [property: Description("The entry's link, \"soundcharts://reference/<spec>/<kind>/<code>\".")] string? Link,
    [property: Description("Whether the tab has an entry to go back to (Alt+Left).")] bool CanGoBack,
    [property: Description("Whether the tab has an entry to go forward to (Alt+Right).")] bool CanGoForward,
    [property: Description("The breadcrumb: the keys of the last entries opened, the shown one last.")] IReadOnlyList<string> Trail);

/// <summary>
/// Opens an entry in the Reference tab, or reports the one shown (MCP
/// <c>open_chart_reference</c>).
/// </summary>
internal sealed class OpenChartReferenceTool
{
    /// <summary>The MCP tool name.</summary>
    public const string Name = "open_chart_reference";

    private readonly ChartReferenceService _service;
    private readonly ReferenceNavigator _navigator;
    private readonly IRouteEditInvoker _invoker;

    public OpenChartReferenceTool(ChartReferenceService service, ReferenceNavigator navigator, IRouteEditInvoker invoker)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(navigator);
        ArgumentNullException.ThrowIfNull(invoker);
        _service = service;
        _navigator = navigator;
        _invoker = invoker;
    }

    /// <summary>
    /// Opens <paramref name="key"/> in the Reference tab and switches the
    /// right dock to it; with no key, reports the entry shown.
    /// </summary>
    /// <param name="key">A key or <c>soundcharts://reference/…</c> link; null or blank to report only.</param>
    /// <param name="ct">Cancels the call.</param>
    public async Task<ToolResult<ChartReferenceShownDto>> InvokeAsync(string? key = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!string.IsNullOrWhiteSpace(key))
        {
            if (!ReferenceNavigator.TryParse(key, out var refKey))
                return ToolResult<ChartReferenceShownDto>.Err(new InvalidArgument(nameof(key),
                    "key must be <spec>/<kind>/<code> or soundcharts://reference/<spec>/<kind>/<code>, e.g. S-101/FeatureType/LateralBeacon"));
            var index = await _service.GetIndexAsync(refKey.Spec, ct).ConfigureAwait(false);
            if (index is null)
                return ToolResult<ChartReferenceShownDto>.Err(new ChartReferenceProductNotFound(refKey.Spec.Name));
            if (!index.TryGet(refKey, out var entry))
                return ToolResult<ChartReferenceShownDto>.Err(new ChartReferenceEntryNotFound(key.Trim()));
            // The entry's own key, so the code is in the catalogue's case.
            await _invoker.InvokeAsync(() =>
            {
                _navigator.Open(entry.Key);
                return true;
            }).ConfigureAwait(false);
        }

        var (current, back, forward, trail) = await _invoker.InvokeAsync(
            () => (_navigator.Current, _navigator.CanGoBack, _navigator.CanGoForward, _navigator.Breadcrumb.Select(c => c.Key.ToString()).ToList()))
            .ConfigureAwait(false);
        if (current is not { } shown)
            return ToolResult<ChartReferenceShownDto>.Ok(new ChartReferenceShownDto(null, null, null, null, back, forward, trail));

        var shownIndex = await _service.GetIndexAsync(shown.Spec, ct).ConfigureAwait(false);
        string? name = shownIndex is not null && shownIndex.TryGet(shown, out var shownEntry) ? shownEntry.Name : null;
        return ToolResult<ChartReferenceShownDto>.Ok(new ChartReferenceShownDto(
            shown.ToString(),
            name,
            ChartReferenceWire.Kind(shown.Kind),
            ReferenceNavigator.LinkOf(shown),
            back,
            forward,
            trail));
    }

    /// <summary>Creates the MCP tool.</summary>
    public McpServerTool CreateTool()
    {
        var del = (
            [Description("The entry to open: a key from search_chart_reference, \"<spec>/<kind>/<code>\" (e.g. \"S-101/FeatureType/LateralBeacon\", \"S-101/Symbol/BCNLAT21\"), or the link \"soundcharts://reference/S-101/Symbol/BCNLAT21\". The code matches without regard to case. Omit to report the entry shown without changing it.")] string? key = null,
            CancellationToken ct = default) =>
            McpAdapterShared.DispatchAsync(() => InvokeAsync(key, ct));
        return McpServerTool.Create(del, new McpServerToolCreateOptions
        {
            Name = Name,
            Description =
                "Opens a chart reference entry in the viewer's Reference tab, in the right dock, and switches the dock to that "
                + "tab, as selecting a row in Chart reference does; Object information keeps its pick. Opening adds the entry "
                + "to the tab's back/forward history. Returns the entry shown (key, name, kind, its soundcharts:// link), "
                + "whether back and forward are possible, and the breadcrumb. Without a key it only reports what is shown. "
                + "Returns chart_reference_entry_not_found for a key that names no entry. Viewer-injected tool.",
            SerializerOptions = McpAdapterShared.Options,
        });
    }
}
