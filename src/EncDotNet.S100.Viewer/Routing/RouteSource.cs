namespace EncDotNet.S100.Viewer.Routing;

/// <summary>
/// Where a received route came from: the exchange file it was read from and
/// the identity that file gives it (#960, #391).
/// </summary>
/// <param name="Format">The exchange format, e.g. <c>"S-421"</c> (see <see cref="RouteSourceFormats"/>).</param>
/// <param name="FormatVersion">The format's version as the file declares it, e.g. <c>"1.0"</c>, or <c>null</c>.</param>
/// <param name="FilePath">The file the route was read from.</param>
/// <param name="RouteId">The file's route identifier (S-421 <c>routeID</c>), or <c>null</c> when it has none.</param>
/// <param name="Edition">The route's edition (S-421 <c>routeEditionNo</c>), or <c>null</c>.</param>
/// <param name="Status">The raw route status code (S-421 <c>routeInfoStatus</c>, 1 = Initial, 2 = Planned…), or <c>null</c>.</param>
public sealed record RouteSource(
    string Format,
    string? FormatVersion,
    string FilePath,
    string? RouteId,
    int? Edition,
    int? Status)
{
    /// <summary>The file name without its directory.</summary>
    public string FileName => Path.GetFileName(FilePath);

    /// <summary>
    /// The key the local check-settings layer is kept under: route ID and
    /// edition, else the file path when the file gives the route no ID.
    /// </summary>
    public string CheckSettingsKey => RouteId is { Length: > 0 } id
        ? $"{id}|{Edition?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-"}"
        : $"file|{FilePath}";
}

/// <summary>The exchange format names <see cref="RouteSource.Format"/> uses.</summary>
public static class RouteSourceFormats
{
    /// <summary>IHO S-421 route plan (IEC 63173-1).</summary>
    public const string S421 = "S-421";
}

/// <summary>
/// How the map draws a route (#391 concern 11): a display property of any
/// route, not of its kind.
/// </summary>
public enum RouteDisplayMode
{
    /// <summary>The route renderer draws the route (owned routes always; received routes on request).</summary>
    Planning,

    /// <summary>The route's dataset draws it with its own portrayal, and the route renderer draws nothing.</summary>
    Native,
}

/// <summary>An edit was attempted on a received, read-only route.</summary>
public sealed class RouteReadOnlyException : InvalidOperationException
{
    /// <summary>Creates the exception for <paramref name="routeId"/>.</summary>
    /// <param name="routeId">The read-only route's id.</param>
    public RouteReadOnlyException(string routeId)
        : base($"Route '{routeId}' is a received route and is read-only; copy it to edit.")
    {
        RouteId = routeId;
    }

    /// <summary>The read-only route's id.</summary>
    public string RouteId { get; }
}
