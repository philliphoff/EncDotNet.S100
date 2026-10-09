using System.ComponentModel;
using EncDotNet.S100.Mcp.Tools.Library;
using ModelContextProtocol.Server;

namespace EncDotNet.S100.Mcp.Library;

/// <summary>
/// Wraps the Library read tools (<see cref="ListLibrarySourcesTool"/>,
/// <see cref="QueryLibraryItemsTool"/>, <see cref="DescribeLibraryItemTool"/>,
/// <see cref="ListKnownSourcesTool"/>) as MCP server tools (#715), for every host (#792).
/// </summary>
public static class LibraryMcpAdapters
{
    /// <summary>Creates <c>list_library_sources</c>.</summary>
    public static McpServerTool Create(ListLibrarySourcesTool inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        var del = (
            [Description("True (default) counts each source's items by state; false skips reading every item, for very large Libraries.")] bool? counts = null,
            CancellationToken ct = default) =>
            McpToolDispatch.DispatchAsync(() => inner.InvokeAsync(counts, ct));
        return Tool(del, ListLibrarySourcesTool.Name,
            "Lists the Library: each collection (a top-level node) with its kind tag (DIR, ZIP, WEB, AWS, "
            + "LIST, FEED, JSON, S-128), item count and status line, and each source with its index state, when its "
            + "index was built (how stale a cached online catalogue is), URL (shared-feed tokens masked) and item counts "
            + "by state (online, local, loaded, on_pan, update, expired, missing, listed). Use the ids with "
            + "query_library_items. Read-only.");
    }

    /// <summary>Creates <c>query_library_items</c>.</summary>
    public static McpServerTool Create(QueryLibraryItemsTool inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        var del = (
            [Description("A collection or source id from list_library_sources; omit for the whole Library.")] string? sourceId = null,
            [Description("States to keep: online, local, loaded, on_pan, update, expired, missing, listed. Omit for all.")] string[]? states = null,
            [Description("Product specification to keep, e.g. 'S-101', 'S-111'.")] string? spec = null,
            [Description("Text matched against name, title and properties, as the Library's filter box does.")] string? text = null,
            [Description("Southern latitude of a box items must intersect (with west, north, east).")] double? south = null,
            [Description("Western longitude of the box.")] double? west = null,
            [Description("Northern latitude of the box.")] double? north = null,
            [Description("Eastern longitude of the box.")] double? east = null,
            [Description("Latitude of a point items must cover (with lon), as tapping the map does; results are then most detailed first.")] double? lat = null,
            [Description("Longitude of the point.")] double? lon = null,
            [Description("0-based page (default 0).")] int? page = null,
            [Description("Items per page, 1–500 (default 50).")] int? pageSize = null,
            [Description("Keep only items whose data covers a time: 'view_time' (the Timeline's view time, as the Library's Valid at view time toggle) or an ISO-8601 time. A forecast's window is its run's (the downloaded copy's, else the catalogue's).")] string? validAt = null,
            CancellationToken ct = default) =>
            McpToolDispatch.DispatchAsync(() => inner.InvokeAsync(
                new QueryLibraryItemsRequest(sourceId, states, spec, text, south, west, north, east, lat, lon, page, pageSize, validAt), ct));
        return Tool(del, QueryLibraryItemsTool.Name,
            "Finds datasets in the Library, paged. Each item reports its id, spec, state as the Library "
            + "row shows it ('online' not downloaded, 'local' on disk, 'loaded', 'on_pan', 'update' newer edition or run "
            + "online, 'expired' forecast run ended, 'missing', 'listed'), tags, edition, issue date, size, bounds, local "
            + "path, and for forecasts the model, run and end of the valid window. Filter by source, states, spec, text, "
            + "a bounding box, or a point (what covers here, most detailed first). Examples: what is out of date "
            + "(states=[update]); which currents cover a position (spec=S-111, lat, lon). Read-only.");
    }

    /// <summary>Creates <c>describe_library_item</c>.</summary>
    public static McpServerTool Create(DescribeLibraryItemTool inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        var del = (
            [Description("Item id from query_library_items ('<sourceId>:<key>').")] string itemId,
            CancellationToken ct = default) =>
            McpToolDispatch.DispatchAsync(() => inner.InvokeAsync(itemId, ct));
        return Tool(del, DescribeLibraryItemTool.Name,
            "Describes one Library item as its details pane does: the item summary plus groups of labelled fields "
            + "(Forecast, Product, Coverage, Source, …). Fails with library_item_not_found for an unknown id. "
            + "Read-only.");
    }

    /// <summary>Creates <c>list_secom_services</c>.</summary>
    public static McpServerTool Create(ListSecomServicesTool inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        var del = (
            [Description("Only services of this product, e.g. 'S-124'; omit for every S-100 data service.")] string? product = null,
            [Description("True to probe each service (at most 60) for whether it can be read without a certificate; slower.")] bool? probe = null,
            CancellationToken ct = default) =>
            McpToolDispatch.DispatchAsync(() => inner.InvokeAsync(product, probe, ct));
        return Tool(del, ListSecomServicesTool.Name,
            "Lists SECOM (IEC 63173-2) data services registered in the MCP service registry: name, organisation, product, "
            + "released or provisional, endpoint and area, cleaned of unusable entries. With probe, each is checked: Open "
            + "(readable without a certificate), OpenWithCertificate (readable with the set_secom_identity identity), "
            + "NeedsCertificate (no identity set), CertificateRefused, UntrustedServer (its TLS certificate is refused) or "
            + "Unreachable. Add an Open or OpenWithCertificate service with add_library_source url=<endpoint>. Read-only.");
    }

    /// <summary>Creates <c>set_secom_identity</c>.</summary>
    public static McpServerTool Create(SetSecomIdentityTool inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        var del = (
            [Description("A PKCS#12 (.p12/.pfx) or PEM file holding an MCP client certificate and its private key; omit to report the current identity.")] string? path = null,
            [Description("The PKCS#12 or encrypted-key password, if any.")] string? password = null,
            [Description("True to clear the identity.")] bool? clear = null,
            CancellationToken ct = default) =>
            McpToolDispatch.DispatchAsync(() => inner.InvokeAsync(path, password, clear, ct));
        return Tool(del, SetSecomIdentityTool.Name,
            "Sets the MCP identity (client certificate) presented to SECOM services that ask for one (mutual TLS), clears "
            + "it, or reports it: subject, MRN, trust anchor and expiry. For this session only; nothing is persisted. An "
            + "expired or not-yet-valid identity is refused. Refresh SECOM sources afterwards to list what it can read.");
    }

    /// <summary>Creates <c>list_known_sources</c>.</summary>
    public static McpServerTool Create(ListKnownSourcesTool inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        var del = (CancellationToken ct = default) =>
            McpToolDispatch.DispatchAsync(() => inner.InvokeAsync(ct));
        return Tool(del, ListKnownSourcesTool.Name,
            "Lists the Online Catalogue directory: the curated online sources (NOAA ENC, USACE Inland ENC, NOAA S-102 / "
            + "S-104 / S-111 on AWS, community lists, …) and any the user added, with provider, region, format, URL, "
            + "whether it lists editions and sizes, product, pilot and not-for-navigation flags, and for forecast feeds "
            + "their models (cadence and horizon). Read-only.");
    }

    private static McpServerTool Tool(Delegate del, string name, string description) =>
        McpServerTool.Create(del, new McpServerToolCreateOptions
        {
            Name = name,
            Description = description,
            SerializerOptions = McpJson.Options,
        });
}
