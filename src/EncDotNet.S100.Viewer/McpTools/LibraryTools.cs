using System.ComponentModel;
using System.Globalization;
using EncDotNet.S100.Collections;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.McpTools;

// Viewer-only tools that read the Library through the panel's own view
// models (#715 slice 2).

/// <summary>The Library's collections, as list_library_sources returns them.</summary>
[Description("The Library's collections and their sources.")]
internal sealed record LibrarySourcesDto(
    [property: Description("The collections, in Library order.")] IReadOnlyList<LibraryCollectionInfo> Collections);

/// <summary>The Online Catalogue directory, as list_known_sources returns it.</summary>
[Description("The Online Catalogue directory.")]
internal sealed record KnownSourcesDto(
    [property: Description("The curated entries followed by the user's own (Custom).")] IReadOnlyList<KnownSourceInfo> Sources);

/// <summary>No Library collection or source has the given id.</summary>
[Description("Raised when no Library collection or source has the requested id (call list_library_sources).")]
internal sealed record LibrarySourceNotFound(
    [property: Description("The id that could not be resolved.")] string Id)
    : ToolError("library_source_not_found", $"No Library collection or source has id '{Id}'.");

/// <summary>No Library item has the given id.</summary>
[Description("Raised when no Library item has the requested id (call query_library_items).")]
internal sealed record LibraryItemNotFound(
    [property: Description("The id that could not be resolved.")] string Id)
    : ToolError("library_item_not_found", $"No Library item has id '{Id}'.");

/// <summary>Lists the Library's collections and sources (MCP <c>list_library_sources</c>).</summary>
internal sealed class ListLibrarySourcesTool(IViewerLibraryController library)
{
    /// <summary>The MCP tool name.</summary>
    public const string Name = "list_library_sources";

    private readonly IViewerLibraryController _library = library ?? throw new ArgumentNullException(nameof(library));

    /// <summary>Lists them.</summary>
    public async Task<ToolResult<LibrarySourcesDto>> InvokeAsync(bool? counts, CancellationToken ct = default) =>
        ToolResult<LibrarySourcesDto>.Ok(new LibrarySourcesDto(
            await _library.ListSourcesAsync(counts ?? true, ct).ConfigureAwait(false)));
}

/// <summary>Request for <see cref="QueryLibraryItemsTool"/>.</summary>
internal sealed record QueryLibraryItemsRequest(
    string? SourceId,
    IReadOnlyList<string>? States,
    string? Spec,
    string? Text,
    double? South,
    double? West,
    double? North,
    double? East,
    double? Lat,
    double? Lon,
    int? Page,
    int? PageSize,
    string? ValidAt = null);

/// <summary>Finds Library items (MCP <c>query_library_items</c>).</summary>
internal sealed class QueryLibraryItemsTool(IViewerLibraryController library)
{
    /// <summary>The MCP tool name.</summary>
    public const string Name = "query_library_items";

    /// <summary>Items per page when none is given.</summary>
    public const int DefaultPageSize = 50;

    /// <summary>The largest page allowed.</summary>
    public const int MaxPageSize = 500;

    private readonly IViewerLibraryController _library = library ?? throw new ArgumentNullException(nameof(library));

    /// <summary>Runs the query.</summary>
    public async Task<ToolResult<LibraryItemPage>> InvokeAsync(QueryLibraryItemsRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var (query, error) = Parse(request);
        if (error is not null)
            return ToolResult<LibraryItemPage>.Err(error);

        var result = await _library.QueryItemsAsync(query!, ct).ConfigureAwait(false);
        return result is null
            ? ToolResult<LibraryItemPage>.Err(new LibrarySourceNotFound(request.SourceId!.Trim()))
            : ToolResult<LibraryItemPage>.Ok(result);
    }

    /// <summary>
    /// Validates the filters and page of <paramref name="request"/> (shared
    /// with library_action, which selects items the same way).
    /// </summary>
    internal static (LibraryItemQuery? Query, ToolError? Error) Parse(QueryLibraryItemsRequest request)
    {
        Guid? sourceId = null;
        if (!string.IsNullOrWhiteSpace(request.SourceId))
        {
            if (!Guid.TryParse(request.SourceId.Trim(), out var parsed))
                return (null, new InvalidArgument("sourceId", "expected a collection or source id from list_library_sources"));
            sourceId = parsed;
        }

        HashSet<string>? states = null;
        if (request.States is { Count: > 0 })
        {
            states = new HashSet<string>(StringComparer.Ordinal);
            foreach (var raw in request.States)
            {
                var state = raw.Trim().ToLowerInvariant() switch
                {
                    "updates" => "update",
                    "onpan" or "on pan" => "on_pan",
                    var other => other,
                };
                if (!ViewerLibraryController.StateNames.Contains(state))
                {
                    return (null, new InvalidArgument(
                        "states", $"unknown state '{raw}'; expected {string.Join(", ", ViewerLibraryController.StateNames.Order())}"));
                }
                states.Add(state);
            }
        }

        var boxValues = new[] { request.South, request.West, request.North, request.East };
        GeoBounds? bounds = null;
        if (boxValues.Any(v => v is not null))
        {
            if (boxValues.Any(v => v is null))
                return (null, new InvalidArgument("south", "supply all of south, west, north and east, or none"));
            bounds = new GeoBounds(request.South!.Value, request.West!.Value, request.North!.Value, request.East!.Value);
        }

        GeoPosition? point = null;
        if (request.Lat is not null || request.Lon is not null)
        {
            if (request.Lat is not { } lat || request.Lon is not { } lon)
                return (null, new InvalidArgument("lat", "supply both lat and lon, or neither"));
            point = new GeoPosition(lat, lon);
        }

        // #711 G2: data valid at the Timeline's view time, or at a time.
        DateTime? validAt = null;
        var atViewTime = false;
        if (!string.IsNullOrWhiteSpace(request.ValidAt))
        {
            if (string.Equals(request.ValidAt.Trim(), "view_time", StringComparison.OrdinalIgnoreCase))
                atViewTime = true;
            else if (DateTime.TryParse(request.ValidAt, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var at))
                validAt = at;
            else
                return (null, new InvalidArgument("validAt", "expected 'view_time' or an ISO-8601 time"));
        }

        var page = request.Page ?? 0;
        if (page < 0)
            return (null, new InvalidArgument("page", "must be 0 or more"));
        var pageSize = request.PageSize ?? DefaultPageSize;
        if (pageSize is < 1 or > MaxPageSize)
            return (null, new InvalidArgument("pageSize", $"must be between 1 and {MaxPageSize}"));

        return (new LibraryItemQuery(
            sourceId,
            states,
            string.IsNullOrWhiteSpace(request.Spec) ? null : request.Spec.Trim(),
            string.IsNullOrWhiteSpace(request.Text) ? null : request.Text.Trim(),
            bounds,
            point,
            page,
            pageSize)
        {
            ValidAt = validAt,
            ValidAtViewTime = atViewTime,
        }, null);
    }

    /// <summary>True when <paramref name="request"/> sets any filter (not just a page).</summary>
    internal static bool HasFilter(QueryLibraryItemsRequest request) =>
        !string.IsNullOrWhiteSpace(request.SourceId)
        || request.States is { Count: > 0 }
        || !string.IsNullOrWhiteSpace(request.Spec)
        || !string.IsNullOrWhiteSpace(request.Text)
        || request.South is not null || request.West is not null || request.North is not null || request.East is not null
        || request.Lat is not null || request.Lon is not null
        || !string.IsNullOrWhiteSpace(request.ValidAt);
}

/// <summary>Describes one Library item as its details pane does (MCP <c>describe_library_item</c>).</summary>
internal sealed class DescribeLibraryItemTool(IViewerLibraryController library)
{
    /// <summary>The MCP tool name.</summary>
    public const string Name = "describe_library_item";

    private readonly IViewerLibraryController _library = library ?? throw new ArgumentNullException(nameof(library));

    /// <summary>Describes it.</summary>
    public async Task<ToolResult<LibraryItemDetail>> InvokeAsync(string itemId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(itemId))
            return ToolResult<LibraryItemDetail>.Err(new InvalidArgument("itemId", "value is required; call query_library_items for the ids"));
        var detail = await _library.DescribeItemAsync(itemId.Trim(), ct).ConfigureAwait(false);
        return detail is null
            ? ToolResult<LibraryItemDetail>.Err(new LibraryItemNotFound(itemId.Trim()))
            : ToolResult<LibraryItemDetail>.Ok(detail);
    }
}

/// <summary>Lists the Online Catalogue directory (MCP <c>list_known_sources</c>).</summary>
internal sealed class ListKnownSourcesTool(IViewerLibraryController library)
{
    /// <summary>The MCP tool name.</summary>
    public const string Name = "list_known_sources";

    private readonly IViewerLibraryController _library = library ?? throw new ArgumentNullException(nameof(library));

    /// <summary>Lists them.</summary>
    public async Task<ToolResult<KnownSourcesDto>> InvokeAsync(CancellationToken ct = default) =>
        ToolResult<KnownSourcesDto>.Ok(new KnownSourcesDto(await _library.ListKnownSourcesAsync(ct).ConfigureAwait(false)));
}
