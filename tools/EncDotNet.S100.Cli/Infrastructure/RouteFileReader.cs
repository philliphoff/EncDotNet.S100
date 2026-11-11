using System.Text;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.S421;
using EncDotNet.S100.Datasets.S421.DataModel;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Routes.Rtz;

namespace EncDotNet.S100.Cli.Infrastructure;

/// <summary>A route read from an RTZ or S-421 file, as the route check takes it.</summary>
/// <param name="Path">The file it was read from.</param>
/// <param name="Format"><c>rtz</c> or <c>s421</c>.</param>
/// <param name="Version">The RTZ version, or the S-421 route format version; <c>null</c> when the file gives none.</param>
/// <param name="RouteId">The S-421 <c>routeID</c>; <c>null</c> for RTZ, which has none.</param>
/// <param name="Name">The route's name, or <c>null</c>.</param>
/// <param name="Waypoints">The waypoint positions in order.</param>
/// <param name="Plan">
/// The route's passage plan, from its format package's conversion
/// (<see cref="RtzPassagePlan"/>, <see cref="S421PassagePlan"/>): the file's
/// draft, speed, required UKC and cross-track limits per leg, and its departure.
/// </param>
internal sealed record RouteFile(
    string Path,
    string Format,
    string? Version,
    string? RouteId,
    string? Name,
    IReadOnlyList<GeoPosition> Waypoints,
    PassagePlan Plan)
{
    /// <summary>The file's departure (its planning schedule's first departure), or <c>null</c>.</summary>
    public DateTimeOffset? Departure => Plan.Departure;
}

/// <summary>The file isn't a route the CLI can read.</summary>
internal sealed class RouteFileException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Reads an RTZ (IEC 61174 Annex S) or S-421 route file. Each format's
/// package converts the route to a <see cref="PassagePlan"/>, with that
/// format's semantics (<see cref="RtzPassagePlan"/>, <see cref="S421PassagePlan"/>).
/// </summary>
internal static class RouteFileReader
{
    private const int SniffLength = 8192;

    /// <summary>Reads the route in <paramref name="path"/>.</summary>
    /// <param name="path">An RTZ file (any extension; recognised by its root and namespace) or an S-421 GML dataset.</param>
    /// <returns>The route.</returns>
    /// <exception cref="RouteFileException">The file is missing, isn't a route, or has fewer than two waypoints.</exception>
    public static RouteFile Read(string path)
    {
        if (!File.Exists(path))
            throw new RouteFileException($"Route file not found: {path}");

        var route = RtzVersions.Sniff(Head(path)) is { } version ? ReadRtz(path, version) : ReadS421(path);
        if (route.Waypoints.Count < 2)
            throw new RouteFileException($"The route in {path} has fewer than two waypoints.");
        return route;
    }

    private static string Head(string path)
    {
        using var stream = File.OpenRead(path);
        var buffer = new byte[SniffLength];
        var read = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
        return Encoding.UTF8.GetString(buffer, 0, read);
    }

    private static RouteFile ReadRtz(string path, string version)
    {
        if (!RtzVersions.IsSupported(version))
            throw new RouteFileException($"RTZ {version} isn't supported (supported: {string.Join(", ", RtzVersions.Supported)}).");

        RtzRoute rtz;
        try
        {
            rtz = RtzReader.Read(path);
        }
        catch (Exception ex) when (ex is FormatException or System.Xml.XmlException or IOException)
        {
            throw new RouteFileException($"Not a readable RTZ route: {ex.Message}", ex);
        }

        return new RouteFile(
            path,
            "rtz",
            rtz.Version,
            RouteId: null,
            string.IsNullOrWhiteSpace(rtz.Info.RouteName) ? null : rtz.Info.RouteName,
            rtz.Waypoints.Select(w => w.Position).ToList(),
            rtz.ToPassagePlan());
    }

    private static RouteFile ReadS421(string path)
    {
        S421Route s421;
        try
        {
            s421 = S421RoutePlan.From(S421Dataset.Open(path), out _).Route;
        }
        catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException or IOException or System.Xml.XmlException or FormatException)
        {
            throw new RouteFileException($"Not an RTZ or S-421 route: {ex.Message}", ex);
        }

        return new RouteFile(
            path,
            "s421",
            s421.FormatVersion,
            s421.RouteId,
            s421.Info?.Name,
            s421.Waypoints.Select(w => w.Position).ToList(),
            s421.ToPassagePlan());
    }
}
