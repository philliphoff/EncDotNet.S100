using System.ComponentModel;
using System.Text.Json;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Viewer.Routing;
using EncDotNet.S100.Viewer.Routing.Exchange;
using EncDotNet.S100.Viewer.Services;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EncDotNet.S100.Viewer.McpTools;

// Received routes (#960): display mode, import, copy to edit and export.

// ---------------------------------------------------------------------------
// set_route_display_mode
// ---------------------------------------------------------------------------

/// <summary>Request for <see cref="SetRouteDisplayModeTool"/>.</summary>
[Description("Request for set_route_display_mode: choose how the map draws a route.")]
internal sealed record SetRouteDisplayModeRequest(
    [property: Description("\"native\" (the route's S-421 dataset draws it with its own portrayal; the route renderer draws nothing) or \"planning\" (the route renderer draws it, without editing handles on a received route; the dataset is hidden).")] string Mode,
    [property: Description("Id of the route; omit to use the active route.")] string? RouteId = null);

/// <summary>
/// Sets a route's display mode, as the Native / Planning control under a
/// received route's header does. The route's mode decides its dataset's
/// visibility. While Check route is on for the route it's drawn in Planning
/// mode whatever its mode, and its dataset stays on for the action points.
/// </summary>
internal sealed class SetRouteDisplayModeTool : RouteToolBase
{
    /// <summary>Public tool name as exposed over MCP.</summary>
    public const string Name = "set_route_display_mode";

    /// <summary>Creates a new <see cref="SetRouteDisplayModeTool"/>.</summary>
    public SetRouteDisplayModeTool(RoutesService routes, IRouteEditInvoker invoker)
        : base(routes, invoker) { }

    /// <summary>Executes the tool.</summary>
    public Task<ToolResult<RouteDetail>> InvokeAsync(
        SetRouteDisplayModeRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        RouteDisplayMode mode;
        switch (request.Mode?.Trim().ToLowerInvariant())
        {
            case "native": mode = RouteDisplayMode.Native; break;
            case "planning": mode = RouteDisplayMode.Planning; break;
            default:
                return Task.FromResult(ToolResult<RouteDetail>.Err(new InvalidArgument("mode", "use \"native\" or \"planning\"")));
        }

        return Invoker.InvokeAsync(() =>
        {
            var route = Resolve(request.RouteId, out var error);
            if (route is null)
                return ToolResult<RouteDetail>.Err(error!);
            if (mode == RouteDisplayMode.Native && route.Source?.Format != RouteSourceFormats.S421)
                return ToolResult<RouteDetail>.Err(new InvalidArgument("mode", "native needs a route received from an S-421 dataset"));
            route.SetDisplayMode(mode);
            return Ok(route, Routes);
        });
    }
}

// ---------------------------------------------------------------------------
// import_route
// ---------------------------------------------------------------------------

/// <summary>A file couldn't be opened as a route.</summary>
/// <param name="Reason">Why: not_a_route, unsupported_version, too_few_waypoints or older_edition.</param>
/// <param name="Detail">The one line the viewer's error dialog shows.</param>
[Description("Raised when import_route can't open the file as a route; 'reason' says why and 'detail' is the line the viewer's dialog shows.")]
internal sealed record RouteImportFailed(
    [property: Description("Why: \"not_a_route\" (neither RTZ nor S-421, or no usable route in it), \"unsupported_version\", \"too_few_waypoints\" or \"older_edition\" (a newer edition of the route is open).")] string Reason,
    [property: Description("The one-line message the viewer shows, e.g. \"This file isn't an RTZ or S-421 route.\"")] string Detail)
    : ToolError("route_import_failed", Detail);

/// <summary>Request for <see cref="ImportRouteTool"/>.</summary>
[Description("Request for import_route: open a route file as a received, read-only route.")]
internal sealed record ImportRouteRequest(
    [property: Description("Path of the route file to open (S-421 GML; RTZ in a later release). Give this or content.")] string? Path = null,
    [property: Description("The file's content, when there is no file to point at; it's saved to a temporary file and opened from there. Give this or path.")] string? Content = null);

/// <summary>Result of <see cref="ImportRouteTool"/>.</summary>
[Description("Result of import_route: the received route and whether it was already open.")]
internal sealed record ImportRouteResult(
    [property: Description("True when the route was already open (the same file, or the same route ID and edition) and was selected instead.")] bool AlreadyOpen,
    [property: Description("The received route, now active.")] RouteDetail Route);

/// <summary>
/// Opens a route file as a received route, as the Routes header's Import
/// route… does: the format is sniffed from the content.
/// </summary>
internal sealed class ImportRouteTool : RouteToolBase
{
    /// <summary>Public tool name as exposed over MCP.</summary>
    public const string Name = "import_route";

    private readonly RouteExchangeService _exchange;
    private readonly string _scratch;

    /// <summary>Creates the tool.</summary>
    /// <param name="routes">The routes.</param>
    /// <param name="invoker">UI-thread marshalling.</param>
    /// <param name="exchange">The import, copy and export flows.</param>
    /// <param name="scratchDirectory">Where content is saved before opening; the temp directory when omitted.</param>
    public ImportRouteTool(RoutesService routes, IRouteEditInvoker invoker, RouteExchangeService exchange, string? scratchDirectory = null)
        : base(routes, invoker)
    {
        ArgumentNullException.ThrowIfNull(exchange);
        _exchange = exchange;
        _scratch = scratchDirectory ?? System.IO.Path.Combine(System.IO.Path.GetTempPath(), "soundcharts-route-imports");
    }

    /// <summary>Executes the tool.</summary>
    public async Task<ToolResult<ImportRouteResult>> InvokeAsync(ImportRouteRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(request.Path) == string.IsNullOrEmpty(request.Content))
            return ToolResult<ImportRouteResult>.Err(new InvalidArgument("path", "give either path or content"));

        var path = request.Path;
        if (path is null)
        {
            var extension = _exchange.Formats.Sniff(request.Content!) is { } sniffed ? sniffed.Format.FileExtension : ".txt";
            Directory.CreateDirectory(_scratch);
            path = System.IO.Path.Combine(_scratch, Guid.NewGuid().ToString("N") + extension);
            await File.WriteAllTextAsync(path, request.Content, cancellationToken).ConfigureAwait(false);
        }
        else if (!File.Exists(path))
        {
            return ToolResult<ImportRouteResult>.Err(new InvalidArgument("path", $"no file at '{path}'"));
        }

        var result = await (await Invoker.InvokeAsync(() => _exchange.ImportAsync(path)).ConfigureAwait(false)).ConfigureAwait(false);
        return await Invoker.InvokeAsync(() => result.Route is { } route
            ? ToolResult<ImportRouteResult>.Ok(new ImportRouteResult(result.AlreadyOpen, RouteProjection.Detail(route, Routes)))
            : ToolResult<ImportRouteResult>.Err(new RouteImportFailed(result.ErrorCode!, result.Error!))).ConfigureAwait(false);
    }
}

// ---------------------------------------------------------------------------
// copy_route
// ---------------------------------------------------------------------------

/// <summary>A route's identity in an exchange file (decision 17).</summary>
[Description("How a route is identified in an exchange file: a received route exported unchanged keeps its route ID, edition and status; a copy is a new route with a previous-route reference, edition 1 and status Planned.")]
internal sealed record RouteIdentityDto(
    [property: Description("The route ID written, or null in a copy_route dry run (a new ID is assigned when the copy is made).")] string? RouteId,
    [property: Description("The edition written.")] int Edition,
    [property: Description("The status code written (S-421 routeInfoStatus: 1 Initial, 2 Planned, 3 Recommended, …), or null.")] int? Status,
    [property: Description("The route ID this route was copied from, or null.")] string? PreviousRouteId,
    [property: Description("The edition it was copied from, or null.")] int? PreviousEdition,
    [property: Description("True when the identity is the received file's own; false for a new route.")] bool Kept)
{
    public static RouteIdentityDto From(RouteExchangeIdentity identity)
        => new(identity.RouteId.Length == 0 ? null : identity.RouteId, identity.Edition, identity.Status, identity.PreviousRouteId, identity.PreviousEdition, identity.Kept);
}

/// <summary>Request for <see cref="CopyRouteTool"/>.</summary>
[Description("Request for copy_route: make an editable copy of a received route.")]
internal sealed record CopyRouteRequest(
    [property: Description("Id of the received route to copy; omit to use the active route.")] string? RouteId = null,
    [property: Description("True to return only what the copy would keep and couldn't hold (the Copy to edit dialog) without making it.")] bool DryRun = false);

/// <summary>Result of <see cref="CopyRouteTool"/>.</summary>
[Description("Result of copy_route: what the copy keeps and can't hold, its identity, and the copy when one was made.")]
internal sealed record CopyRouteResult(
    [property: Description("Id of the received route copied.")] string SourceRouteId,
    [property: Description("The copy's name: \"{name} (copy)\".")] string Name,
    [property: Description("What the copy keeps, \"what → where\".")] IReadOnlyList<string> Keeps,
    [property: Description("What the viewer's route can't hold; preserved content is kept and written back on export.")] IReadOnlyList<string> NotKept,
    [property: Description("The copy's identity: a new route ID referencing the source's route ID and edition, edition 1, status Planned.")] RouteIdentityDto Identity,
    [property: Description("True when nothing was changed (dryRun).")] bool DryRun,
    [property: Description("The copy, now active and in Edit mode; null on a dry run. One undo_route_edit removes it.")] RouteDetail? Copy);

/// <summary>
/// Copy to edit: makes an owned, editable copy of a received route, or with
/// <c>dryRun</c> reports what the dialog shows.
/// </summary>
internal sealed class CopyRouteTool : RouteToolBase
{
    /// <summary>Public tool name as exposed over MCP.</summary>
    public const string Name = "copy_route";

    private readonly RouteExchangeService _exchange;

    /// <summary>Creates the tool.</summary>
    public CopyRouteTool(RoutesService routes, IRouteEditInvoker invoker, RouteExchangeService exchange)
        : base(routes, invoker)
    {
        ArgumentNullException.ThrowIfNull(exchange);
        _exchange = exchange;
    }

    /// <summary>Executes the tool.</summary>
    public Task<ToolResult<CopyRouteResult>> InvokeAsync(CopyRouteRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        return Invoker.InvokeAsync(() =>
        {
            var route = Resolve(request.RouteId, out var error);
            if (route is null)
                return ToolResult<CopyRouteResult>.Err(error!);
            if (!route.IsReadOnly)
                return ToolResult<CopyRouteResult>.Err(new InvalidArgument("routeId", "the route is already editable; copy_route copies a received route"));

            var preview = _exchange.PreviewCopy(route);
            RouteDetail? copy = null;
            var identity = preview.Identity;
            if (!request.DryRun)
            {
                var made = _exchange.Copy(route);
                identity = RouteExchangeIdentity.For(made);
                copy = RouteProjection.Detail(made, Routes);
            }
            return ToolResult<CopyRouteResult>.Ok(new CopyRouteResult(
                route.Id, preview.Name, preview.Description.Keeps, preview.Description.NotKept,
                RouteIdentityDto.From(identity), request.DryRun, copy));
        });
    }
}

// ---------------------------------------------------------------------------
// export_route
// ---------------------------------------------------------------------------

/// <summary>Request for <see cref="ExportRouteTool"/>.</summary>
[Description("Request for export_route: write a route in an exchange format.")]
internal sealed record ExportRouteRequest(
    [property: Description("The format: \"S-421\" (RTZ in a later release).")] string Format,
    [property: Description("Id of the route to export; omit to use the active route.")] string? RouteId = null,
    [property: Description("The format version; the newest when omitted (S-421: 1.0).")] string? Version = null,
    [property: Description("True to also write your check settings (draft, required UKC, departure). Off by default: they're yours, not the route's.")] bool IncludeCheckSettings = false,
    [property: Description("File to write. Omit to get the file's content back instead.")] string? Path = null);

/// <summary>Result of <see cref="ExportRouteTool"/>.</summary>
[Description("Result of export_route: how the route is identified in the file, what the format can't hold, and where it went.")]
internal sealed record ExportRouteResult(
    [property: Description("The format written.")] string Format,
    [property: Description("The version written.")] string Version,
    [property: Description("How the route is identified in the file (decision 17).")] RouteIdentityDto Identity,
    [property: Description("Whether your check settings were written.")] bool IncludeCheckSettings,
    [property: Description("Fields of this route the format can't hold, as the Export dialog lists them; empty when nothing is lost.")] IReadOnlyList<string> CannotHold,
    [property: Description("The file written, or null when content is returned.")] string? Path,
    [property: Description("The file's content when no path was given, else null.")] string? Content,
    [property: Description("The file name the Export dialog suggests, e.g. \"Nab to Southampton.gml\".")] string SuggestedFileName);

/// <summary>Export…: writes a route (owned or received) in an exchange format.</summary>
internal sealed class ExportRouteTool : RouteToolBase
{
    /// <summary>Public tool name as exposed over MCP.</summary>
    public const string Name = "export_route";

    private readonly RouteExchangeService _exchange;

    /// <summary>Creates the tool.</summary>
    public ExportRouteTool(RoutesService routes, IRouteEditInvoker invoker, RouteExchangeService exchange)
        : base(routes, invoker)
    {
        ArgumentNullException.ThrowIfNull(exchange);
        _exchange = exchange;
    }

    /// <summary>Executes the tool.</summary>
    public Task<ToolResult<ExportRouteResult>> InvokeAsync(ExportRouteRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        return Invoker.InvokeAsync(() =>
        {
            var route = Resolve(request.RouteId, out var error);
            if (route is null)
                return ToolResult<ExportRouteResult>.Err(error!);
            if (_exchange.Formats.Find(request.Format) is not { } format)
                return ToolResult<ExportRouteResult>.Err(new InvalidArgument("format", $"use one of: {string.Join(", ", _exchange.Formats.Formats.Select(f => f.Name))}"));
            if (request.Version is { } version && !format.Versions.Contains(version))
                return ToolResult<ExportRouteResult>.Err(new InvalidArgument("version", $"{format.Name} versions: {string.Join(", ", format.Versions)}"));
            if (route.Waypoints.Count < 2)
                return ToolResult<ExportRouteResult>.Err(new InvalidArgument("routeId", "the route needs at least two waypoints"));

            var options = new RouteExportOptions(request.Version, request.IncludeCheckSettings);
            RouteExportPreview preview;
            string? content = null;
            try
            {
                if (request.Path is { } path)
                {
                    preview = _exchange.ExportToFile(route, format.Name, options, path);
                }
                else
                {
                    using var stream = new MemoryStream();
                    preview = _exchange.Export(route, format.Name, options, stream);
                    content = System.Text.Encoding.UTF8.GetString(stream.ToArray());
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return ToolResult<ExportRouteResult>.Err(new InvalidArgument("path", ex.Message));
            }

            return ToolResult<ExportRouteResult>.Ok(new ExportRouteResult(
                preview.Format.Name, preview.Version, RouteIdentityDto.From(preview.Identity), request.IncludeCheckSettings,
                preview.CannotHold, request.Path, content, preview.SuggestedFileName));
        });
    }
}

/// <summary>Wraps the received-route tools as <see cref="McpServerTool"/>s.</summary>
internal static class ReceivedRouteMcpAdapters
{
    private static readonly JsonSerializerOptions JsonOptions = McpAdapterShared.Options;

    private const string SetDisplayModeDescription =
        "Sets how the live viewer's map draws a route: \"native\" draws a received S-421 route with its " +
        "dataset's own portrayal (waypoints, legs, action points) and the route renderer draws nothing; " +
        "\"planning\" hides the dataset and the route renderer draws the route (without editing handles on " +
        "a received route). The dataset's visibility follows the mode. While Check route is on for the " +
        "route it is drawn in Planning mode and its dataset stays on for the action points. Only received " +
        "S-421 routes have a Native mode. Returns the route (displayMode, readOnly, source). " +
        "Viewer-injected tool — mutates live state.";

    /// <summary>Creates the <c>set_route_display_mode</c> tool.</summary>
    public static McpServerTool Create(SetRouteDisplayModeTool inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        var del = (
            [Description("\"native\" or \"planning\".")] string mode,
            [Description("Id of the route; omit to use the active route.")] string? routeId = null,
            CancellationToken ct = default) =>
            McpAdapterShared.DispatchAsync(() => inner.InvokeAsync(new SetRouteDisplayModeRequest(mode, routeId), ct));

        return McpServerTool.Create(del, new McpServerToolCreateOptions
        {
            Name = SetRouteDisplayModeTool.Name,
            Description = SetDisplayModeDescription,
            SerializerOptions = JsonOptions,
        });
    }

    /// <summary>Creates the <c>import_route</c> tool.</summary>
    public static McpServerTool Create(ImportRouteTool inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        var del = (
            [Description("Path of the route file (S-421 GML). Give this or content.")] string? path = null,
            [Description("The file's content, when there is no file. Give this or path.")] string? content = null,
            CancellationToken ct = default) =>
            McpAdapterShared.DispatchAsync(() => inner.InvokeAsync(new ImportRouteRequest(path, content), ct));
        return McpServerTool.Create(del, new McpServerToolCreateOptions
        {
            Name = ImportRouteTool.Name,
            Description =
                "Opens a route file in the live viewer as a received, read-only route, as the Routes header's Import route… does. " +
                "The format is sniffed from the content: an S-421 file also opens as a dataset and its route starts in Native mode. " +
                "A file already open, or the same route ID and edition, selects the existing route (alreadyOpen: true); a newer edition " +
                "replaces it. The route becomes active. Fails with route_import_failed (reason not_a_route, unsupported_version, " +
                "too_few_waypoints or older_edition). Edit a received route by copying it with copy_route. Viewer-injected tool — mutates live state.",
            SerializerOptions = JsonOptions,
        });
    }

    /// <summary>Creates the <c>copy_route</c> tool.</summary>
    public static McpServerTool Create(CopyRouteTool inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        var del = (
            [Description("Id of the received route to copy; omit to use the active route.")] string? routeId = null,
            [Description("True to report what the copy keeps and can't hold without making it.")] bool dryRun = false,
            CancellationToken ct = default) =>
            McpAdapterShared.DispatchAsync(() => inner.InvokeAsync(new CopyRouteRequest(routeId, dryRun), ct));
        return McpServerTool.Create(del, new McpServerToolCreateOptions
        {
            Name = CopyRouteTool.Name,
            Description =
                "Copy to edit: makes an owned, editable copy of a received route, as the Routes panel's Copy to edit does. The copy is " +
                "named \"{name} (copy)\", gets a new route ID with a reference to the source's route ID and edition, edition 1 and status " +
                "Planned, keeps the content the viewer's route can't hold for re-export, becomes active and opens in Edit mode. One " +
                "undo_route_edit removes it. dryRun: true returns only the dialog's lists (keeps, notKept) and identity. " +
                "Viewer-injected tool — mutates live state.",
            SerializerOptions = JsonOptions,
        });
    }

    /// <summary>Creates the <c>export_route</c> tool.</summary>
    public static McpServerTool Create(ExportRouteTool inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        var del = (
            [Description("The format: \"S-421\".")] string format,
            [Description("Id of the route; omit to use the active route.")] string? routeId = null,
            [Description("The format version; the newest when omitted.")] string? version = null,
            [Description("True to also write your check settings (draft, required UKC, departure); off by default.")] bool includeCheckSettings = false,
            [Description("File to write; omit to get the content back.")] string? path = null,
            CancellationToken ct = default) =>
            McpAdapterShared.DispatchAsync(() => inner.InvokeAsync(new ExportRouteRequest(format, routeId, version, includeCheckSettings, path), ct));
        return McpServerTool.Create(del, new McpServerToolCreateOptions
        {
            Name = ExportRouteTool.Name,
            Description =
                "Export…: writes a route (owned or received) in an exchange format, as the route ⋯ menu's Export… does. A received " +
                "route exported unchanged keeps its route ID, edition and status; a copy is written as a new route that references its " +
                "source. Your check settings are written only with includeCheckSettings: true. Check results are never exported. " +
                "Returns the identity, the fields the format can't hold (cannotHold), and the path, or the content when no path is given.",
            SerializerOptions = JsonOptions,
        });
    }

    /// <summary>Test seam mirroring the production translation.</summary>
    internal static CallToolResult TranslateResult(ToolResult<RouteDetail> result)
        => McpAdapterShared.Translate(result);
}
