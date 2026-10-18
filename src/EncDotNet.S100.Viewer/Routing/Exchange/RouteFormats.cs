namespace EncDotNet.S100.Viewer.Routing.Exchange;

/// <summary>
/// One route exchange format (RTZ or S-421): how to recognise a
/// file, read it as a received route, describe what a copy keeps, and write a
/// route (#960). Import route…, Copy to edit and Export… work only through
/// this interface, so a new format plugs in by registering an implementation
/// with <see cref="RouteFormatRegistry"/>.
/// </summary>
public interface IRouteFormat
{
    /// <summary>The format's name as <see cref="RouteSource.Format"/> carries it, e.g. <c>"S-421"</c>.</summary>
    string Name { get; }

    /// <summary>A one-line description for the Export dialog, e.g. "IHO route plan, edition 1.0".</summary>
    string Description { get; }

    /// <summary>Who the format is for, e.g. "For S-100 systems.".</summary>
    string Audience { get; }

    /// <summary>The versions this build writes, newest first.</summary>
    IReadOnlyList<string> Versions { get; }

    /// <summary>The file extension a written file gets, with its dot, e.g. <c>".gml"</c>.</summary>
    string FileExtension { get; }

    /// <summary>File-name patterns the Import picker offers, e.g. <c>*.gml</c>.</summary>
    IReadOnlyList<string> FilePatterns { get; }

    /// <summary>
    /// Whether a file of this format also opens as a dataset with its own
    /// portrayal (S-421), so Import route… loads the dataset and the route
    /// follows it; otherwise the received route is read directly and opens in
    /// Planning mode.
    /// </summary>
    bool OpensAsDataset { get; }

    /// <summary>
    /// Whether <paramref name="head"/> (the start of a file, as text) is this
    /// format, and which version.
    /// </summary>
    /// <param name="head">The first few kilobytes of the file.</param>
    /// <returns>The match, or <c>null</c> when it isn't this format.</returns>
    RouteFormatMatch? Sniff(string head);

    /// <summary>Reads <paramref name="path"/> as a received, read-only route.</summary>
    /// <param name="path">The file.</param>
    /// <returns>The received route, with its <see cref="Route.Source"/> and <see cref="Route.Preserved"/> set.</returns>
    /// <exception cref="RouteImportException">The file isn't a usable route.</exception>
    Route ReadReceived(string path);

    /// <summary>
    /// What a copy of <paramref name="received"/> keeps in the viewer's model
    /// and what it can't hold (kept for re-export), for the Copy to edit
    /// dialog. Plain sentences, "what → where".
    /// </summary>
    /// <param name="received">A received route of this format.</param>
    /// <returns>The two lists.</returns>
    RouteCopyDescription DescribeCopy(Route received);

    /// <summary>
    /// The fields of <paramref name="route"/> this format can't hold, as
    /// plain sentences: only fields the route actually has. Empty when
    /// nothing is lost.
    /// </summary>
    /// <param name="route">The route to export.</param>
    /// <param name="options">The export options.</param>
    /// <returns>What the file will leave out.</returns>
    IReadOnlyList<string> CannotHold(Route route, RouteExportOptions options);

    /// <summary>Writes <paramref name="route"/> to <paramref name="stream"/>.</summary>
    /// <param name="route">The route.</param>
    /// <param name="identity">How the route is identified in the file (decision 17).</param>
    /// <param name="options">The export options.</param>
    /// <param name="stream">Where to write; left open.</param>
    void Write(Route route, RouteExchangeIdentity identity, RouteExportOptions options, Stream stream);

    /// <summary>Serialises preserved content of this format (for <c>routes.json</c>).</summary>
    /// <param name="content">The preserved content.</param>
    /// <returns>Its text.</returns>
    string SerializePreserved(RoutePreservedContent content);

    /// <summary>Reads preserved content back from <see cref="SerializePreserved"/>'s text.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The content, or <c>null</c> when it can't be read.</returns>
    RoutePreservedContent? DeserializePreserved(string text);
}

/// <summary>A sniffed file format.</summary>
/// <param name="Version">The version the file declares, or <c>null</c>.</param>
/// <param name="Supported">Whether this build reads that version.</param>
public sealed record RouteFormatMatch(string? Version, bool Supported);

/// <summary>Options for exporting a route.</summary>
/// <param name="Version">The format version to write; the newest when <c>null</c>.</param>
/// <param name="IncludeCheckSettings">
/// Whether to write the mariner's check settings (draft, required UKC,
/// departure). Off by default (decision 16).
/// </param>
public sealed record RouteExportOptions(string? Version = null, bool IncludeCheckSettings = false);

/// <summary>What a copy keeps and what it can't hold.</summary>
/// <param name="Keeps">What the copy keeps, "what → where".</param>
/// <param name="NotKept">What the viewer's model can't hold, with reasons; preserved content says it's written back on export.</param>
public sealed record RouteCopyDescription(IReadOnlyList<string> Keeps, IReadOnlyList<string> NotKept);

/// <summary>
/// Content of a received route the viewer's model can't hold (action points,
/// further schedules, extensions…), kept on a copy so export can write it back.
/// </summary>
/// <param name="Format">The format it came from.</param>
/// <param name="Model">The format's own model of the received route (for S-421 an <c>S421Route</c>).</param>
/// <param name="WaypointCount">The source's waypoint count; preserved schedules stop matching when the copy's differs.</param>
/// <param name="ScheduleCount">How many preserved schedules can go stale.</param>
public sealed record RoutePreservedContent(string Format, object Model, int WaypointCount, int ScheduleCount);

/// <summary>A file couldn't be read as a route; <see cref="Exception.Message"/> is the one line Import shows.</summary>
public sealed class RouteImportException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="message">The one-line, user-facing reason.</param>
    /// <param name="code">A stable code: <c>not_a_route</c>, <c>unsupported_version</c> or <c>too_few_waypoints</c>.</param>
    /// <param name="inner">The underlying failure, if any.</param>
    public RouteImportException(string message, string code, Exception? inner = null)
        : base(message, inner)
    {
        Code = code;
    }

    /// <summary>The stable code.</summary>
    public string Code { get; }
}

/// <summary>
/// How a route is identified in an exported file (decision 17): a received
/// route exported unchanged keeps its route ID, edition and status; a copy is
/// a new route with a previous-route reference, edition 1 and status Planned.
/// </summary>
/// <param name="RouteId">The route ID written.</param>
/// <param name="Edition">The edition written.</param>
/// <param name="Status">The status code written (S-421 <c>routeInfoStatus</c>), or <c>null</c>.</param>
/// <param name="PreviousRouteId">The route ID the route was copied from, or <c>null</c>.</param>
/// <param name="PreviousEdition">The edition it was copied from, or <c>null</c>.</param>
/// <param name="Kept">Whether the identity is the received file's own (not a new route).</param>
public sealed record RouteExchangeIdentity(
    string RouteId,
    int Edition,
    int? Status,
    string? PreviousRouteId,
    int? PreviousEdition,
    bool Kept)
{
    /// <summary>S-421 <c>routeInfoStatus</c> Initial.</summary>
    public const int StatusInitial = 1;

    /// <summary>S-421 <c>routeInfoStatus</c> Planned.</summary>
    public const int StatusPlanned = 2;

    /// <summary>The identity <paramref name="route"/> is exported with.</summary>
    /// <param name="route">The route.</param>
    /// <returns>Its identity.</returns>
    public static RouteExchangeIdentity For(Route route)
    {
        ArgumentNullException.ThrowIfNull(route);
        if (route.Source is { } source)
            return new(source.RouteId ?? route.Id, source.Edition ?? 1, source.Status, null, null, Kept: true);
        if (route.Origin is { } origin)
            return new(route.Id, 1, StatusPlanned, origin.RouteId, origin.Edition, Kept: false);
        return new(route.Id, 1, StatusInitial, null, null, Kept: false);
    }

    /// <summary>The previous-route reference as written, e.g. <c>GB-NAB-SOU-1:2</c>, or <c>null</c>.</summary>
    public string? PreviousRouteReference => PreviousRouteId is null
        ? null
        : PreviousEdition is { } e ? $"{PreviousRouteId}:{e.ToString(System.Globalization.CultureInfo.InvariantCulture)}" : PreviousRouteId;
}

/// <summary>The route formats this build knows, in the order the Export dialog lists them.</summary>
public sealed class RouteFormatRegistry
{
    private readonly List<IRouteFormat> _formats;

    /// <summary>Creates a registry of <paramref name="formats"/>.</summary>
    /// <param name="formats">The formats, in display order.</param>
    public RouteFormatRegistry(IEnumerable<IRouteFormat> formats)
    {
        ArgumentNullException.ThrowIfNull(formats);
        _formats = formats.ToList();
    }

    /// <summary>The registry of the built-in formats: RTZ first (the Export dialog's default), then S-421.</summary>
    public static RouteFormatRegistry Default { get; } = new([new RtzRouteFormat(), new S421RouteFormat()]);

    /// <summary>The formats.</summary>
    public IReadOnlyList<IRouteFormat> Formats => _formats;

    /// <summary>Every format's file patterns, for the Import picker.</summary>
    public IReadOnlyList<string> FilePatterns => _formats.SelectMany(f => f.FilePatterns).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>The format named <paramref name="name"/> (case-insensitive, with or without the hyphen), or <c>null</c>.</summary>
    /// <param name="name">A format name, e.g. <c>"S-421"</c> or <c>"s421"</c>.</param>
    /// <returns>The format.</returns>
    public IRouteFormat? Find(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;
        static string Key(string s) => s.Replace("-", string.Empty, StringComparison.Ordinal).Trim().ToUpperInvariant();
        var key = Key(name);
        return _formats.FirstOrDefault(f => Key(f.Name) == key);
    }

    /// <summary>Recognises a file from its first bytes.</summary>
    /// <param name="head">The start of the file, as text.</param>
    /// <returns>The format and its match, or <c>null</c> when no format recognises it.</returns>
    public (IRouteFormat Format, RouteFormatMatch Match)? Sniff(string head)
    {
        foreach (var format in _formats)
        {
            if (format.Sniff(head) is { } match)
                return (format, match);
        }
        return null;
    }

    /// <summary>Reads the start of <paramref name="path"/> for <see cref="Sniff(string)"/>.</summary>
    /// <param name="path">The file.</param>
    /// <returns>Up to the first 8 KB as text.</returns>
    public static string ReadHead(string path)
    {
        using var stream = File.OpenRead(path);
        var buffer = new byte[8192];
        var read = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
        return System.Text.Encoding.UTF8.GetString(buffer, 0, read);
    }
}
