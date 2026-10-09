using System.ComponentModel;
using System.Globalization;
using EncDotNet.S100.Collections;
using EncDotNet.S100.Collections.Library;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines.Query;

namespace EncDotNet.S100.Mcp.Tools.Library;

// The Library read tools (#715 slice 2), shared by every host since #792.

/// <summary>The Library's collections, as list_library_sources returns them.</summary>
[Description("The Library's collections and their sources.")]
public sealed record LibrarySourcesDto(
    [property: Description("The collections, in Library order.")] IReadOnlyList<LibraryCollectionInfo> Collections);

/// <summary>The Online Catalogue directory, as list_known_sources returns it.</summary>
[Description("The Online Catalogue directory.")]
public sealed record KnownSourcesDto(
    [property: Description("The curated entries followed by the user's own (Custom).")] IReadOnlyList<KnownSourceInfo> Sources);

/// <summary>No Library collection or source has the given id.</summary>
[Description("Raised when no Library collection or source has the requested id (call list_library_sources).")]
public sealed record LibrarySourceNotFound(
    [property: Description("The id that could not be resolved.")] string Id)
    : ToolError("library_source_not_found", $"No Library collection or source has id '{Id}'.");

/// <summary>No Library item has the given id.</summary>
[Description("Raised when no Library item has the requested id (call query_library_items).")]
public sealed record LibraryItemNotFound(
    [property: Description("The id that could not be resolved.")] string Id)
    : ToolError("library_item_not_found", $"No Library item has id '{Id}'.");

/// <summary>Lists the Library's collections and sources (MCP <c>list_library_sources</c>).</summary>
public sealed class ListLibrarySourcesTool(ILibraryReader library)
{
    /// <summary>The MCP tool name.</summary>
    public const string Name = "list_library_sources";

    private readonly ILibraryReader _library = library ?? throw new ArgumentNullException(nameof(library));

    /// <summary>Lists them.</summary>
    public async Task<ToolResult<LibrarySourcesDto>> InvokeAsync(bool? counts, CancellationToken ct = default) =>
        ToolResult<LibrarySourcesDto>.Ok(new LibrarySourcesDto(
            await _library.ListSourcesAsync(counts ?? true, ct).ConfigureAwait(false)));
}

/// <summary>Request for <see cref="QueryLibraryItemsTool"/>.</summary>
public sealed record QueryLibraryItemsRequest(
    [property: Description("A collection or source id from list_library_sources; null for the whole Library.")] string? SourceId,
    [property: Description("States to keep: online, local, loaded, on_pan, update, expired, missing, listed; null for all.")] IReadOnlyList<string>? States,
    [property: Description("Product specification to keep, e.g. 'S-101'.")] string? Spec,
    [property: Description("Text matched against name, title and properties.")] string? Text,
    [property: Description("Southern latitude of a box items must intersect.")] double? South,
    [property: Description("Western longitude of the box.")] double? West,
    [property: Description("Northern latitude of the box.")] double? North,
    [property: Description("Eastern longitude of the box.")] double? East,
    [property: Description("Latitude of a point items must cover.")] double? Lat,
    [property: Description("Longitude of the point.")] double? Lon,
    [property: Description("The 0-based page.")] int? Page,
    [property: Description("Items per page.")] int? PageSize,
    [property: Description("Keep only items whose data covers a time: 'view_time' or an ISO-8601 time.")] string? ValidAt = null);

/// <summary>Finds Library items (MCP <c>query_library_items</c>).</summary>
public sealed class QueryLibraryItemsTool(ILibraryReader library)
{
    /// <summary>The MCP tool name.</summary>
    public const string Name = "query_library_items";

    /// <summary>Items per page when none is given.</summary>
    public const int DefaultPageSize = 50;

    /// <summary>The largest page allowed.</summary>
    public const int MaxPageSize = 500;

    private readonly ILibraryReader _library = library ?? throw new ArgumentNullException(nameof(library));

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
    public static (LibraryItemPageQuery? Query, ToolError? Error) Parse(QueryLibraryItemsRequest request)
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
                if (!LibraryAvailabilityNames.All.Contains(state))
                {
                    return (null, new InvalidArgument(
                        "states", $"unknown state '{raw}'; expected {string.Join(", ", LibraryAvailabilityNames.All.Order())}"));
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

        return (new LibraryItemPageQuery(
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
    public static bool HasFilter(QueryLibraryItemsRequest request) =>
        !string.IsNullOrWhiteSpace(request.SourceId)
        || request.States is { Count: > 0 }
        || !string.IsNullOrWhiteSpace(request.Spec)
        || !string.IsNullOrWhiteSpace(request.Text)
        || request.South is not null || request.West is not null || request.North is not null || request.East is not null
        || request.Lat is not null || request.Lon is not null
        || !string.IsNullOrWhiteSpace(request.ValidAt);
}

/// <summary>Describes one Library item as its details pane does (MCP <c>describe_library_item</c>).</summary>
public sealed class DescribeLibraryItemTool(ILibraryReader library)
{
    /// <summary>The MCP tool name.</summary>
    public const string Name = "describe_library_item";

    private readonly ILibraryReader _library = library ?? throw new ArgumentNullException(nameof(library));

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

/// <summary>SECOM services from the MCP service registry, as list_secom_services returns them.</summary>
[Description("SECOM data services listed in the MCP service registry (#822).")]
public sealed record SecomServicesDto(
    [property: Description("The services, released first; add one with add_library_source url=<endpoint>.")] IReadOnlyList<SecomServiceInfo> Services,
    [property: Description("How many instances the registry listed in all.")] int Listed,
    [property: Description("How many were left out: not S-100 data, deleted, unusable endpoints, duplicates, or another product.")] int Hidden,
    [property: Description("When the listing was read from the registry (UTC).")] DateTimeOffset FetchedAt,
    [property: Description("Why the registry could not be refreshed when a cached copy is returned, or null.")] string? Stale);

/// <summary>One registry service.</summary>
[Description("A SECOM service instance in the MCP service registry.")]
public sealed record SecomServiceInfo(
    [property: Description("The instance's MRN.")] string InstanceId,
    [property: Description("The registered name.")] string Name,
    [property: Description("The registering organisation (last part of its MRN), or null.")] string? Organization,
    [property: Description("The data product, e.g. 'S-124'.")] string Product,
    [property: Description("'Released' or 'Provisional' (test).")] string Status,
    [property: Description("The SECOM endpoint; pass it as url to add_library_source.")] string Endpoint,
    [property: Description("The area it covers, as [south, west, north, east], or null.")] double[]? Bounds,
    [property: Description("With probe: 'Open' (readable without a certificate), 'NeedsCertificate', 'UntrustedServer' (TLS certificate refused) or 'Unreachable'; otherwise null.")] string? Reachability,
    [property: Description("With probe: what the service answered, or null.")] string? Detail,
    [property: Description("With probe: the decision on the server's TLS certificate — 'SystemTrusted', 'AnchorTrusted' (issued under a SECOM trust anchor such as MCP MCC), 'NotTrusted', 'Expired', 'WrongHost' or 'Revoked' (listed on its CA's CRL); null when unknown.")] string? ServerCertificate = null,
    [property: Description("With probe: the trust anchor the server certificate chains to (e.g. 'MCP MCC'), or null.")] string? ServerCertificateAnchor = null,
    [property: Description("With probe, for an 'AnchorTrusted' certificate: 'NotRevoked' (checked against its CAs' CRLs) or 'NotChecked' (no current CRL could be read; the connection is still allowed); otherwise null.")] string? ServerCertificateRevocation = null);

/// <summary>Lists SECOM services from the MCP service registry (MCP <c>list_secom_services</c>, #822).</summary>
public sealed class ListSecomServicesTool(EncDotNet.S100.Collections.Secom.SecomRegistry registry)
{
    /// <summary>The MCP tool name.</summary>
    public const string Name = "list_secom_services";

    /// <summary>The most services probed in one call.</summary>
    public const int MaxProbed = 60;

    private readonly EncDotNet.S100.Collections.Secom.SecomRegistry _registry = registry ?? throw new ArgumentNullException(nameof(registry));

    /// <summary>Lists them, optionally for one product, optionally probing each.</summary>
    public async Task<ToolResult<SecomServicesDto>> InvokeAsync(string? product, bool? probe, CancellationToken ct = default)
    {
        EncDotNet.S100.Collections.Secom.SecomRegistryListing listing;
        try
        {
            listing = await _registry.GetServicesAsync(cancellationToken: ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            return ToolResult<SecomServicesDto>.Err(new LibraryChangeRejected($"the service registry could not be read ({ex.Message})"));
        }

        var wanted = EncDotNet.S100.Collections.Secom.SecomRegistry.NormalizeProduct(product);
        var services = listing.Services
            .Where(s => s.IsS100Product && (wanted is null || string.Equals(s.ProductSpec, wanted, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        var probes = new Dictionary<string, EncDotNet.S100.Collections.Secom.SecomProbeResult>(StringComparer.Ordinal);
        if (probe == true)
        {
            using var gate = new SemaphoreSlim(6);
            var results = await Task.WhenAll(services.Take(MaxProbed).Select(async s =>
            {
                await gate.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    return (s.InstanceId, Result: await _registry.ProbeAsync(s.EndpointUri, ct).ConfigureAwait(false));
                }
                finally
                {
                    gate.Release();
                }
            })).ConfigureAwait(false);
            foreach (var (id, result) in results)
                probes[id] = result;
        }

        return ToolResult<SecomServicesDto>.Ok(new SecomServicesDto(
            [.. services.Select(s => new SecomServiceInfo(
                s.InstanceId,
                s.Name,
                s.OrganizationName,
                s.ProductSpec,
                s.Status.ToString(),
                EncDotNet.S100.Collections.Secom.SecomClient.NormalizeServiceUri(s.EndpointUri).AbsoluteUri,
                s.Bounds is { } b ? [b.South, b.West, b.North, b.East] : null,
                probes.TryGetValue(s.InstanceId, out var r) ? r.Reachability.ToString() : null,
                probes.TryGetValue(s.InstanceId, out var d) ? d.Detail : null,
                probes.TryGetValue(s.InstanceId, out var t) ? t.ServerTrust?.Outcome.ToString() : null,
                probes.TryGetValue(s.InstanceId, out var a) ? a.ServerTrust?.Anchor : null,
                probes.TryGetValue(s.InstanceId, out var v) && v.ServerTrust is { Outcome: EncDotNet.S100.Collections.Secom.SecomServerTrustOutcome.AnchorTrusted } anchored
                    ? anchored.Revocation.ToString()
                    : null))],
            listing.Listed,
            listing.Listed - services.Length,
            listing.FetchedAt,
            listing.Stale));
    }
}

/// <summary>Lists the Online Catalogue directory (MCP <c>list_known_sources</c>).</summary>
public sealed class ListKnownSourcesTool(ILibraryReader library)
{
    /// <summary>The MCP tool name.</summary>
    public const string Name = "list_known_sources";

    private readonly ILibraryReader _library = library ?? throw new ArgumentNullException(nameof(library));

    /// <summary>Lists them.</summary>
    public async Task<ToolResult<KnownSourcesDto>> InvokeAsync(CancellationToken ct = default) =>
        ToolResult<KnownSourcesDto>.Ok(new KnownSourcesDto(await _library.ListKnownSourcesAsync(ct).ConfigureAwait(false)));
}
