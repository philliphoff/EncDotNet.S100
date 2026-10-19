using System.Globalization;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Routing;
using EncDotNet.S100.Viewer.Routing.Exchange;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Import route…, Copy to edit and Export… (#960; handoff Part F): the flows
/// the Routes panel and the MCP tools share. Formats come from a
/// <see cref="RouteFormatRegistry"/> (RTZ and S-421), so a new format needs
/// no change here.
/// </summary>
/// <remarks>Runs on the UI thread.</remarks>
internal sealed class RouteExchangeService
{
    private readonly RoutesService _routes;
    private readonly ReceivedRoutesService _received;
    private readonly Func<string, Task<DatasetEntry?>>? _openDataset;

    /// <summary>Creates the service.</summary>
    /// <param name="routes">The route collection.</param>
    /// <param name="received">The received routes, which datasets and imported files feed.</param>
    /// <param name="formats">The route formats; the built-in ones when omitted.</param>
    /// <param name="openDataset">Opens a file as a dataset the user chose (Import route… for a format that opens as one).</param>
    public RouteExchangeService(
        RoutesService routes,
        ReceivedRoutesService received,
        RouteFormatRegistry? formats = null,
        Func<string, Task<DatasetEntry?>>? openDataset = null)
    {
        ArgumentNullException.ThrowIfNull(routes);
        ArgumentNullException.ThrowIfNull(received);
        _routes = routes;
        _received = received;
        _openDataset = openDataset;
        Formats = formats ?? RouteFormatRegistry.Default;
        LastExportFormat = Formats.Formats.FirstOrDefault()?.Name;
    }

    /// <summary>The route formats.</summary>
    public RouteFormatRegistry Formats { get; }

    /// <summary>The format last exported to, which the Export dialog starts on.</summary>
    public string? LastExportFormat { get; private set; }

    /// <summary>The version last exported, which the Export dialog starts on.</summary>
    public string? LastExportVersion { get; private set; }

    /// <summary>
    /// Opens <paramref name="path"/> as a received route (F2): the format is
    /// sniffed from the content. A format with its own portrayal (S-421)
    /// loads as a dataset, and the route follows it in Native mode; any other
    /// (RTZ) is read directly and opens in Planning mode. A file already open
    /// selects its route. The route becomes the active route.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <returns>The route, or the one-line reason it couldn't be opened.</returns>
    public async Task<RouteImportResult> ImportAsync(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        if (_received.FindByFile(path) is { } open)
        {
            _routes.Routes.SetActiveRoute(open);
            return RouteImportResult.Opened(open, alreadyOpen: true);
        }

        string head;
        try
        {
            head = RouteFormatRegistry.ReadHead(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return RouteImportResult.Failed("not_a_route", Strings.Import_NotARoute);
        }

        if (Formats.Sniff(head) is not { } sniffed)
            return RouteImportResult.Failed("not_a_route", Strings.Import_NotARoute);
        var (format, match) = sniffed;
        if (!match.Supported)
        {
            return RouteImportResult.Failed(
                "unsupported_version",
                string.Format(CultureInfo.CurrentCulture, Strings.Import_UnsupportedVersionFormat, format.Name, match.Version));
        }

        if (format.OpensAsDataset && _openDataset is not null)
        {
            var entry = await _openDataset(path).ConfigureAwait(true);
            if (_received.FindByFile(path) is { } route)
                return RouteImportResult.Opened(route, alreadyOpen: false);
            if (entry?.RouteWarning is { } warning)
                return RouteImportResult.Failed("not_a_route", warning);
            // The same route ID and edition was already open from another file.
            if (_routes.Routes.ActiveRoute is { IsReadOnly: true } active && entry is { IsLoaded: true })
                return RouteImportResult.Opened(active, alreadyOpen: true);
            return RouteImportResult.Failed("not_a_route", Strings.Import_NotARoute);
        }

        Route read;
        try
        {
            read = format.ReadReceived(path);
        }
        catch (RouteImportException ex)
        {
            return RouteImportResult.Failed(ex.Code, ex.Message);
        }

        var (admitted, refused) = _received.Receive(read);
        return admitted is not null
            ? RouteImportResult.Opened(admitted, alreadyOpen: !ReferenceEquals(admitted, read))
            : RouteImportResult.Failed("older_edition", refused!);
    }

    /// <summary>
    /// Reopens the route files without a dataset (RTZ) that were open when the
    /// viewer last closed and still exist (F8), keeping the active route.
    /// </summary>
    /// <returns>The routes reopened.</returns>
    public async Task<IReadOnlyList<Route>> ReopenFilesAsync()
    {
        var active = _routes.Routes.ActiveRoute;
        var reopened = new List<Route>();
        foreach (var path in _received.TakeFilesToReopen())
        {
            if (File.Exists(path) && (await ImportAsync(path).ConfigureAwait(true)).Route is { } route)
                reopened.Add(route);
        }
        _routes.Routes.SetActiveRoute(active);
        _received.SaveOpenFiles();
        return reopened;
    }

    /// <summary>
    /// What Copy to edit would make from <paramref name="received"/> (F5): the
    /// copy's name, what it keeps and can't hold, and its identity. Nothing
    /// changes.
    /// </summary>
    /// <param name="received">A received route.</param>
    /// <returns>The preview.</returns>
    /// <exception cref="ArgumentException"><paramref name="received"/> isn't a received route.</exception>
    public RouteCopyPreview PreviewCopy(Route received)
    {
        ArgumentNullException.ThrowIfNull(received);
        if (received.Source is not { } source)
            throw new ArgumentException("Only a received route is copied to edit.", nameof(received));
        var description = Formats.Find(source.Format)?.DescribeCopy(received)
            ?? new RouteCopyDescription([], []);
        return new RouteCopyPreview(
            received,
            CopyName(received),
            description,
            new RouteExchangeIdentity(string.Empty, 1, RouteExchangeIdentity.StatusPlanned, source.RouteId, source.Edition, Kept: false));
    }

    /// <summary>
    /// Makes an owned, editable copy of <paramref name="received"/> (F5,
    /// decision 17): named "{name} (copy)", with a new route ID, a reference
    /// to the source's route ID and edition, and the content the model can't
    /// hold kept for re-export. The copy becomes the active route and opens in
    /// Edit mode; one undo removes it.
    /// </summary>
    /// <param name="received">A received route.</param>
    /// <returns>The copy.</returns>
    /// <exception cref="ArgumentException"><paramref name="received"/> isn't a received route.</exception>
    public Route Copy(Route received)
    {
        ArgumentNullException.ThrowIfNull(received);
        if (received.Source is not { } source)
            throw new ArgumentException("Only a received route is copied to edit.", nameof(received));

        var copy = new Route(null, CopyName(received));
        var snapshot = received.CaptureSnapshot();
        snapshot.Info.Name = copy.Name;
        copy.RestoreSnapshot(snapshot);
        copy.Origin = source;
        copy.Preserved = received.Preserved;

        _routes.Routes.Add(copy);
        copy.History.RecordCreation(RouteEditLabels.CopyRoute, () =>
        {
            _routes.IsEditModeActive = false;
            _routes.Routes.Remove(copy);
            if (_routes.Routes.Routes.Contains(received))
                _routes.Routes.SetActiveRoute(received);
        });
        _routes.IsEditModeActive = true;
        return copy;
    }

    /// <summary>
    /// The newer edition of a copy's source that is open now (F4: "Source has
    /// a newer edition (ed. 4)"), or <c>null</c>.
    /// </summary>
    /// <param name="copy">An owned route.</param>
    /// <returns>The newer edition number.</returns>
    public int? NewerSourceEdition(Route copy)
    {
        ArgumentNullException.ThrowIfNull(copy);
        if (copy.Origin is not { RouteId: { Length: > 0 } routeId } origin)
            return null;
        return _routes.Routes.Routes
            .Where(r => r.Source is { } s && s.RouteId == routeId && (s.Edition ?? 0) > (origin.Edition ?? 0))
            .Select(r => r.Source!.Edition)
            .Max();
    }

    /// <summary>What exporting <paramref name="route"/> would write (F7), without writing.</summary>
    /// <param name="route">The route.</param>
    /// <param name="formatName">The format, e.g. <c>S-421</c>.</param>
    /// <param name="options">The options.</param>
    /// <returns>The preview, or <c>null</c> when the format is unknown or the version unsupported.</returns>
    public RouteExportPreview? PreviewExport(Route route, string formatName, RouteExportOptions options)
    {
        ArgumentNullException.ThrowIfNull(route);
        ArgumentNullException.ThrowIfNull(options);
        if (Formats.Find(formatName) is not { } format)
            return null;
        var version = options.Version ?? format.Versions[0];
        if (!format.Versions.Contains(version))
            return null;
        return new RouteExportPreview(
            format,
            version,
            RouteExchangeIdentity.For(route),
            format.CannotHold(route, options),
            SuggestedFileName(route, format));
    }

    /// <summary>Writes <paramref name="route"/> to <paramref name="stream"/> in <paramref name="formatName"/>.</summary>
    /// <param name="route">The route; it needs two waypoints.</param>
    /// <param name="formatName">The format.</param>
    /// <param name="options">The options.</param>
    /// <param name="stream">Where to write.</param>
    /// <returns>What was written.</returns>
    /// <exception cref="ArgumentException">The format is unknown, the version unsupported, or the route too short.</exception>
    public RouteExportPreview Export(Route route, string formatName, RouteExportOptions options, Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (route.Waypoints.Count < 2)
            throw new ArgumentException(Strings.Import_TooFewWaypoints, nameof(route));
        var preview = PreviewExport(route, formatName, options)
            ?? throw new ArgumentException($"Unknown format or version: {formatName} {options.Version}.", nameof(formatName));
        preview.Format.Write(route, preview.Identity, options with { Version = preview.Version }, stream);
        LastExportFormat = preview.Format.Name;
        LastExportVersion = preview.Version;
        return preview;
    }

    /// <summary>Exports to <paramref name="path"/>, staged through a temporary file.</summary>
    /// <param name="route">The route.</param>
    /// <param name="formatName">The format.</param>
    /// <param name="options">The options.</param>
    /// <param name="path">The file to write.</param>
    /// <returns>What was written.</returns>
    public RouteExportPreview ExportToFile(Route route, string formatName, RouteExportOptions options, string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var temp = path + ".tmp";
        RouteExportPreview preview;
        using (var stream = File.Create(temp))
            preview = Export(route, formatName, options, stream);
        File.Move(temp, path, overwrite: true);
        return preview;
    }

    private static string CopyName(Route route)
        => string.Format(CultureInfo.CurrentCulture, Strings.Copy_NameFormat, route.Name ?? route.Source?.RouteId ?? string.Empty);

    private static string SuggestedFileName(Route route, IRouteFormat format)
    {
        var name = string.Join("_", (route.Name ?? route.Id).Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Trim();
        return (name.Length == 0 ? route.Id : name) + format.FileExtension;
    }
}

/// <summary>The outcome of Import route….</summary>
/// <param name="Route">The route opened, or <c>null</c>.</param>
/// <param name="AlreadyOpen">Whether the route was already open (the same file, or the same route ID and edition).</param>
/// <param name="ErrorCode">Why it failed: <c>not_a_route</c>, <c>unsupported_version</c>, <c>too_few_waypoints</c> or <c>older_edition</c>.</param>
/// <param name="Error">The one line the error dialog shows.</param>
internal sealed record RouteImportResult(Route? Route, bool AlreadyOpen, string? ErrorCode, string? Error)
{
    public static RouteImportResult Opened(Route route, bool alreadyOpen) => new(route, alreadyOpen, null, null);

    public static RouteImportResult Failed(string code, string error) => new(null, false, code, error);
}

/// <summary>What Copy to edit would make.</summary>
/// <param name="Source">The received route.</param>
/// <param name="Name">The copy's name.</param>
/// <param name="Description">What it keeps and can't hold.</param>
/// <param name="Identity">The copy's identity; its route ID is assigned when it's made.</param>
internal sealed record RouteCopyPreview(Route Source, string Name, RouteCopyDescription Description, RouteExchangeIdentity Identity);

/// <summary>What an export writes.</summary>
/// <param name="Format">The format.</param>
/// <param name="Version">The version.</param>
/// <param name="Identity">How the route is identified in the file.</param>
/// <param name="CannotHold">What the format can't hold.</param>
/// <param name="SuggestedFileName">The save dialog's suggested name, e.g. "Nab to Southampton.gml".</param>
internal sealed record RouteExportPreview(
    IRouteFormat Format,
    string Version,
    RouteExchangeIdentity Identity,
    IReadOnlyList<string> CannotHold,
    string SuggestedFileName);
