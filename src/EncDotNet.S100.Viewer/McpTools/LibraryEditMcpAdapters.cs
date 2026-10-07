using System.ComponentModel;
using EncDotNet.S100.Viewer.Services;
using ModelContextProtocol.Server;

namespace EncDotNet.S100.Viewer.McpTools;

/// <summary>
/// Wraps the Library write tools (<see cref="AddLibrarySourceTool"/>,
/// <see cref="RefreshLibrarySourceTool"/>, <see cref="LibraryActionTool"/>,
/// <see cref="RemoveLibrarySourceTool"/>, <see cref="AwaitLibraryIdleTool"/>)
/// as MCP server tools (#715).
/// </summary>
internal static class LibraryEditMcpAdapters
{
    /// <summary>Creates <c>add_library_source</c>.</summary>
    public static McpServerTool Create(AddLibrarySourceTool inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        var del = (
            [Description("A list_known_sources id, e.g. 'noaa-enc', 'noaa-s111', 'usace-ienc-rivers'.")] string? knownSourceId = null,
            [Description("A local folder, exchange set (folder, ZIP or catalogue), collection manifest (*.s100collection.json) or S-128 catalogue.")] string? path = null,
            [Description("An online catalogue or feed URL; its format is detected.")] string? url = null,
            [Description("For a path: folder, exchange_set, manifest or s128 (inferred when omitted).")] string? kind = null,
            [Description("Choice values or labels to include, from a preview (states, districts, rivers, forecast models, areas, groups, products, charts).")] string[]? choices = null,
            [Description("True to include everything the catalogue lists (the default without choices).")] bool? includeAll = null,
            [Description("An existing collection id to add to; omit to create a new collection.")] string? collectionId = null,
            [Description("The new collection's name (default: the dialog's).")] string? collectionName = null,
            [Description("For a forecast feed: 'tiles' (default) or 'regional'.")] string? shape = null,
            [Description("For an S-100 catalogue with several resolutions: one from the preview.")] string? resolution = null,
            [Description("True to load the catalogue and report its choices without adding anything.")] bool? preview = null,
            [Description("For a SECOM service: true to keep every object downloaded and pruned on each refresh, false not to (default: on when small).")] bool? sync = null,
            [Description("For a SECOM service: true to read only the objects in the current map view.")] bool? inMapView = null,
            [Description("True to keep the source's local datasets on the map (loading as you pan, one Datasets row), false not to (default: on for a synced SECOM service).")] bool? showOnMap = null,
            CancellationToken ct = default) =>
        {
            Guid? target = null;
            if (!string.IsNullOrWhiteSpace(collectionId))
            {
                if (!Guid.TryParse(collectionId.Trim(), out var parsed))
                {
                    return McpAdapterShared.DispatchAsync(() => Task.FromResult(
                        Datasets.Pipelines.Query.ToolResult<AddSourceResult>.Err(new Datasets.Pipelines.Query.InvalidArgument(
                            "collectionId", "expected a collection id from list_library_sources"))));
                }
                target = parsed;
            }
            return McpAdapterShared.DispatchAsync(() => inner.InvokeAsync(new AddSourceRequest(
                knownSourceId, path, url, kind, choices, includeAll, target, collectionName, shape, resolution, preview == true,
                sync, inMapView == true, showOnMap), ct));
        };
        return Tool(del, AddLibrarySourceTool.Name,
            "Adds a source to the live viewer's Library through the Add-to-Library dialog's own logic: a known online "
            + "source (knownSourceId from list_known_sources), a catalogue or feed URL, or a local path. Call with "
            + "preview: true first to load the catalogue and see its choices (NOAA states / districts / regions, USACE "
            + "rivers, S-111 forecast models, S-102 areas, manifest groups, feed products) with sizes, plus forecast "
            + "shapes, resolutions and existing collections. Then call again with 'choices' (values or labels) or "
            + "includeAll, and optionally collectionId or collectionName. Adding only indexes the catalogue; nothing is "
            + "downloaded (use library_action), except that a SECOM service added with sync keeps every object "
            + "downloaded (inMapView narrows it to the current map view). Returns the new collection and source ids. "
            + "Mutating; viewer-injected tool.");
    }

    /// <summary>Creates <c>refresh_library_source</c>.</summary>
    public static McpServerTool Create(RefreshLibrarySourceTool inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        var del = (
            [Description("A collection or source id; omit to refresh the whole Library.")] string? id = null,
            [Description("How long to wait for re-indexing, in ms (default 60000, max 600000; 0 returns at once).")] int? waitMs = null,
            CancellationToken ct = default) =>
            McpAdapterShared.DispatchAsync(() => inner.InvokeAsync(id, waitMs, ct));
        return Tool(del, RefreshLibrarySourceTool.Name,
            "Re-indexes a Library collection or source (or everything), as the panel's Refresh does: online "
            + "catalogues are checked again, so new editions, new forecast runs and expired runs appear. Waits for "
            + "indexing, then reports what changed: items added and removed, items whose state changed (by new state, "
            + "e.g. 'update', 'expired') and counts by state. Mutating; viewer-injected tool.");
    }

    /// <summary>Creates <c>library_action</c>.</summary>
    public static McpServerTool Create(LibraryActionTool inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        var del = (
            [Description("load (open now), load_as_you_pan (open as the map pans to them), download (then load), download_only, update (download newer editions or runs), or cancel (running downloads).")] string action,
            [Description("Item ids from query_library_items. Use these or the filters below.")] string[]? itemIds = null,
            [Description("Filter: a collection or source id.")] string? sourceId = null,
            [Description("Filter: states, as query_library_items (online, local, loaded, on_pan, update, expired, missing, listed).")] string[]? states = null,
            [Description("Filter: product specification, e.g. 'S-111'.")] string? spec = null,
            [Description("Filter: text, as the Library filter box.")] string? text = null,
            [Description("Filter box: southern latitude (with west, north, east).")] double? south = null,
            [Description("Filter box: western longitude.")] double? west = null,
            [Description("Filter box: northern latitude.")] double? north = null,
            [Description("Filter box: eastern longitude.")] double? east = null,
            [Description("Filter point: latitude (with lon); items covering it.")] double? lat = null,
            [Description("Filter point: longitude.")] double? lon = null,
            [Description("For cancel only: true cancels every running download.")] bool? all = null,
            [Description("True reports what would happen (count, bytes, skipped by state) without doing it.")] bool? dryRun = null,
            [Description("Refuse a download larger than this many bytes.")] long? maxBytes = null,
            CancellationToken ct = default) =>
            McpAdapterShared.DispatchAsync(() => inner.InvokeAsync(new LibraryActionToolRequest(
                action,
                itemIds,
                new QueryLibraryItemsRequest(sourceId, states, spec, text, south, west, north, east, lat, lon, null, null),
                all,
                dryRun,
                maxBytes), ct));
        return Tool(del, LibraryActionTool.Name,
            "Acts on Library items as the panel's buttons do. Select items by id or with the query_library_items "
            + "filters; items the action does not apply to are skipped and counted by state. Downloads can be large: "
            + "call with dryRun: true first to see the count and bytes, confirm with the user, and pass maxBytes. "
            + "Downloads (and the loads after them) continue in the background (await_library_idle waits for them); load returns how many "
            + "datasets opened. Mutating; viewer-injected tool.");
    }

    /// <summary>Creates <c>remove_library_source</c>.</summary>
    public static McpServerTool Create(RemoveLibrarySourceTool inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        var del = (
            [Description("The collection or source id to remove.")] string id,
            [Description("Must be true: removing cannot be undone from here.")] bool? confirm = null,
            CancellationToken ct = default) =>
            McpAdapterShared.DispatchAsync(() => inner.InvokeAsync(id, confirm, ct));
        return Tool(del, RemoveLibrarySourceTool.Name,
            "REMOVES a collection or one source from the live viewer's Library, as the panel's Remove does, and "
            + "deletes its cached index. Downloaded files stay on disk, but the Library no longer lists them. Requires "
            + "confirm: true; confirm with the user first. Mutating; viewer-injected tool.");
    }

    /// <summary>Creates <c>await_library_idle</c>.</summary>
    public static McpServerTool Create(AwaitLibraryIdleTool inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        var del = (
            [Description("How long to wait, in ms (default 60000, max 600000; 0 only reports).")] int? timeoutMs = null,
            CancellationToken ct = default) =>
            McpAdapterShared.DispatchAsync(() => inner.InvokeAsync(timeoutMs, ct));
        return Tool(del, AwaitLibraryIdleTool.Name,
            "Waits until the Library is idle: no source indexing, no download running, and every dataset a "
            + "library_action download or load opens is open. Returns whether it is idle, whether the wait timed out, "
            + "loading (datasets still to open, including any still downloading) and the running download batch's "
            + "progress (items and bytes done, failed). Use after add_library_source, refresh_library_source or a "
            + "library_action download. "
            + "Read-only; viewer-injected tool.");
    }

    private static McpServerTool Tool(Delegate del, string name, string description) =>
        McpServerTool.Create(del, new McpServerToolCreateOptions
        {
            Name = name,
            Description = description,
            SerializerOptions = McpAdapterShared.Options,
        });
}
